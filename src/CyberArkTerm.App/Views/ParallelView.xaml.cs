using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Vue parallèle : jusqu'à 8 sessions SSH affichées ensemble, en grille. Leurs terminaux y sont déplacés (comme pour
/// un onglet détaché) ; les sessions continuent et retrouvent leur onglet en sortant de la vue.
/// Avec la saisie simultanée, ce qui est tapé dans une session cochée est aussi envoyé aux autres sessions cochées et
/// connectées. Désactivée à l'ouverture, elle est signalée par un bandeau et un cadre orange autour des sessions qui
/// reçoivent la saisie ; une session ajoutée pendant qu'elle est active n'est pas cochée. Chaque touche est encodée
/// par la session qui la reçoit, selon l'état de son terminal (shell, vim...). La molette n'est pas recopiée, et un
/// collage de plusieurs lignes vers plusieurs sessions demande confirmation.
/// </summary>
public partial class ParallelView : UserControl
{
    private static readonly Brush IncludedBorder = Frozen(0xF5, 0x9E, 0x0B);
    private static readonly Brush ActiveBorder = Frozen(0x15, 0x65, 0xC0);
    private static readonly Brush IdleBorder = Frozen(0x3A, 0x41, 0x4A);
    private static readonly Brush ActiveHeader = Frozen(0x23, 0x3A, 0x55);
    private static readonly Brush IdleHeader = Frozen(0x26, 0x2B, 0x31);
    private static readonly Brush Connected = Frozen(0x4C, 0xC3, 0x5A);
    private static readonly Brush Connecting = Frozen(0xF5, 0x9E, 0x0B);
    private static readonly Brush Closed = Frozen(0xE5, 0x48, 0x48);

    private readonly List<Pane> _panes = [];
    private Pane? _active;
    private Pane? _zoomed;

    public ParallelView()
    {
        InitializeComponent();
        UpdateLayoutAndState();
    }

    /// <summary>« ✕ » d'une session : elle retourne dans son onglet.</summary>
    public event Action<SshSession>? RemoveRequested;

    public event Action? CloseRequested;

    public event Action? ChooseRequested;

    /// <summary>« Envoyer des fichiers » : vers les sessions de la vue.</summary>
    public event Action? SendFilesRequested;

    /// <summary>« Fenêtre séparée » ou « Ramener dans l'onglet ».</summary>
    public event Action? DetachRequested;

    /// <summary>Vue dans une fenêtre séparée : le bouton propose de la ramener dans l'onglet.</summary>
    public bool IsDetached
    {
        get => _detached;
        set
        {
            _detached = value;
            DetachButton.Content = value ? Strings.DetachedReattach : Strings.ParallelDetach;
        }
    }

    private bool _detached;

    /// <summary>Session où l'utilisateur travaille (l'onglet Fichiers la suit).</summary>
    public event Action<SshSession>? ActiveSessionChanged;

    /// <summary>Confirmation d'un collage de plusieurs lignes vers plusieurs sessions (lignes, sessions) ; remplaçable (tests).</summary>
    internal Func<int, int, bool>? ConfirmPaste { get; set; }

    /// <summary>Session connectée, qui peut recevoir la saisie simultanée ; remplaçable (tests).</summary>
    internal Func<SshSession, bool> IsConnected { get; set; } = session => session.State == RemoteSessionState.Connected;

    /// <summary>Envoi d'une saisie à une session ; remplaçable (tests).</summary>
    internal Action<SshSessionView, TerminalInput> Deliver { get; set; } = (view, input) => view.Send(input);

    public IReadOnlyList<SshSession> Sessions => _panes.Select(p => p.View.Session).ToList();

    public SshSession? ActiveSession => _active?.View.Session;

    public bool IsFull => _panes.Count >= ParallelLayout.MaxSessions;

    public bool Broadcast
    {
        get => BroadcastToggle.IsChecked == true;
        set
        {
            BroadcastToggle.IsChecked = value;
            UpdateLayoutAndState();
        }
    }

    public bool Contains(SshSession session) => _panes.Any(p => ReferenceEquals(p.View.Session, session));

    public SshSessionView? ViewOf(SshSession session) => Find(session)?.View;

    /// <summary>Session incluse dans la saisie simultanée (case cochée).</summary>
    public bool IsIncluded(SshSession session) => Find(session)?.Included == true;

    public void SetIncluded(SshSession session, bool included)
    {
        if (Find(session) is { } pane)
        {
            pane.IncludeBox.IsChecked = included;
            UpdateLayoutAndState();
        }
    }

