using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.Rdp;

namespace CyberArkTerm.App.Services.Rdp;

internal enum RdpSessionState
{
    Connecting,
    Connected,
    Ended,
    Failed,
}

/// <summary>Ce qu'il faut pour (re)lancer une connexion : réglages et, pour une connexion directe, le mot de passe.</summary>
internal sealed record RdpConnectionRequest(RdpConnectionSettings Settings, string? Password);

/// <summary>
/// Session Bureau à distance dans un onglet. Chaque connexion utilise un contrôle neuf ; la demande de connexion
/// (<see cref="RdpConnectionRequest"/>) est refaite à chaque fois, car le jeton d'une session PSM ne sert qu'une fois.
/// </summary>
internal sealed class RdpSession : IDisposable
{
    private static readonly Guid EventsIid = new("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6");
    private const int DispIdLoginComplete = 3;
    private const int DispIdDisconnected = 4;
    private const int DispIdEnterFullScreen = 5;
    private const int DispIdLeaveFullScreen = 6;
    private const int DispIdFatalError = 10;
    private const int DispIdRemoteProgramResult = 20;
    private const int DispIdRemoteProgramDisplayed = 21;

    // Raisons de déconnexion normales : par ce poste, par l'utilisateur distant, par le serveur.
    private static readonly int[] NormalDisconnects = [1, 2, 3];

    private readonly Func<CancellationToken, Task<RdpConnectionRequest>> _prepare;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _resizeTimer;
    private readonly List<(int DispId, Delegate Handler)> _handlers = [];
    private RdpClientHost? _client;
    private object? _ocx;
    private bool _loggedIn;
    private bool _dynamicResize = true;
    private (int Width, int Height) _sessionSize;
    private string? _programError;
    private RdpConnectionSettings? _remoteApp;
    private bool _programStarted;
    private bool _disposed;

