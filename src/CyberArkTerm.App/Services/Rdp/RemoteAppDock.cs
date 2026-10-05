using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CyberArkTerm.Core.Diagnostics;

namespace CyberArkTerm.App.Services.Rdp;

/// <summary>
/// Fenêtres d'une application distante (RemoteApp) affichées dans l'onglet, sur le thread de la connexion.
/// Le contrôle Bureau à distance crée les fenêtres de l'application dans ce processus, sur un thread à lui, comme des
/// fenêtres de premier niveau qu'il place où le serveur les met. Chaque fenêtre principale (ni menu ni boîte de
/// dialogue : une fenêtre qui peut être réduite ou agrandie, ou une fenêtre plein écran) est rattachée au conteneur de
/// l'onglet et en prend toute la place ; l'onglet montre la dernière visible (une fenêtre plein écran ouverte par-dessus
/// la principale, puis la principale quand elle se ferme). Les autres (menus, boîtes de dialogue) restent à part,
/// au-dessus, à l'endroit voulu par le serveur.
/// <para>
/// Le contrôle n'indique pas au serveur les déplacements faits par programme : la fenêtre resterait, sur le serveur,
/// à sa place et à sa taille d'origine ; or la souris est transmise en coordonnées d'écran (les clics tomberaient à côté)
/// et l'image est étirée à la taille de la fenêtre locale (constaté). Le contrôle n'envoie la position d'une fenêtre
/// qu'à la fin d'un déplacement commencé par le serveur : on demande donc au serveur un déplacement au clavier
/// (commande système « Déplacer »), que le contrôle fait localement, et qu'on termine aussitôt (Entrée) ; il envoie
/// alors la place de la fenêtre, c'est-à-dire celle de l'onglet, et le serveur y met la sienne (position et taille).
/// </para>
/// Vérifié sur un vrai serveur : rendu, souris (clics reçus là où ils sont faits), clavier, redimensionnement, onglet
/// masqué puis réaffiché, fenêtre plein écran ouverte puis fermée.
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
    private const int WmKeyDown = 0x0100;
    private const int WmSysCommand = 0x0112;
    private const int ScMove = 0xF010;
    private const int ScRestore = 0xF120;
    private const int VkReturn = 0x0D;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;
    private const uint EventSystemMoveSizeStart = 0x000A;
    private const uint EventSystemMoveSizeEnd = 0x000B;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0;
    private const uint MonitorDefaultToNearest = 2;
    private const int GuiInMoveSize = 0x0002;
    private const uint GaRoot = 2;
    private const int MaxLoggedFits = 30;
    private const int MaxSyncAttempts = 3;

    private static readonly IntPtr MessageOnlyParent = new(-3);

    /// <summary>Délai pour que le serveur commence le déplacement demandé.</summary>
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Après un envoi, le serveur confirme la place : ce n'est pas un déplacement de son fait.</summary>
    private static readonly TimeSpan SyncSettle = TimeSpan.FromSeconds(1.5);

    /// <summary>Envois rapprochés pour une même place (serveur qui remet sa fenêtre ailleurs) : au plus trois.</summary>
    private static readonly TimeSpan SyncBurst = TimeSpan.FromSeconds(10);

    private readonly Control _container;
    private readonly Action<bool> _changed;
    private readonly string _label;
    private readonly List<IntPtr> _windows = [];
    private readonly List<IntPtr> _attached = [];
    private readonly List<IntPtr> _hooks = [];
    private readonly Dictionary<IntPtr, string> _logged = [];

    /// <summary>Place (écran) de chaque fenêtre rattachée sur le serveur, autant qu'on la connaisse.</summary>
    private readonly Dictionary<IntPtr, Rect> _places = [];
    private readonly System.Windows.Forms.Timer _timer;
    private readonly WinEventProc _onEvent;
    private readonly System.Windows.Forms.Timer _cursorTimer;
    private IntPtr _docked;
    private int _hookedThread;
    private int _loggedFits;
    private bool _shown;

    // Place de la fenêtre de l'onglet sur le serveur (coordonnées d'écran).
    private Rect _sent;
    private Rect _seen;
    private Rect _target;
    private Rect _loop;
    private DateTime _requested = DateTime.MinValue;
    private DateTime _settled = DateTime.MinValue;
    private DateTime _lastSync = DateTime.MinValue;
    private int _attempts;
    private IntPtr _focus;
    private Point _cursor;
    private Point _parked;
    private bool _cursorParked;

    /// <param name="container">Conteneur du contrôle dans l'onglet (thread de la connexion).</param>
    /// <param name="changed">Fenêtre affichée dans l'onglet (vrai) ou plus (faux).</param>
    public RemoteAppDock(Control container, Action<bool> changed, string label)
    {
        _container = container;
        _changed = changed;
        _label = label;
        _onEvent = OnEvent;
        // Filet de sécurité : fenêtre ouverte, fermée, masquée, déplacée ou réduite par le serveur ; onglet déplacé.
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        _cursorTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _cursorTimer.Tick += (_, _) => RestoreCursor();
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
    public void Fit() => Fit(fromServer: false);

    /// <summary>
    /// Fin de la connexion : les fenêtres quittent le conteneur (qui va être détruit) pour une fenêtre sans affichage ;
    /// le contrôle les détruit lui-même, sur son thread.
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        RestoreCursor();
        _cursorTimer.Dispose();
        Unhook();
        foreach (var window in _attached.Where(IsWindow))
        {
            ShowWindow(window, 0);
            SetParent(window, MessageOnlyParent);
        }

        _docked = IntPtr.Zero;
        _attached.Clear();
        _places.Clear();
        _windows.Clear();
    }

    /// <param name="fromServer">
    /// Déplacement signalé par Windows (et non changement de taille de l'onglet) : hors d'un envoi de place, c'est le
    /// serveur qui a déplacé ou redimensionné sa fenêtre ; sa nouvelle place lui sera renvoyée.
    /// </param>
    private void Fit(bool fromServer)
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
        var position = rect;
        MapWindowPoints(IntPtr.Zero, _container.Handle, ref position, 2);
        if (position.Left == 0 && position.Top == 0 && rect.Width == target.Width && rect.Height == target.Height)
        {
            return;
        }

        var now = DateTime.UtcNow;
        bool syncing = _requested != DateTime.MinValue || now - _settled < SyncSettle;
        if (fromServer && !syncing)
        {
            _sent = default;
            _places[_docked] = rect;
            if (DebugLog.Enabled && _loggedFits < MaxLoggedFits)
            {
                _loggedFits++;
                DebugLog.Write("rdp", $"{_label} : fenêtre 0x{_docked.ToInt64():X} placée par le serveur en {position.Left},{position.Top} " +
                                      $"{rect.Width}x{rect.Height}, remise à la taille de l'onglet {target.Width}x{target.Height}" +
                                      (_loggedFits == MaxLoggedFits ? " (corrections suivantes non notées)" : ""));
            }
        }

        SetWindowPos(_docked, IntPtr.Zero, 0, 0, Math.Max(target.Width, 1), Math.Max(target.Height, 1), SwpNoZOrder | SwpNoActivate);
    }

    private void Update()
    {
        _windows.RemoveAll(w => !IsWindow(w));
        _attached.RemoveAll(w => !IsWindow(w));
        foreach (var gone in _places.Keys.Where(w => !IsWindow(w)).ToList())
        {
            _places.Remove(gone);
        }

        if (_docked != IntPtr.Zero && !IsWindow(_docked))
        {
            DebugLog.Write("rdp", $"{_label} : fenêtre de l'application dans l'onglet fermée");
            _docked = IntPtr.Zero;
        }

        if (DebugLog.Enabled)
        {
            LogChanges();
        }

        // Nouvelle fenêtre principale : par-dessus, dans l'onglet.
        foreach (var window in _windows.Where(w => !_attached.Contains(w) && IsMainWindow(w)).ToList())
        {
            Dock(window);
        }

        // L'onglet montre la dernière fenêtre rattachée que le serveur n'a pas masquée.
        var current = _attached.LastOrDefault(IsShown);
        if (current != IntPtr.Zero && current != _docked)
        {
            DebugLog.Write("rdp", $"{_label} : fenêtre de l'application 0x{current.ToInt64():X} « {Title(current)} » de nouveau affichée dans l'onglet");
            Select(current);
            SetWindowPos(current, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        Fit(fromServer: true);
        Sync();
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
        // Fenêtre de premier niveau : le contrôle la met où le serveur a la sienne.
        GetWindowRect(window, out var place);
        _places[window] = place;
        long style = Style(window);
        SetParent(window, _container.Handle);
        SetWindowLongPtr(window, GwlStyle, new IntPtr((style & ~WsPopup) | WsChild));
        GetClientRect(_container.Handle, out var target);
        // Au-dessus de la fenêtre du contrôle, inutile en application distante, et des fenêtres rattachées avant.
        SetWindowPos(window, IntPtr.Zero, 0, 0, Math.Max(target.Width, 1), Math.Max(target.Height, 1),
            SwpFrameChanged | SwpShowWindow | SwpNoActivate);
        _attached.Add(window);
        Select(window);
        Hook(window);
    }

    /// <summary>Fenêtre montrée par l'onglet : sa place sur le serveur est à envoyer.</summary>
    private void Select(IntPtr window)
    {
        _docked = window;
        _loggedFits = 0;
        _sent = default;
        _seen = default;
        _requested = DateTime.MinValue;
        _attempts = 0;
    }

    /// <summary>
    /// Envoie au serveur la place de la fenêtre de l'onglet quand elle a changé (onglet déplacé ou redimensionné,
    /// fenêtre déplacée par le serveur), une fois l'onglet immobile. Seulement quand l'application est au premier plan,
    /// sans bouton de souris enfoncé : le déplacement au clavier place un instant le pointeur sur la fenêtre.
    /// </summary>
    private void Sync()
    {
        if (_docked == IntPtr.Zero || !_container.IsHandleCreated || !IsWindowVisible(_docked)
            || IsIconic(GetAncestor(_container.Handle, GaRoot)))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_requested != DateTime.MinValue)
        {
            if (now - _requested < SyncTimeout)
            {
                return;
            }

            // Pas de déplacement (serveur sans déplacement local, fenêtre qui le refuse) : nouvel essai plus tard.
            _requested = DateTime.MinValue;
            RestoreCursor();
            DebugLog.Write("rdp", $"{_label} : le serveur n'a pas déplacé la fenêtre 0x{_docked.ToInt64():X}" +
                                  (_attempts < MaxSyncAttempts ? ", nouvel essai" : ", abandon pour cette place"));
            if (_attempts >= MaxSyncAttempts)
            {
                _sent = _target;
            }

            return;
        }

        GetWindowRect(_docked, out var rect);
        if (rect.Equals(_sent))
        {
            return;
        }

        // Onglet en mouvement : on attend qu'il s'arrête.
        if (!rect.Equals(_seen))
        {
            _seen = rect;
            return;
        }

        if (!IsForeground() || IsMouseButtonDown())
        {
            return;
        }

        if (!rect.Equals(_target) || now - _lastSync > SyncBurst)
        {
            _target = rect;
            _attempts = 0;
        }

        if (_attempts >= MaxSyncAttempts)
        {
            return;
        }

        _attempts++;
        _lastSync = now;
        _requested = now;
        _focus = GetFocus();
        DebugLog.Write("rdp", $"{_label} : place de l'onglet envoyée au serveur pour la fenêtre 0x{_docked.ToInt64():X} : " +
                              $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height} (essai {_attempts})");
        ParkCursor();
        // Une fenêtre agrandie ne se déplace pas : restaurée d'abord (sans effet sur une fenêtre normale).
        PostMessage(_docked, WmSysCommand, new IntPtr(ScRestore), IntPtr.Zero);
        PostMessage(_docked, WmSysCommand, new IntPtr(ScMove), IntPtr.Zero);
    }

    /// <summary>
    /// À la fin du déplacement, le contrôle envoie au serveur un clic de souris là où est le pointeur, que le serveur
    /// applique à ses fenêtres telles qu'avant le déplacement (constaté : reçu par l'application quand le pointeur était
    /// sur sa fenêtre, quelle que soit la façon de terminer le déplacement). Le pointeur est donc mis, le temps du
    /// déplacement, là où le serveur n'a aucune fenêtre de l'application : coin de la fenêtre de CyberArkTerm, sinon
    /// un coin de l'écran. Une fenêtre qui couvre tout l'écran du serveur (client plein écran) ne laisse aucun tel
    /// endroit : le clic, inévitable, est alors mis au milieu de son bord haut (barre de connexion d'un client Bureau
    /// à distance, haut de l'écran), jamais dans un coin (bouton Fermer, menu système, « Afficher le bureau »).
    /// Le pointeur revient ensuite, si l'utilisateur ne l'a pas bougé.
    /// </summary>
    private void ParkCursor()
    {
        if (_cursorParked || !GetCursorPos(out _cursor) || !GetWindowRect(GetAncestor(_container.Handle, GaRoot), out var root))
        {
            return;
        }

        var screen = SystemInformation.VirtualScreen;
        Point At(int x, int y) => new()
        {
            X = Math.Clamp(x, screen.Left, screen.Right - 1),
            Y = Math.Clamp(y, screen.Top, screen.Bottom - 1),
        };

        GetWindowRect(_docked, out var tab);
        _places.TryGetValue(_docked, out var place);
        // Autres fenêtres de l'application : à part (menus, boîtes de dialogue) et rattachées (leur place sur le serveur).
        var others = _windows.Where(w => w != _docked && IsWindowVisible(w) && !_attached.Contains(w))
            .Select(w => GetWindowRect(w, out var r) ? r : default)
            .Concat(_places.Where(p => p.Key != _docked).Select(p => p.Value)).ToList();
        bool Free(Point p) => !Contains(tab, p) && !Contains(place, p) && !others.Any(r => Contains(r, p));

        var candidates = new[]
        {
            At(root.Left + 16, root.Top + 16), At(screen.Left, screen.Top), At(screen.Right - 1, screen.Top),
            At(screen.Left, screen.Bottom - 1), At(screen.Right - 1, screen.Bottom - 1),
        };
        if (candidates.Where(Free).Take(1).ToList() is [var free])
        {
            _parked = free;
        }
        else if (place.Width > 0)
        {
            _parked = At(place.Left + (place.Width / 2), place.Top);
            DebugLog.Write("rdp", $"{_label} : fenêtre 0x{_docked.ToInt64():X} sur tout l'écran du serveur : clic du déplacement " +
                                  $"au milieu de son bord haut ({_parked.X},{_parked.Y})");
        }
        else
        {
            return;
        }

        _cursorParked = SetCursorPos(_parked.X, _parked.Y);
    }

    private static bool Contains(Rect r, Point p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    private void RestoreCursor()
    {
        _cursorTimer.Stop();
        if (_cursorParked && GetCursorPos(out var now) && now.X == _parked.X && now.Y == _parked.Y)
        {
            SetCursorPos(_cursor.X, _cursor.Y);
        }

        _cursorParked = false;
    }

    /// <summary>Notifications de Windows sur les fenêtres du thread de l'application (sur le thread de la connexion).</summary>
    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, int thread, uint time)
    {
        if (objectId != 0 || !_attached.Contains(window))
        {
            return;
        }

        if (eventType == EventSystemMoveSizeStart)
        {
            // Déplacement demandé par Sync (ou commencé sur le serveur, barre de titre tirée) : dans l'onglet, la
            // fenêtre ne bouge pas ; il est terminé aussitôt, à sa place, que le contrôle envoie au serveur. (Entrée
            // seulement si le déplacement est toujours en cours : sinon elle irait à l'application.)
            if (window == _docked)
            {
                GetWindowRect(window, out _loop);
                _requested = DateTime.UtcNow;
            }

            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(thread, ref info) && (info.Flags & GuiInMoveSize) != 0)
            {
                PostMessage(window, WmKeyDown, new IntPtr(VkReturn), new IntPtr(0x001C0001));
            }

            return;
        }

        if (window != _docked)
        {
            return;
        }

        if (eventType == EventObjectLocationChange)
        {
            Fit(fromServer: true);
        }
        else if (eventType == EventSystemMoveSizeEnd)
        {
            _sent = _loop;
            _places[window] = _loop;
            _requested = DateTime.MinValue;
            _settled = DateTime.UtcNow;
            DebugLog.Write("rdp", $"{_label} : fenêtre 0x{window.ToInt64():X} placée sur le serveur en {_loop.Left},{_loop.Top} {_loop.Width}x{_loop.Height}");
            // Le contrôle replace la fenêtre locale avec des coordonnées d'écran : remise dans l'onglet. Le clavier
            // revient où il était (le déplacement le prend).
            Fit(fromServer: false);
            if (_focus != IntPtr.Zero && IsWindow(_focus) && GetFocus() != _focus)
            {
                SetFocus(_focus);
            }

            _focus = IntPtr.Zero;
            _cursorTimer.Start();
        }
    }

    private void Hook(IntPtr window)
    {
        int thread = GetWindowThreadProcessId(window, out int process);
        if (thread == _hookedThread)
        {
            return;
        }

        Unhook();
        _hooks.Add(SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _onEvent, process, thread, WinEventOutOfContext));
        _hooks.Add(SetWinEventHook(EventSystemMoveSizeStart, EventSystemMoveSizeEnd, IntPtr.Zero, _onEvent, process, thread, WinEventOutOfContext));
        _hookedThread = thread;
    }

    private void Unhook()
    {
        foreach (var hook in _hooks.Where(h => h != IntPtr.Zero))
        {
            UnhookWinEvent(hook);
        }

        _hooks.Clear();
        _hookedThread = 0;
    }

    /// <summary>Fenêtre au premier plan de ce processus (l'utilisateur est dans l'application).</summary>
    private static bool IsForeground()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero && GetWindowThreadProcessId(foreground, out int process) != 0 && process == Environment.ProcessId;
    }

    private static bool IsMouseButtonDown() =>
        GetAsyncKeyState(VkLButton) < 0 || GetAsyncKeyState(VkRButton) < 0 || GetAsyncKeyState(VkMButton) < 0;

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
            // Position omise pour les fenêtres de l'onglet : elle suit l'onglet.
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

    /// <summary>Fenêtre rattachée que le serveur n'a pas masquée (l'onglet, lui, peut l'être).</summary>
    private static bool IsShown(IntPtr window) => (Style(window) & WsVisible) != 0;

    /// <summary>
    /// Fenêtre principale : visible, non vide, de premier niveau, ni fenêtre d'outil (menus, boîtes de dialogue du
    /// serveur) ni fenêtre surgissante, sauf si elle peut être réduite ou agrandie, ou si elle occupe tout son écran.
    /// </summary>
    private static bool IsMainWindow(IntPtr window)
    {
        long style = Style(window);
        return IsWindowVisible(window) && GetWindowRect(window, out var r) && r.Width > 0 && r.Height > 0
               && (style & WsChild) == 0 && (ExStyle(window) & WsExToolWindow) == 0
               && ((style & WsPopup) == 0 || (style & (WsMinimizeBox | WsMaximizeBox)) != 0 || IsFullScreen(window));
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
    private record struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public Rect CaretRect;
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
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(int thread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

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