    /// <summary>Place un terminal dans la vue (au plus 8).</summary>
    public void Add(SshSessionView view)
    {
        if (IsFull || Contains(view.Session))
        {
            return;
        }

        // Saisie simultanée déjà active : la nouvelle session ne la reçoit qu'une fois cochée.
        var pane = new Pane(this, view) { Included = !Broadcast };
        _panes.Add(pane);
        view.InputRouter = Route;
        view.Session.StateChanged += pane.OnStateChanged;
        _active ??= pane;
        _zoomed = null;
        UpdateLayoutAndState();
    }

    /// <summary>Retire un terminal de la vue et le rend (pour son onglet).</summary>
    public SshSessionView? Remove(SshSession session)
    {
        if (Find(session) is not { } pane)
        {
            return null;
        }

        pane.Detach();
        pane.View.InputRouter = null;
        session.StateChanged -= pane.OnStateChanged;
        _panes.Remove(pane);
        PanesGrid.Children.Remove(pane);
        if (ReferenceEquals(_zoomed, pane))
        {
            _zoomed = null;
        }

        if (ReferenceEquals(_active, pane))
        {
            _active = _panes.FirstOrDefault();
            if (_active is not null)
            {
                ActiveSessionChanged?.Invoke(_active.View.Session);
            }
        }

        UpdateLayoutAndState();
        return pane.View;
    }

    public void FocusActive() => _active?.View.FocusTerminal();

    /// <summary>Une seule session à l'écran (ou toutes à nouveau).</summary>
    public void ToggleZoom(SshSession session)
    {
        var pane = Find(session);
        _zoomed = ReferenceEquals(_zoomed, pane) ? null : pane;
        UpdateLayoutAndState();
        pane?.View.FocusTerminal();
    }

    private Pane? Find(SshSession session) => _panes.FirstOrDefault(p => ReferenceEquals(p.View.Session, session));

    /// <summary>Aiguillage de la saisie faite dans un terminal de la vue.</summary>
    private void Route(SshSessionView source, TerminalInput input)
    {
        if (_panes.FirstOrDefault(p => ReferenceEquals(p.View, source)) is not { } from)
        {
            source.Send(input);
            return;
        }

        var targets = ParallelLayout.Targets(from, _panes, Broadcast, p => p.Included, p => IsConnected(p.View.Session));
        if (targets.Count > 1 && input.IsMultiLinePaste)
        {
            int lines = input.Text.TrimEnd('\r', '\n').Replace("\r\n", "\n").Split('\n', '\r').Length;
            if (!(ConfirmPaste ?? AskPaste)(lines, targets.Count))
            {
                return;
            }
        }

        foreach (var pane in targets)
        {
            Deliver(pane.View, input);
        }
    }

