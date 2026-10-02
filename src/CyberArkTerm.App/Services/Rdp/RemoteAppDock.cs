using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CyberArkTerm.Core.Diagnostics;

namespace CyberArkTerm.App.Services.Rdp;

/// <summary>
/// Fenêtre d'une application distante (RemoteApp) affichée dans l'onglet, sur le thread de la connexion.
/// Le contrôle Bureau à distance crée les fenêtres de l'application dans ce processus, sur un thread à lui, comme des
/// fenêtres de premier niveau qu'il place où le serveur les met. La fenêtre principale (la première qui n'est ni un
/// menu ni une boîte de dialogue) est rattachée au conteneur de l'onglet et en prend toute la place ; les autres
/// (menus, boîtes de dialogue) restent à part, au-dessus, à l'endroit voulu par le serveur.
/// Vérifié sur un vrai serveur : rendu, souris (menu contextuel ouvert sous le pointeur), clavier, redimensionnement,
/// onglet masqué puis réaffiché.
/// </summary>
internal sealed class RemoteAppDock : IDisposable
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsPopup = 0x80000000;
    private const long WsChild = 0x40000000;
    private const long WsVisible = 0x10000000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;
    private const long WsExToolWindow = 0x00000080;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwRestore = 9;

    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly Control _container;
    private readonly Action<bool> _changed;
    private readonly string _label;
    private readonly List<IntPtr> _windows = [];
    private readonly System.Windows.Forms.Timer _timer;
    private IntPtr _docked;
    private bool _shown;

    /// <param name="container">Conteneur du contrôle dans l'onglet (thread de la connexion).</param>
    /// <param name="changed">Fenêtre affichée dans l'onglet (vrai) ou plus (faux).</param>
    public RemoteAppDock(Control container, Action<bool> changed, string label)
    {
        _container = container;
        _changed = changed;
        _label = label;
        // Filet de sécurité : fenêtre fermée, déplacée ou réduite par le serveur.
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
    }

    /// <summary>Fenêtre de l'application signalée par le contrôle (OnRemoteWindowDisplayed).</summary>
    public void Add(IntPtr window)
    {
        if (window != IntPtr.Zero && !_windows.Contains(window))
        {
            _windows.Add(window);
        }

        Update();
    }

    /// <summary>Donne le clavier à la fenêtre de l'application ; faux s'il n'y en a pas dans l'onglet.</summary>
    public bool Focus()
    {
        if (_docked == IntPtr.Zero || !IsWindow(_docked))
        {
            return false;
        }

        var focus = GetFocus();
        if (focus != _docked && !IsChild(_docked, focus))
        {
            SetFocus(_docked);
        }

        return true;
    }

    /// <summary>Remet la fenêtre de l'application à la taille de l'onglet.</summary>
    public void Fit()
    {
        if (_docked == IntPtr.Zero || !_container.IsHandleCreated || !IsWindow(_docked))
        {
            return;
        }

        if (IsIconic(_docked))
        {
            // Réduite par le serveur : dans un onglet, elle n'a nulle part où aller.
            ShowWindow(_docked, SwRestore);
        }

        GetClientRect(_container.Handle, out var target);
        GetWindowRect(_docked, out var rect);
        var position = new Rect { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
        MapWindowPoints(IntPtr.Zero, _container.Handle, ref position, 2);
        if (position.Left != 0 || position.Top != 0 || rect.Width != target.Width || rect.Height != target.Height)
        {
            SetWindowPos(_docked, IntPtr.Zero, 0, 0, Math.Max(target.Width, 1), Math.Max(target.Height, 1), SwpNoZOrder | SwpNoActivate);
        }
    }

    /// <summary>
    /// Fin de la connexion : la fenêtre quitte le conteneur (qui va être détruit) pour une fenêtre sans affichage ;
    /// le contrôle la détruit lui-même, sur son thread.
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        if (_docked != IntPtr.Zero && IsWindow(_docked))
        {
            ShowWindow(_docked, 0);
            SetParent(_docked, MessageOnlyParent);
        }

        _docked = IntPtr.Zero;
        _windows.Clear();
    }

    private void Update()
    {
        _windows.RemoveAll(w => !IsWindow(w));
        if (_docked != IntPtr.Zero && !IsWindow(_docked))
        {
            DebugLog.Write("rdp", $"{_label} : fenêtre de l'application dans l'onglet fermée");
            _docked = IntPtr.Zero;
        }

        if (_docked == IntPtr.Zero && _windows.FirstOrDefault(IsMainWindow) is var main && main != IntPtr.Zero)
        {
            Dock(main);
        }

        Fit();
        // Affichée dans l'onglet tant qu'elle existe et que le serveur ne l'a pas masquée.
        bool shown = _docked != IntPtr.Zero && (Style(_docked) & WsVisible) != 0;
        if (shown != _shown)
        {
            _shown = shown;
            _changed(shown);
        }
    }

    private void Dock(IntPtr window)
    {
        DebugLog.Write("rdp", $"{_label} : fenêtre de l'application « {Title(window)} » affichée dans l'onglet");
        long style = Style(window);
        SetParent(window, _container.Handle);
        SetWindowLongPtr(window, GwlStyle, new IntPtr((style & ~WsPopup) | WsChild));
        GetClientRect(_container.Handle, out var target);
        // Au-dessus de la fenêtre du contrôle, inutile en application distante.
        SetWindowPos(window, IntPtr.Zero, 0, 0, Math.Max(target.Width, 1), Math.Max(target.Height, 1),
            SwpFrameChanged | SwpShowWindow | SwpNoActivate);
        _docked = window;
    }

    /// <summary>
    /// Fenêtre principale : visible, de premier niveau, ni fenêtre d'outil (menus, boîtes de dialogue du serveur) ni
    /// fenêtre surgissante, sauf si elle peut être réduite ou agrandie.
    /// </summary>
    private static bool IsMainWindow(IntPtr window)
    {
        long style = Style(window);
        long exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        return IsWindowVisible(window) && (style & WsChild) == 0 && (exStyle & WsExToolWindow) == 0
               && ((style & WsPopup) == 0 || (style & (WsMinimizeBox | WsMaximizeBox)) != 0);
    }

    private static long Style(IntPtr window) => GetWindowLongPtr(window, GwlStyle).ToInt64();

    private static string Title(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Rect rect, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
}
