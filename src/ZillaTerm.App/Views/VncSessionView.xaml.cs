using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;
using ZillaTerm.Core.Vnc;

namespace ZillaTerm.App.Views;

/// <summary>
/// Onglet VNC : écran distant (ajusté à l'onglet ou en taille réelle), clavier et souris transmis au serveur,
/// presse-papiers texte échangé seulement à la demande (boutons), jamais automatiquement.
/// </summary>
public partial class VncSessionView : UserControl
{
    private static readonly uint[] Modifiers =
        [VncKeys.ControlLeft, VncKeys.ControlRight, VncKeys.AltLeft, VncKeys.AltRight];

    private readonly object _dirtyLock = new();
    private readonly HashSet<uint> _pressed = [];
    private readonly Dictionary<Key, uint> _combos = [];
    private RfbClient? _client;
    private WriteableBitmap? _bitmap;
    private VncRect _dirty;
    private bool _renderQueued;
    private byte _buttons;
    private (int X, int Y, byte Buttons) _lastPointer = (-1, -1, 0);
    private string? _remoteText;

    public VncSessionView(VncSession session)
    {
        InitializeComponent();
        Session = session;
        session.StateChanged += () => Dispatcher.BeginInvoke(UpdateState);
        session.ClientChanged += client =>
        {
            if (Dispatcher.CheckAccess())
            {
                Attach(client);
            }
            else
            {
                Dispatcher.Invoke(() => Attach(client));
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        PreviewTextInput += OnPreviewTextInput;
        LostKeyboardFocus += (_, _) => ReleaseAll();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Focus());
            }
            else
            {
                ReleaseAll();
            }
        };
        UpdateState();
    }

    public VncSession Session { get; }

    // ===================== État et image =====================

    private void Attach(RfbClient client)
    {
        _client = client;
        _remoteText = null;
        CopyRemoteButton.IsEnabled = false;
        _pressed.Clear();
        _combos.Clear();
        _buttons = 0;
        client.Updated += rect =>
        {
            if (ReferenceEquals(client, _client))
            {
                QueueRender(rect);
            }
        };
        client.Resized += () =>
        {
            if (ReferenceEquals(client, _client))
            {
                QueueRender(new VncRect(0, 0, int.MaxValue / 2, int.MaxValue / 2));
            }
        };
        client.ClipboardReceived += text => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(client, _client))
            {
                _remoteText = text;
                CopyRemoteButton.IsEnabled = text.Length > 0;
                StatusLine.Text = Text.Format(Strings.VncClipboardReceived, text.Length);
            }
        });
        client.Bell += () => Dispatcher.BeginInvoke(() => System.Media.SystemSounds.Beep.Play());
        _bitmap = null;
        QueueRender(new VncRect(0, 0, int.MaxValue / 2, int.MaxValue / 2));
    }

    private void QueueRender(VncRect rect)
    {
        lock (_dirtyLock)
        {
            _dirty = _dirty.Union(rect);
            if (_renderQueued)
            {
                return;
            }

            _renderQueued = true;
        }

        Dispatcher.BeginInvoke(Render, DispatcherPriority.Render);
    }

    /// <summary>Copie la zone modifiée de l'image reçue dans l'image affichée (une fois par passage de rendu au plus).</summary>
    private void Render()
    {
        VncRect dirty;
        lock (_dirtyLock)
        {
            dirty = _dirty;
            _dirty = default;
            _renderQueued = false;
        }

        if (_client is not { } client)
        {
            return;
        }

        var fb = client.Framebuffer;
        lock (fb.Sync)
        {
            if (_bitmap is null || _bitmap.PixelWidth != fb.Width || _bitmap.PixelHeight != fb.Height)
            {
                _bitmap = new WriteableBitmap(fb.Width, fb.Height, 96, 96, PixelFormats.Bgr32, null);
                Screen.Source = _bitmap;
                dirty = new VncRect(0, 0, fb.Width, fb.Height);
                ApplyFit();
            }

            int x = Math.Clamp(dirty.X, 0, fb.Width), y = Math.Clamp(dirty.Y, 0, fb.Height);
            int w = Math.Min(dirty.X + dirty.Width, fb.Width) - x, h = Math.Min(dirty.Y + dirty.Height, fb.Height) - y;
            if (w > 0 && h > 0)
            {
                _bitmap.WritePixels(new Int32Rect(x, y, w, h), fb.Pixels, fb.Stride, x, y);
            }
        }

        if (Session.IsConnected)
        {
            StatusLine.Text = Text.Format(Strings.VncStatus, client.DesktopName, Address, fb.Width, fb.Height, client.ProtocolVersion);
        }
    }

    private string Address => Session.Port == RfbClient.DefaultPort ? Session.Host : $"{Session.Host}:{Session.Port}";

    private void UpdateState()
    {
        var state = Session.State;
        Overlay.Visibility = state == VncSessionState.Connected ? Visibility.Collapsed : Visibility.Visible;
        ReconnectButton.Visibility = state is VncSessionState.Failed or VncSessionState.Closed ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new UIElement[] { CtrlAltDelButton, SendClipboardButton })
        {
            button.IsEnabled = state == VncSessionState.Connected;
        }

        switch (state)
        {
            case VncSessionState.Connecting:
                OverlayText.Text = Text.Format(Strings.VncConnecting, Address);
                OverlayDetail.Text = "";
                StatusLine.Text = OverlayText.Text;
                break;
            case VncSessionState.Connected:
                Focus();
                break;
            case VncSessionState.Failed:
                OverlayText.Text = Strings.VncFailed;
                OverlayDetail.Text = Session.Error ?? "";
                StatusLine.Text = OverlayText.Text;
                break;
            case VncSessionState.Closed:
                OverlayText.Text = Strings.VncClosedText;
                OverlayDetail.Text = Session.Error ?? "";
                StatusLine.Text = OverlayText.Text;
                ReleaseAll();
                break;
        }
    }

    private void OnReconnect(object sender, RoutedEventArgs e) => _ = Session.ConnectAsync();

    private void OnFitChanged(object sender, RoutedEventArgs e) => ApplyFit();

    /// <summary>Ajusté : l'écran est réduit pour tenir dans l'onglet ; sinon taille réelle, avec défilement.</summary>
    private void ApplyFit()
    {
        bool fit = FitButton.IsChecked == true;
        Screen.Stretch = fit ? Stretch.Uniform : Stretch.None;
        Screen.StretchDirection = StretchDirection.DownOnly;
        Scroller.HorizontalScrollBarVisibility = Scroller.VerticalScrollBarVisibility =
            fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    // ===================== Souris =====================

    private bool Map(MouseEventArgs e, out int x, out int y)
    {
        x = y = 0;
        if (_client is not { } client || !Session.IsConnected || Screen.ActualWidth <= 0 || Screen.ActualHeight <= 0)
        {
            return false;
        }

        var p = e.GetPosition(Screen);
        var fb = client.Framebuffer;
        x = (int)(p.X * fb.Width / Screen.ActualWidth);
        y = (int)(p.Y * fb.Height / Screen.ActualHeight);
        return true;
    }

    private void SendPointer(int x, int y)
    {
        if (_lastPointer != (x, y, _buttons))
        {
            _lastPointer = (x, y, _buttons);
            _client?.SendPointer(x, y, _buttons);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Map(e, out var x, out var y))
        {
            SendPointer(x, y);
        }
    }

    private void OnMouseButton(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (!Map(e, out var x, out var y))
        {
            return;
        }

        e.Handled = true;
        _buttons = (byte)((e.LeftButton == MouseButtonState.Pressed ? 1 : 0)
                          | (e.MiddleButton == MouseButtonState.Pressed ? 2 : 0)
                          | (e.RightButton == MouseButtonState.Pressed ? 4 : 0));
        if (_buttons != 0)
        {
            Screen.CaptureMouse();
        }
        else
        {
            Screen.ReleaseMouseCapture();
        }

        SendPointer(x, y);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Map(e, out var x, out var y))
        {
            e.Handled = true;
            for (int i = 0; i < Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine); i++)
            {
                _client?.SendWheel(x, y, e.Delta);
            }
        }
    }

    // ===================== Clavier =====================

    private void Press(uint keysym)
    {
        _pressed.Add(keysym);
        _client?.SendKey(keysym, down: true);
    }

    private void Release(uint keysym)
    {
        _pressed.Remove(keysym);
        _client?.SendKey(keysym, down: false);
    }

    /// <summary>Focus perdu ou session fermée : rien ne doit rester enfoncé sur le serveur.</summary>
    private void ReleaseAll()
    {
        foreach (var keysym in _pressed.ToList())
        {
            Release(keysym);
        }

        _combos.Clear();
        if (_buttons != 0 && _lastPointer.X >= 0)
        {
            _buttons = 0;
            SendPointer(_lastPointer.X, _lastPointer.Y);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Session.IsConnected)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Special(key) is { } keysym)
        {
            Press(keysym);
            e.Handled = true;
            return;
        }

        // Ctrl ou Alt + lettre ou chiffre (pas AltGr, qui produit un caractère) : la touche elle-même.
        var modifiers = Keyboard.Modifiers;
        bool altGr = Keyboard.IsKeyDown(Key.RightAlt) && modifiers.HasFlag(ModifierKeys.Control);
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0 && !altGr && Base(key) is { } baseKey)
        {
            _combos[key] = baseKey;
            Press(baseKey);
            e.Handled = true;
        }

        // Les autres touches arrivent en caractères (TextInput), disposition du clavier de ce poste comprise.
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!Session.IsConnected)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Special(key) is { } keysym)
        {
            Release(keysym);
            e.Handled = true;
        }
        else if (_combos.Remove(key, out var baseKey))
        {
            Release(baseKey);
            e.Handled = true;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!Session.IsConnected || e.Text.Length == 0)
        {
            return;
        }

        e.Handled = true;
        // Caractère obtenu avec AltGr : Ctrl et Alt sont relâchés le temps de l'envoyer.
        var held = Modifiers.Where(_pressed.Contains).ToList();
        foreach (var keysym in held)
        {
            _client?.SendKey(keysym, down: false);
        }

        foreach (var c in e.Text)
        {
            var keysym = VncKeys.FromChar(c);
            if (keysym != 0)
            {
                _client?.SendKey(keysym, down: true);
                _client?.SendKey(keysym, down: false);
            }
        }

        foreach (var keysym in held)
        {
            _client?.SendKey(keysym, down: true);
        }
    }

    private static uint? Special(Key key) => key switch
    {
        Key.Back => VncKeys.BackSpace,
        Key.Tab => VncKeys.Tab,
        Key.Enter => VncKeys.Return,
        Key.Escape => VncKeys.Escape,
        Key.PageUp => VncKeys.PageUp,
        Key.PageDown => VncKeys.PageDown,
        Key.End => VncKeys.End,
        Key.Home => VncKeys.Home,
        Key.Left => VncKeys.Left,
        Key.Up => VncKeys.Up,
        Key.Right => VncKeys.Right,
        Key.Down => VncKeys.Down,
        Key.Insert => VncKeys.Insert,
        Key.Delete => VncKeys.Delete,
        Key.Pause => VncKeys.Pause,
        Key.Scroll => VncKeys.ScrollLock,
        Key.Snapshot => VncKeys.Print,
        Key.Apps => VncKeys.Menu,
        >= Key.F1 and <= Key.F24 => VncKeys.Function(key - Key.F1 + 1),
        Key.LeftShift => VncKeys.ShiftLeft,
        Key.RightShift => VncKeys.ShiftRight,
        Key.LeftCtrl => VncKeys.ControlLeft,
        Key.RightCtrl => VncKeys.ControlRight,
        Key.LeftAlt => VncKeys.AltLeft,
        Key.RightAlt => VncKeys.AltRight,
        Key.LWin => VncKeys.SuperLeft,
        Key.RWin => VncKeys.SuperRight,
        _ => null,
    };

    private static uint? Base(Key key) => key switch
    {
        >= Key.A and <= Key.Z => 'a' + (uint)(key - Key.A),
        >= Key.D0 and <= Key.D9 => '0' + (uint)(key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => '0' + (uint)(key - Key.NumPad0),
        Key.Space => ' ',
        _ => null,
    };

    // ===================== Barre de la session =====================

    private void OnCtrlAltDel(object sender, RoutedEventArgs e)
    {
        if (_client is not { } client || !Session.IsConnected)
        {
            return;
        }

        // Sur certaines consoles de machines virtuelles, Ctrl+Alt+Suppr redémarre la machine.
        if (!ConfirmDialog.Confirm(Window.GetWindow(this), new ConfirmRequest
            {
                Title = Strings.VncCtrlAltDelAction,
                Heading = Text.Format(Strings.VncCtrlAltDelHeading, Session.Label),
                Message = Strings.VncCtrlAltDelMessage,
                Kind = ConfirmKind.Warning,
                Actions = [Strings.VncCtrlAltDelAction],
            }) || !Session.IsConnected)
        {
            return;
        }

        uint[] keys = [VncKeys.ControlLeft, VncKeys.AltLeft, VncKeys.Delete];
        foreach (var k in keys)
        {
            client.SendKey(k, down: true);
        }

        foreach (var k in keys.Reverse())
        {
            client.SendKey(k, down: false);
        }

        Focus();
    }

    /// <summary>Envoie le texte du presse-papiers de ce poste au serveur (à la demande seulement).</summary>
    private void OnSendClipboard(object sender, RoutedEventArgs e)
    {
        if (_client is not { } client || !Session.IsConnected)
        {
            return;
        }

        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            text = "";
        }

        if (text.Length == 0)
        {
            StatusLine.Text = Strings.VncClipboardEmpty;
            return;
        }

        client.SendClipboard(text);
        StatusLine.Text = Text.Format(Strings.VncClipboardSent, text.Length);
        Focus();
    }

    private void OnCopyRemote(object sender, RoutedEventArgs e)
    {
        if (_remoteText is { Length: > 0 } text)
        {
            try
            {
                Clipboard.SetText(text);
                StatusLine.Text = Text.Format(Strings.VncClipboardCopied, text.Length);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Presse-papiers occupé par une autre application : nouvel essai possible.
            }
        }

        Focus();
    }
}
