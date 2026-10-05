using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CyberArkTerm.Core.Diagnostics;

namespace CyberArkTerm.App.Services.Rdp;

/// <summary>
/// Fenêtre d'une application distante (RemoteApp) affichée dans l'onglet, sur le thread de la connexion.
/// Le contrôle Bureau à distance crée les fenêtres de l'application dans ce processus, sur un thread à lui, comme des
/// fenêtres de premier niveau qu'il place où le serveur les met. Une fenêtre principale (ni menu ni boîte de dialogue :
/// une fenêtre qui peut être réduite ou agrandie, ou une fenêtre plein écran) est rattachée au conteneur de l'onglet
/// et en prend toute la place ; les autres (menus, boîtes de dialogue) restent à part, au-dessus, à l'endroit voulu
/// par le serveur. Si le serveur masque la fenêtre de l'onglet et en affiche une autre (passage en plein écran d'un
/// client Bureau à distance, puis retour), l'onglet affiche celle qui est visible.
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
    private const long WsMaximize = 0x01000000;
    private const long WsCaption = 0x00C00000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;
    private const long WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwRestore = 9;
    private const int WmActivate = 0x0006;
    private const int WaActive = 1;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0;
    private const uint MonitorDefaultToNearest = 2;
    private const int MaxLoggedFits = 30;

    private static readonly IntPtr MessageOnlyParent = new(-3);

    /// <summary>Expérience : comportement d'avant (pas de plein écran ni de changement de fenêtre).</summary>
    internal static bool Legacy;

    /// <summary>Expérience : rattachement suspendu (fenêtre manipulée par le test).</summary>
    internal static bool Paused;

    private readonly Control _container;
    private readonly Action<bool> _changed;
    private readonly string _label;
    private readonly List<IntPtr> _windows = [];
    private readonly List<IntPtr> _attached = [];
    private readonly Dictionary<IntPtr, string> _logged = [];
    private readonly System.Windows.Forms.Timer _timer;
    private readonly WinEventProc _onLocationChange;
    private IntPtr _docked;
    private IntPtr _hook;
    private int _hookedThread;
    private int _loggedFits;
    private bool _shown;

    /// <param name="container">Conteneur du contrôle dans l'onglet (thread de la connexion).</param>
    /// <param name="changed">Fenêtre affichée dans l'onglet (vrai) ou plus (faux).</param>
    public RemoteAppDock(Control container, Action<bool> changed, string label)
    {
        _container = container;
        _changed = changed;
        _label = label;
        _onLocationChange = OnLocationChange;
        // Filet de sécurité : fenêtre fermée, masquée, déplacée ou réduite par le serveur.
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

    /// <summary>
    /// Donne le clavier à la fenêtre de l'application ; faux s'il n'y en a pas dans l'onglet. Le serveur envoie la
    /// frappe à sa fenêtre active : rattachée à l'onglet, la fenêtre n'est plus activée par Windows, et le contrôle
    /// ne le signale plus au serveur ; l'activation lui est donc envoyée (constaté : sans elle, ni clic préalable, la
    /// frappe n'arrive pas à l'application).
    /// </summary>
    public bool Focus()
    {
        if (_docked == IntPtr.Zero || !IsWindow(_docked))
        {
            return false;
        }

        PostMessage(_docked, WmActivate, new IntPtr(WaActive), IntPtr.Zero);
        var focus = GetFocus();
        if (focus != _docked && !IsChild(_docked, focus))
        {
            SetFocus(_docked);
        }

        return true;
    }

    /// <summary>Remet la fenêtre de l'application à la taille de l'onglet (onglet redimensionné).</summary>
    public void Fit() => Fit(log: false);

    /// <summary>
    /// Fin de la connexion : les fenêtres quittent le conteneur (qui va être détruit) pour une fenêtre sans affichage ;
    /// le contrôle les détruit lui-même, sur son thread.
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        foreach (var window in _attached.Where(IsWindow))
        {
            ShowWindow(window, 0);
            SetParent(window, MessageOnlyParent);
        }

        _docked = IntPtr.Zero;
        _attached.Clear();
        _windows.Clear();
    }

    /// <param name="log">Correction d'une position donnée par le serveur (et non par l'onglet) : notée au journal.</param>
    private void Fit(bool log)
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
        if (position.Left == 0 && position.Top == 0 && rect.Width == target.Width && rect.Height == target.Height)
        {
            return;
        }

        if (log && DebugLog.Enabled && _loggedFits < MaxLoggedFits)
        {
            _loggedFits++;
            DebugLog.Write("rdp", $"{_label} : fenêtre 0x{_docked.ToInt64():X} placée par le serveur en {position.Left},{position.Top} " +
                                  $"{rect.Width}x{rect.Height}, remise à la taille de l'onglet {target.Width}x{target.Height}" +
                                  (_loggedFits == MaxLoggedFits ? " (corrections suivantes non notées)" : ""));
        }

        SetWindowPos(_docked, IntPtr.Zero, 0, 0, Math.Max(target.Width, 1), Math.Max(target.Height, 1), SwpNoZOrder | SwpNoActivate);
    }

    private void Update()
    {
        if (Paused)
        {
            return;
        }

        _windows.RemoveAll(w => !IsWindow(w));
        _attached.RemoveAll(w => !IsWindow(w));
        if (_docked != IntPtr.Zero && !IsWindow(_docked))
        {
            DebugLog.Write("rdp", $"{_label} : fenêtre de l'application dans l'onglet fermée");
            _docked = IntPtr.Zero;
        }

        if (DebugLog.Enabled)
        {
            LogChanges();
        }

        // Fenêtre de l'onglet masquée par le serveur : une autre, visible, prend sa place.
        if (_docked == IntPtr.Zero || (!Legacy && !IsShown(_docked)))
        {
            var next = _windows.FirstOrDefault(w => w != _docked && (_attached.Contains(w) ? IsShown(w) : IsMainWindow(w)));
            if (next != IntPtr.Zero && _attached.Contains(next))
            {
                DebugLog.Write("rdp", $"{_label} : fenêtre de l'application 0x{next.ToInt64():X} « {Title(next)} » de nouveau affichée dans l'onglet");
                _docked = next;
                _loggedFits = 0;
                SetWindowPos(next, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            else if (next != IntPtr.Zero)
            {
                Dock(next);
            }
        }

        Fit(log: true);
        // Affichée dans l'onglet tant qu'elle existe et que le serveur ne l'a pas masquée.
        bool shown = _docked != IntPtr.Zero && IsShown(_docked);
        if (shown != _shown)
        {
            _shown = shown;
            _changed(shown);
        }
    }

    private void Dock(IntPtr window)
    {
        DebugLog.Write("rdp", $"{_label} : fenêtre de l'application 0x{window.ToInt64():X} « {Title(window)} » affichée dans l'onglet ({Describe(window, rect: true)})");
        long style = Style(window);
        SetParent(window, _container.Handle);
        SetWindowLongPtr(window, GwlStyle, new IntPtr((style & ~WsPopup) | WsChild));
        GetClientRect(_container.Handle, out var target);
        // Au-dessus de la fenêtre du contrôle, inutile en application distante, et des fenêtres rattachées avant.
        SetWindowPos(window, IntPtr.Zero, 0, 0, Math.Max(target.Width, 1), Math.Max(target.Height, 1),
            SwpFrameChanged | SwpShowWindow | SwpNoActivate);
        _docked = window;
        _attached.Add(window);
        _loggedFits = 0;
        // Le serveur peut encore lui donner sa taille d'origine juste après (constaté) : remise en place aussitôt,
        // sans attendre la minuterie.
        int thread = GetWindowThreadProcessId(window, out int process);
        if (thread != _hookedThread)
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
            }

            _hook = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _onLocationChange,
                process, thread, WinEventOutOfContext);
            _hookedThread = thread;
        }
    }

    /// <summary>Fenêtre déplacée ou redimensionnée (notification de Windows, sur le thread de la connexion).</summary>
    private void OnLocationChange(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, int thread, uint time)
    {
        if (window == _docked && objectId == 0 && !Paused)
        {
            Fit(log: true);
        }
    }

    /// <summary>Journal de débogage : apparition, changements (style, visibilité, position) et fermeture des fenêtres.</summary>
    private void LogChanges()
    {
        foreach (var window in _logged.Keys.Where(w => !_windows.Contains(w)).ToList())
        {
            _logged.Remove(window);
            DebugLog.Write("rdp", $"{_label} : fenêtre 0x{window.ToInt64():X} fermée");
        }

        foreach (var window in _windows)
        {
            // Position omise pour les fenêtres de l'onglet : elle suit sa taille.
            var state = $"« {Title(window)} » {Describe(window, rect: !_attached.Contains(window))}";
            if (!_logged.TryGetValue(window, out var previous) || previous != state)
            {
                _logged[window] = state;
                DebugLog.Write("rdp", $"{_label} : fenêtre 0x{window.ToInt64():X} {state}");
            }
        }
    }

    private string Describe(IntPtr window, bool rect)
    {
        long style = Style(window);
        var text = new StringBuilder($"style 0x{style:X} ex 0x{ExStyle(window):X}");
        if (rect)
        {
            GetWindowRect(window, out var r);
            text.Append($" en {r.Left},{r.Top} {r.Width}x{r.Height}");
        }

        text.Append((style & WsVisible) != 0 ? ", visible" : ", masquée");
        if ((style & WsMaximize) != 0)
        {
            text.Append(", agrandie");
        }

        if (IsIconic(window))
        {
            text.Append(", réduite");
        }

        if (_attached.Contains(window))
        {
            text.Append(window == _docked ? ", dans l'onglet" : ", rattachée à l'onglet");
        }
        else if (IsMainWindow(window))
        {
            text.Append(IsFullScreen(window) ? ", principale (plein écran)" : ", principale");
        }

        return text.ToString();
    }

    /// <summary>Fenêtre rattachée à l'onglet que le serveur n'a pas masquée (l'onglet, lui, peut l'être).</summary>
    private static bool IsShown(IntPtr window) => (Style(window) & WsVisible) != 0;

    /// <summary>
    /// Fenêtre principale : visible, de premier niveau, ni fenêtre d'outil (menus, boîtes de dialogue du serveur) ni
    /// fenêtre surgissante, sauf si elle peut être réduite ou agrandie, ou si elle occupe tout son écran.
    /// </summary>
    private static bool IsMainWindow(IntPtr window)
    {
        long style = Style(window);
        return IsWindowVisible(window) && (style & WsChild) == 0 && (ExStyle(window) & WsExToolWindow) == 0
               && ((style & WsPopup) == 0 || (style & (WsMinimizeBox | WsMaximizeBox)) != 0 || (!Legacy && IsFullScreen(window)));
    }

    /// <summary>Fenêtre sans barre de titre qui couvre tout l'écran où elle se trouve (client en plein écran).</summary>
    private static bool IsFullScreen(IntPtr window)
    {
        if ((Style(window) & WsCaption) == WsCaption || !GetWindowRect(window, out var r))
        {
            return false;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info)
               && r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
    }

    private static long Style(IntPtr window) => GetWindowLongPtr(window, GwlStyle).ToInt64();

    private static long ExStyle(IntPtr window) => GetWindowLongPtr(window, GwlExStyle).ToInt64();

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, int thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, int process,
        int thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr window, out int process);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
}