    /// <param name="label">« compte@cible », titre de l'onglet et du plein écran.</param>
    /// <param name="prepare">Demande de connexion (appel PSMConnect, lecture du coffre KeePass...).</param>
    public RdpSession(string label, Func<CancellationToken, Task<RdpConnectionRequest>> prepare)
    {
        Label = label;
        _prepare = prepare;
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            ResizeSession();
        };
        // Masqué tant qu'aucune session n'est affichée : le message de l'onglet (WPF) reste alors visible.
        Host = new WindowsFormsHost { Background = System.Windows.Media.Brushes.Black, Visibility = Visibility.Hidden };
        Host.SizeChanged += (_, _) => _resizeTimer.Start();
    }

    public event Action? StateChanged;

    public string Label { get; }

    /// <summary>Élément WPF qui contient le contrôle Bureau à distance.</summary>
    public WindowsFormsHost Host { get; }

    public RdpSessionState State { get; private set; } = RdpSessionState.Connecting;

    /// <summary>Serveur de la dernière connexion (PSM ou serveur cible).</summary>
    public string Server { get; private set; } = "";

    /// <summary>Message de la dernière fin de session ou erreur.</summary>
    public string? Error { get; private set; }

    public bool IsFullScreen { get; private set; }

    /// <summary>Code de la dernière déconnexion signalée par le contrôle (événement OnDisconnected).</summary>
    public int? DisconnectReason { get; private set; }

    /// <summary>
    /// Vrai si la dernière tentative a échoué dans le contrôle Bureau à distance lui-même (création ou réglages),
    /// avant toute connexion au serveur.
    /// </summary>
    public bool ControlFailed { get; private set; }

    public bool IsConnected => State == RdpSessionState.Connected;

    /// <summary>Vrai tant que le contrôle Bureau à distance existe (connexion en cours ou établie).</summary>
    public bool HasControl => _client is not null;

    /// <summary>
    /// Vrai pour une application distante (RemoteApp) : ses fenêtres s'ouvrent directement sur le bureau de ce poste,
    /// l'onglet n'affiche que son état.
    /// </summary>
    public bool IsRemoteApp { get; private set; }

    /// <summary>Nom de l'application distante (vide pour un bureau).</summary>
    public string RemoteAppName { get; private set; } = "";

    /// <summary>Vrai si l'onglet affiche le bureau distant.</summary>
    public bool ShowsDesktop => HasControl && !IsRemoteApp;

    /// <summary>Application distante PSM ouverte comme un bureau (le serveur peut refuser ce mode).</summary>
    public bool DesktopFromRemoteApp { get; private set; }

    /// <summary>Ouverture de la session (fin de l'ouverture de session Windows), ou null.</summary>
    public DateTime? ConnectedAt { get; private set; }

    /// <summary>Événements « application distante » reçus du contrôle (diagnostic).</summary>
    internal List<string> RemoteAppEvents { get; } = [];

    public async Task ConnectAsync()
    {
        ReleaseClient();
        Error = null;
        _programError = null;
        _remoteApp = null;
        _programStarted = false;
        ConnectedAt = null;
        ControlFailed = false;
        DisconnectReason = null;
        SetState(RdpSessionState.Connecting);
        RdpConnectionRequest request;
        try
        {
            request = await _prepare(_lifetime.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Onglet fermé pendant la préparation (appel au PVWA annulé) : plus rien à afficher.
            if (!_disposed)
            {
                Error = ErrorText.Describe(ex);
                SetState(RdpSessionState.Failed);
            }

            return;
        }

        if (_disposed)
        {
            return;
        }

        try
        {
            Server = request.Settings.Server;
            IsRemoteApp = request.Settings.IsRemoteApp;
            DesktopFromRemoteApp = request.Settings.DesktopFromRemoteApp;
            RemoteAppName = IsRemoteApp ? request.Settings.RemoteApplicationTitle : "";
            StartClient(request);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_disposed)
            {
                return;
            }

            ReleaseClient();
            ControlFailed = true;
            Error = ex is COMException com ? Text.Format(Strings.RdpControlError, $"0x{com.HResult:X8}") : ErrorText.Describe(ex);
            SetState(RdpSessionState.Failed);
        }
    }

    /// <summary>Déconnecte la session puis attend sa fin (3 s au plus) avant de libérer le contrôle.</summary>
    public async Task CloseAsync()
    {
        if (_ocx is not null && IsConnected)
        {
            var ended = new TaskCompletionSource();
            void OnState()
            {
                if (!IsConnected)
                {
                    ended.TrySetResult();
                }
            }

            StateChanged += OnState;
            Disconnect();
            await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            StateChanged -= OnState;
        }

        Dispose();
    }

    /// <summary>Déconnecte la session ; pendant la connexion (serveur ou PSM qui ne répond pas), l'abandonne.</summary>
    public void Disconnect()
    {
        if (_ocx is not { } ocx)
        {
            return;
        }

        if (IsConnected)
        {
            TryCall(() => Dispatch.Call(ocx, "Disconnect"));
        }
        else if (State == RdpSessionState.Connecting && !_disposed)
        {
            TryCall(() => Dispatch.Call(ocx, "Disconnect"));
            if (State == RdpSessionState.Connecting)
            {
                // Le contrôle ne signale pas toujours la fin d'une connexion abandonnée : on la termine nous-mêmes.
                Error = null;
                IsFullScreen = false;
                SetState(RdpSessionState.Ended);
                ReleaseLater();
            }
        }
    }

    public void EnterFullScreen()
    {
        if (_ocx is not null && IsConnected && !IsRemoteApp)
        {
            TryCall(() => Dispatch.Set(_ocx, "FullScreen", true));
        }
    }

    public void Focus()
    {
        if (_client is { IsHandleCreated: true } client && IsConnected && !IsRemoteApp)
        {
            client.Focus();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _resizeTimer.Stop();
        Disconnect();
        ReleaseClient();
        Host.Dispose();
        _lifetime.Dispose();
    }

    private void StartClient(RdpConnectionRequest request)
    {
        var client = RdpClientHost.Create();
        client.Dock = System.Windows.Forms.DockStyle.Fill;
        Host.Visibility = Visibility.Visible;
        Host.Child = client;
        _client = client;
        Host.UpdateLayout();
        client.CreateControl();
        var ocx = client.Ocx ?? throw new COMException("MsRdpClient", unchecked((int)0x80004005));
        _ocx = ocx;
        Subscribe(ocx, DispIdLoginComplete, new Action(OnLoginComplete));
        Subscribe(ocx, DispIdDisconnected, new Action<int>(OnDisconnected));
        Subscribe(ocx, DispIdFatalError, new Action<int>(OnFatalError));
        Subscribe(ocx, DispIdRemoteProgramResult, new Action<string, int, bool>(OnRemoteProgramResult));
        Subscribe(ocx, DispIdRemoteProgramDisplayed, new Action<bool, uint>(OnRemoteProgramDisplayed));
        Subscribe(ocx, DispIdEnterFullScreen, new Action(() => IsFullScreen = true));
        Subscribe(ocx, DispIdLeaveFullScreen, new Action(() =>
        {
            IsFullScreen = false;
            Focus();
        }));

        Configure(ocx, request);
        Dispatch.Call(ocx, "Connect");
        if (IsRemoteApp)
        {
            // Application distante : rien à montrer dans l'onglet (ses fenêtres s'ouvrent à part, comme avec mstsc),
            // qui affiche à la place l'état de l'application.
            Host.Visibility = Visibility.Hidden;
        }

        // Le contrôle existe : l'onglet affiche le bureau, ou l'état de l'application distante.
        SetState(RdpSessionState.Connecting);
    }

    /// <summary>Applique les réglages ; ceux qui touchent à la sécurité doivent passer, les autres sont facultatifs.</summary>
    private void Configure(object ocx, RdpConnectionRequest request)
    {
        var s = request.Settings;
        // Application distante : bureau de la taille de tous les écrans, pour que ses fenêtres puissent aller partout.
        _sessionSize = DesktopSize(s.IsRemoteApp ? VirtualScreenSize() : PixelSize());
        _dynamicResize = !s.SmartSizing && !s.IsRemoteApp;
        _loggedIn = false;

        Dispatch.Set(ocx, "Server", s.Server);
        if (s.UserName.Length > 0)
        {
            Dispatch.Set(ocx, "UserName", s.UserName);
        }

        if (s.Domain.Length > 0)
        {
            Dispatch.Set(ocx, "Domain", s.Domain);
        }

        Dispatch.Set(ocx, "DesktopWidth", _sessionSize.Width);
        Dispatch.Set(ocx, "DesktopHeight", _sessionSize.Height);
        Optional(() => Dispatch.Set(ocx, "ColorDepth", s.ColorDepth));
        Optional(() => Dispatch.Set(ocx, "FullScreenTitle", Label));

        var advanced = Dispatch.First(ocx, "AdvancedSettings9", "AdvancedSettings8", "AdvancedSettings7", "AdvancedSettings6")
                       ?? throw new COMException("AdvancedSettings", unchecked((int)0x80004002));
        Dispatch.Set(advanced, "RDPPort", s.Port);
        Dispatch.Set(advanced, "EnableCredSspSupport", s.EnableCredSsp);
        Dispatch.Set(advanced, "AuthenticationLevel", (uint)s.AuthenticationLevel);
        Dispatch.Set(advanced, "RedirectDrives", s.RedirectDrives);
        Dispatch.Set(advanced, "RedirectPrinters", s.RedirectPrinters);
        Dispatch.Set(advanced, "RedirectPorts", s.RedirectPorts);
        Dispatch.Set(advanced, "RedirectSmartCards", s.RedirectSmartCards);
        Dispatch.Set(advanced, "RedirectClipboard", s.RedirectClipboard);
        Optional(() => Dispatch.Set(advanced, "RedirectPOSDevices", s.RedirectPosDevices));
        Optional(() => Dispatch.Set(advanced, "RedirectDevices", false));
        Optional(() => Dispatch.Set(advanced, "NegotiateSecurityLayer", s.NegotiateSecurityLayer));
        Optional(() => Dispatch.Set(advanced, "SmartSizing", s.SmartSizing));
        Optional(() => Dispatch.Set(advanced, "EnableAutoReconnect", s.AutoReconnect));
        Optional(() => Dispatch.Set(advanced, "Compress", s.Compress ? 1 : 0));
        Optional(() => Dispatch.Set(advanced, "BitmapPersistence", s.BitmapPersistence ? 1 : 0));
        Optional(() => Dispatch.Set(advanced, "PerformanceFlags", s.PerformanceFlags));
        Optional(() => Dispatch.Set(advanced, "AudioRedirectionMode", (uint)s.AudioMode));
        Optional(() => Dispatch.Set(advanced, "AudioCaptureRedirectionMode", s.AudioCapture));
        Optional(() => Dispatch.Set(advanced, "ConnectToAdministerServer", s.ConnectToAdministerServer));
        Optional(() => Dispatch.Set(advanced, "GrabFocusOnConnect", true));
        Optional(() => Dispatch.Set(advanced, "DisplayConnectionBar", true));
        Optional(() => Dispatch.Set(advanced, "ConnectionBarShowMinimizeButton", false));
        Optional(() => Dispatch.Set(advanced, "BandwidthDetection", true));
        if (s.LoadBalanceInfo.Length > 0)
        {
            Dispatch.Set(advanced, "LoadBalanceInfo", s.LoadBalanceInfo);
        }

        if (request.Password is { Length: > 0 } password)
        {
            // Transmis au contrôle seulement : jamais écrit sur disque ni dans le gestionnaire d'identification.
            Dispatch.Set(advanced, "ClearTextPassword", password);
        }

        if (s.IsRemoteApp)
        {
            // Mode application distante ; l'application elle-même est lancée une fois la session ouverte
            // (StartRemoteProgram). « alternate shell » ne sert pas dans ce mode.
            var program = Dispatch.First(ocx, "RemoteProgram") ?? throw new COMException("RemoteProgram", unchecked((int)0x80004002));
            Dispatch.Set(program, "RemoteProgramMode", true);
            _remoteApp = s;
            if (s.DisableRemoteAppCapsCheck && ocx is IMsRdpClientNonScriptable5 nonScriptable)
            {
                Optional(() => nonScriptable.SetDisableRemoteAppCapsCheck(true));
            }
        }

        var startProgram = s.IsRemoteApp ? "" : s.StartProgram;
        var workDir = s.IsRemoteApp ? "" : s.WorkDir;
        if (startProgram.Length > 0 || workDir.Length > 0 || s.KeyboardHookMode != 2)
        {
            var secured = Dispatch.First(ocx, "SecuredSettings3", "SecuredSettings2")
                          ?? throw new COMException("SecuredSettings", unchecked((int)0x80004002));
            if (startProgram.Length > 0)
            {
                Dispatch.Set(secured, "StartProgram", startProgram);
            }

            if (workDir.Length > 0)
            {
                Dispatch.Set(secured, "WorkDir", workDir);
            }

            Optional(() => Dispatch.Set(secured, "KeyboardHookMode", s.KeyboardHookMode));
        }

        if (s.GatewayHostname.Length > 0 && s.GatewayUsageMethod != 0)
        {
            var transport = Dispatch.First(ocx, "TransportSettings4", "TransportSettings3", "TransportSettings2", "TransportSettings")
                            ?? throw new COMException("TransportSettings", unchecked((int)0x80004002));
            Dispatch.Set(transport, "GatewayHostname", s.GatewayHostname);
            Dispatch.Set(transport, "GatewayUsageMethod", (uint)s.GatewayUsageMethod);
            Optional(() => Dispatch.Set(transport, "GatewayProfileUsageMethod", (uint)s.GatewayProfileUsageMethod));
            Optional(() => Dispatch.Set(transport, "GatewayCredsSource", (uint)s.GatewayCredsSource));
        }

        var (desktopScale, deviceScale) = ScaleFactors(Dpi());
        if (desktopScale > 100 && ocx is IMsRdpExtendedSettings extended)
        {
            Optional(() =>
            {
                object desktop = desktopScale;
                object device = deviceScale;
                extended.SetProperty("DesktopScaleFactor", ref desktop);
                extended.SetProperty("DeviceScaleFactor", ref device);
            });
        }
    }

    private void OnLoginComplete()
    {
        _loggedIn = true;
        ConnectedAt = DateTime.UtcNow;
        if (_remoteApp is not null)
        {
            // Hors de l'événement du contrôle.
            Host.Dispatcher.BeginInvoke(StartRemoteProgram);
        }

        SetState(RdpSessionState.Connected);
        // La taille de l'onglet a pu changer pendant l'ouverture de session.
        _resizeTimer.Start();
    }

    private void OnDisconnected(int reason)
    {
        if (_disposed)
        {
            return;
        }

        string? description = null;
        if (_ocx is { } ocx)
        {
            TryCall(() =>
            {
                var extended = Convert.ToUInt32(Dispatch.Get(ocx, "ExtendedDisconnectReason") ?? 0, System.Globalization.CultureInfo.InvariantCulture);
                description = Dispatch.Call(ocx, "GetErrorDescription", (uint)reason, extended) as string;
            });
        }

        DisconnectReason = reason;
        bool normal = NormalDisconnects.Contains(reason) && _programError is null;
        Error = _programError
                ?? (normal && reason == 1 ? null : string.IsNullOrWhiteSpace(description) ? Text.Format(Strings.RdpDisconnectCode, reason) : description.Trim());
        IsFullScreen = false;
        _loggedIn = false;
        SetState(normal ? RdpSessionState.Ended : RdpSessionState.Failed);
        ReleaseLater();
    }

    /// <summary>
    /// Lance l'application distante dans la session ouverte, une seule fois par connexion (pas de second lancement
    /// après une reconnexion automatique : pour le PSM, la demande de session ne sert qu'une fois). Le programme
    /// indiqué au contrôle avant la connexion n'est pas lancé par lui (vérifié sur un vrai serveur) : il faut le
    /// demander, comme ici.
    /// </summary>
    private void StartRemoteProgram()
    {
        if (_programStarted || _disposed || _ocx is not { } ocx || _remoteApp is not { } s)
        {
            return;
        }

        _programStarted = true;
        try
        {
            var program = Dispatch.Get(ocx, "RemoteProgram") ?? throw new COMException("RemoteProgram", unchecked((int)0x80004002));
            Dispatch.Call(program, "ServerStartProgram", s.RemoteApplicationProgram, s.RemoteApplicationFile, "", true,
                s.RemoteApplicationArgs, s.RemoteApplicationExpandArgs);
        }
        catch (Exception e) when (Dispatch.IsDispatchError(e))
        {
            _programError = Text.Format(Strings.RdpRemoteAppFailed, RemoteAppName, ErrorText.Describe(e));
            TryCall(() => Dispatch.Call(ocx, "Disconnect"));
        }
    }

    /// <summary>Résultat du lancement de l'application distante ; en cas d'échec, la session est fermée avec l'explication.</summary>
    private void OnRemoteProgramResult(string program, int result, bool isExecutable)
    {
        RemoteAppEvents.Add($"result {program} {result} {isExecutable}");
        if (result == 0 || _disposed)
        {
            return;
        }

        var why = result switch
        {
            3 => Strings.RdpRemoteAppNotAllowed,
            4 or 5 => Strings.RdpRemoteAppNotFound,
            _ => Text.Format(Strings.RdpRemoteAppErrorCode, result.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        _programError = Text.Format(Strings.RdpRemoteAppFailed, RemoteAppName.Length > 0 ? RemoteAppName : program, why);
        // Sans application, la session n'afficherait rien : on la ferme (hors de l'événement du contrôle).
        Host.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && _ocx is { } ocx)
            {
                TryCall(() => Dispatch.Call(ocx, "Disconnect"));
            }
        });
    }

    private void OnRemoteProgramDisplayed(bool displayed, uint information)
    {
        RemoteAppEvents.Add($"displayed {displayed} {information}");
        // Normalement signalé par l'ouverture de session ; certains serveurs n'envoient que l'affichage de l'application.
        if (displayed && !_disposed && State == RdpSessionState.Connecting)
        {
            _loggedIn = true;
            SetState(RdpSessionState.Connected);
        }
    }

    private void OnFatalError(int code)
    {
        Error = Text.Format(Strings.RdpControlError, code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetState(RdpSessionState.Failed);
        ReleaseLater();
    }

    /// <summary>Adapte la résolution du bureau distant à la taille de l'onglet (Windows 8.1 / 2012 R2 et plus).</summary>
    private void ResizeSession()
    {
        if (!_loggedIn || !_dynamicResize || _ocx is not { } ocx)
        {
            return;
        }

        var pixels = PixelSize();
        var size = DesktopSize(pixels);
        if (size == _sessionSize || pixels.Width < 200 || pixels.Height < 200)
        {
            return;
        }

        var (desktopScale, deviceScale) = ScaleFactors(Dpi());
        try
        {
            Dispatch.Call(ocx, "UpdateSessionDisplaySettings", (uint)size.Width, (uint)size.Height, (uint)size.Width, (uint)size.Height,
                0u, desktopScale, deviceScale);
            _sessionSize = size;
        }
        catch (Exception e) when (Dispatch.IsDispatchError(e))
        {
            // Serveur ou contrôle trop ancien : l'image est mise à l'échelle de l'onglet.
            _dynamicResize = false;
            if (Dispatch.First(ocx, "AdvancedSettings9", "AdvancedSettings8", "AdvancedSettings7", "AdvancedSettings6") is { } advanced)
            {
                Optional(() => Dispatch.Set(advanced, "SmartSizing", true));
            }
        }
    }

    /// <summary>Taille du bureau distant : entre 200 et 8192 pixels, largeur paire.</summary>
    internal static (int Width, int Height) DesktopSize((int Width, int Height) pixels) =>
        (Math.Clamp(pixels.Width, 200, 8192) & ~1, Math.Clamp(pixels.Height, 200, 8192));

    /// <summary>Taille de l'ensemble des écrans de ce poste, en pixels.</summary>
    private static (int Width, int Height) VirtualScreenSize()
    {
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        return (screen.Width, screen.Height);
    }

    /// <summary>Taille de la zone d'affichage en pixels de l'écran.</summary>
    private (int Width, int Height) PixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(Host);
        return ((int)Math.Round(Host.ActualWidth * dpi.DpiScaleX), (int)Math.Round(Host.ActualHeight * dpi.DpiScaleY));
    }

    private int Dpi() => (int)Math.Round(VisualTreeHelper.GetDpi(Host).PixelsPerInchX);

    /// <summary>Échelle d'affichage du bureau distant d'après les DPI de l'écran (100 % à 96 DPI).</summary>
    internal static (uint Desktop, uint Device) ScaleFactors(int dpi)
    {
        uint[] allowed = [100, 125, 150, 175, 200, 250, 300, 400, 500];
        var wanted = dpi * 100.0 / 96;
        var desktop = allowed.MinBy(a => Math.Abs(a - wanted));
        uint device = desktop >= 175 ? 180u : desktop >= 125 ? 140u : 100u;
        return (desktop, device);
    }

    private void Subscribe(object ocx, int dispId, Delegate handler)
    {
        ComEventsHelper.Combine(ocx, EventsIid, dispId, handler);
        _handlers.Add((dispId, handler));
    }

    /// <summary>Libère le contrôle après l'événement en cours : on ne le détruit pas pendant qu'il nous appelle.</summary>
    private void ReleaseLater() => Host.Dispatcher.BeginInvoke(() =>
    {
        if (!_disposed && State != RdpSessionState.Connecting)
        {
            ReleaseClient();
            StateChanged?.Invoke();
        }
    });

    private void ReleaseClient()
    {
        _loggedIn = false;
        if (_ocx is { } ocx)
        {
            foreach (var (dispId, handler) in _handlers)
            {
                TryCall(() => ComEventsHelper.Remove(ocx, EventsIid, dispId, handler));
            }
        }

        _handlers.Clear();
        _ocx = null;
        if (_client is { } client)
        {
            _client = null;
            Host.Child = null;
            Host.Visibility = Visibility.Hidden;
            client.Dispose();
        }
    }

    private void SetState(RdpSessionState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    private static void Optional(Action set) => TryCall(set);

    private static void TryCall(Action call)
    {
        try
        {
            call();
        }
        catch (Exception e) when (Dispatch.IsDispatchError(e))
        {
        }
    }
}