    private bool AskPaste(int lines, int sessions) =>
        MessageBox.Show(Window.GetWindow(this), Text.Format(Strings.ParallelPasteConfirm, lines, sessions), Strings.ParallelTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void OnBroadcast(object sender, RoutedEventArgs e)
    {
        UpdateLayoutAndState();
        FocusActive();
    }

    private void OnChoose(object sender, RoutedEventArgs e) => ChooseRequested?.Invoke();

    private void OnSendFiles(object sender, RoutedEventArgs e) => SendFilesRequested?.Invoke();

    private void OnDetach(object sender, RoutedEventArgs e) => DetachRequested?.Invoke();

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void Activate(Pane pane)
    {
        if (ReferenceEquals(_active, pane))
        {
            return;
        }

        _active = pane;
        UpdateLayoutAndState();
        ActiveSessionChanged?.Invoke(pane.View.Session);
    }

    /// <summary>Grille, cadres, bandeau de la saisie simultanée.</summary>
    private void UpdateLayoutAndState()
    {
        List<Pane> shown = _zoomed is not null ? [_zoomed] : _panes;
        var (rows, columns) = ParallelLayout.For(shown.Count);
        if (PanesGrid.RowDefinitions.Count != rows || PanesGrid.ColumnDefinitions.Count != columns)
        {
            PanesGrid.RowDefinitions.Clear();
            PanesGrid.ColumnDefinitions.Clear();
            for (int r = 0; r < rows; r++)
            {
                PanesGrid.RowDefinitions.Add(new RowDefinition());
            }

            for (int c = 0; c < columns; c++)
            {
                PanesGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }
        }

        foreach (var pane in _panes)
        {
            int index = shown.IndexOf(pane);
            pane.Visibility = index >= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (index >= 0)
            {
                Grid.SetRow(pane, index / columns);
                Grid.SetColumn(pane, index % columns);
            }

            if (!PanesGrid.Children.Contains(pane))
            {
                PanesGrid.Children.Add(pane);
            }

            pane.Update(Broadcast, ReferenceEquals(pane, _active), ReferenceEquals(pane, _zoomed));
        }

        TitleText.Text = Text.Format(Strings.ParallelHeading, _panes.Count, ParallelLayout.MaxSessions);
        var receiving = _panes.Where(p => p.Included && IsConnected(p.View.Session)).ToList();
        BroadcastBanner.Visibility = Broadcast ? Visibility.Visible : Visibility.Collapsed;
        BroadcastText.Text = Text.Format(Strings.ParallelBroadcastBanner, receiving.Count,
            string.Join(", ", receiving.Select(p => p.View.Session.Label)));
        if (_zoomed is not null && receiving.Count(p => !ReferenceEquals(p, _zoomed)) is var hidden and > 0)
        {
            // Une session agrandie masque les autres : elles reçoivent quand même la saisie.
            BroadcastText.Text += " " + Text.Format(Strings.ParallelBroadcastHidden, hidden);
        }
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Une session de la vue : en-tête (état, nom, case de la saisie simultanée, agrandir, retirer) et terminal.</summary>
    private sealed class Pane : Border
    {
        private readonly ParallelView _owner;
        private readonly DockPanel _dock = new();
        private readonly Border _header = new() { Padding = new Thickness(6, 2, 2, 2) };
        private readonly Ellipse _state = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly ToggleButton _zoom;

        public Pane(ParallelView owner, SshSessionView view)
        {
            _owner = owner;
            View = view;
            BorderThickness = new Thickness(2);
            Margin = new Thickness(2);
            IncludeBox = new CheckBox
            {
                Content = Strings.ParallelInclude,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                ToolTip = Strings.ParallelIncludeTip,
                Focusable = false,
            };
            IncludeBox.Click += (_, _) =>
            {
                owner.UpdateLayoutAndState();
                View.FocusTerminal();
            };
            _zoom = new ToggleButton
            {
                Content = "⤢",
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = Strings.ParallelZoomTip,
                Focusable = false,
            };
            _zoom.Click += (_, _) => owner.ToggleZoom(View.Session);
            var remove = new Button
            {
                Content = "✕",
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = Strings.ParallelRemoveTip,
                Focusable = false,
            };
            remove.Click += (_, _) => owner.RemoveRequested?.Invoke(View.Session);

            var title = new TextBlock
            {
                Text = view.Session.Label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { IncludeBox, _zoom, remove } };
            DockPanel.SetDock(buttons, Dock.Right);
            _header.Child = new DockPanel { Children = { buttons, _state, title } };
            DockPanel.SetDock(_state, Dock.Left);
            DockPanel.SetDock(_header, Dock.Top);
            _header.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    owner.ToggleZoom(View.Session);
                }
                else
                {
                    View.FocusTerminal();
                }
            };
            _dock.Children.Add(_header);
            _dock.Children.Add(view);
            Child = _dock;
            OnStateChanged();
        }

        public SshSessionView View { get; }

        public CheckBox IncludeBox { get; }

        public bool Included
        {
            get => IncludeBox.IsChecked == true;
            init => IncludeBox.IsChecked = value;
        }

        /// <summary>Rend le terminal (il retourne dans son onglet).</summary>
        public void Detach() => _dock.Children.Remove(View);

        public void OnStateChanged()
        {
            var state = View.Session.State;
            _state.Fill = state switch
            {
                RemoteSessionState.Connected => Connected,
                RemoteSessionState.Connecting => Connecting,
                _ => Closed,
            };
            _state.ToolTip = state switch
            {
                RemoteSessionState.Connected => Strings.ParallelStateConnected,
                RemoteSessionState.Connecting => Strings.ParallelStateConnecting,
                _ => Strings.ParallelStateClosed,
            };
            if (Parent is not null)
            {
                _owner.UpdateLayoutAndState();
            }
        }

        public void Update(bool broadcast, bool active, bool zoomed)
        {
            IncludeBox.Visibility = broadcast ? Visibility.Visible : Visibility.Collapsed;
            _zoom.IsChecked = zoomed;
            BorderBrush = broadcast && Included ? IncludedBorder : active ? ActiveBorder : IdleBorder;
            _header.Background = active ? ActiveHeader : IdleHeader;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            _owner.Activate(this);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            _owner.Activate(this);
        }
    }
}
