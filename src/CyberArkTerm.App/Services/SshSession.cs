using System.Collections.Concurrent;
using System.Text;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;
using CyberArkTerm.Core.Terminal;
using Renci.SshNet;

namespace CyberArkTerm.App.Services;

public enum SshSessionState
{
    Connecting,
    Connected,
    Closed,
    Failed,
}

/// <summary>
/// Session SSH intégrée (onglet) via le PSMP : shell interactif affiché dans un <see cref="TerminalEmulator"/>
/// et navigateur de fichiers SFTP associé, ouvert à la demande.
/// Les événements sont toujours déclenchés sur le thread de l'interface.
/// </summary>
public sealed class SshSession : IDisposable
{
    private readonly PsmpConnector _connector;
    private readonly Dispatcher _dispatcher;
    private readonly bool _followTerminal;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly CancellationTokenSource _lifetime = new();
    private SshClient? _client;
    private ShellStream? _shell;
    private Task<RemoteFileBrowser>? _browser;
    private int _drainScheduled;
    private DateTime _lastData;

    public SshSession(PvwaAccount account, string label, PsmpConnector connector, Dispatcher dispatcher,
        bool followTerminal, SavedSession? saved)
    {
        Account = account;
        Label = label;
        Saved = saved;
        _connector = connector;
        _dispatcher = dispatcher;
        _followTerminal = followTerminal;
        Emulator.Response += Send;
        Emulator.WorkingDirectoryChanged += dir =>
        {
            TerminalDirectory = dir;
            TerminalDirectoryChanged?.Invoke(dir);
        };
    }

    /// <summary>Écran du terminal mis à jour (données reçues).</summary>
    public event Action? ScreenUpdated;

    public event Action? StateChanged;

    /// <summary>Dossier courant du shell (OSC 7), pour l'option « suivre le terminal ».</summary>
    public event Action<string>? TerminalDirectoryChanged;

    public PvwaAccount Account { get; }

    public SavedSession? Saved { get; }

    public string Label { get; }

    public TerminalEmulator Emulator { get; } = new(100, 30);

    public SshSessionState State { get; private set; } = SshSessionState.Connecting;

    public string? Error { get; private set; }

    public string? TerminalDirectory { get; private set; }

    /// <summary>Le navigateur suit le dossier du terminal (case « Suivre le terminal » du panneau Fichiers).</summary>
    public bool FollowTerminal { get; set; } = true;

    /// <summary>Dossier affiché par le panneau Fichiers pour cette session (conservé quand on change d'onglet).</summary>
    public string? BrowserDirectory { get; set; }

    public bool CanFollowTerminal => _followTerminal;

    public async Task ConnectAsync(int columns, int rows)
    {
        SetState(SshSessionState.Connecting, null);
        Emulator.Resize(columns, rows);
        // Reconnexion : on libère la connexion précédente avant d'en ouvrir une nouvelle.
        _shell?.Dispose();
        _client?.Dispose();
        _shell = null;
        _client = null;
        try
        {
            _client = await _connector.ConnectShellAsync(_lifetime.Token);
            _client.KeepAliveInterval = TimeSpan.FromSeconds(30);
            if (!string.IsNullOrWhiteSpace(_connector.Banner))
            {
                // Bannière du PSMP (avertissement d'enregistrement) affichée en gris.
                Emulator.Feed("\x1b[90m" + _connector.Banner.TrimEnd().Replace("\r\n", "\n").Replace("\n", "\r\n") + "\x1b[0m\r\n");
            }

            _shell = _client.CreateShellStream("xterm-256color", (uint)Emulator.Columns, (uint)Emulator.Rows, 0, 0, 65536);
            _shell.DataReceived += (_, e) => OnData(e.Data);
            _shell.Closed += (_, _) => _dispatcher.BeginInvoke(() => SetState(SshSessionState.Closed, Strings.SessionClosedByServer));
            _shell.ErrorOccurred += (_, e) => _dispatcher.BeginInvoke(() => SetState(SshSessionState.Failed, e.Exception.Message));
            SetState(SshSessionState.Connected, null);
            ScreenUpdated?.Invoke();
            if (_followTerminal || Saved?.StartDirectory is not null)
            {
                _ = PrepareShellAsync(Saved?.StartDirectory);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetState(SshSessionState.Failed, ex is OperationCanceledException ? Strings.ConnectionCancelled : ex.Message);
            throw;
        }
    }

    public void Send(string text)
    {
        if (_shell is null || State != SshSessionState.Connected)
        {
            return;
        }

        try
        {
            _shell.Write(text);
            _shell.Flush();
        }
        catch (Exception ex) when (ex is Renci.SshNet.Common.SshException or ObjectDisposedException or InvalidOperationException)
        {
            SetState(SshSessionState.Closed, ex.Message);
        }
    }

    public void Resize(int columns, int rows)
    {
        try
        {
            _shell?.ChangeWindowSize((uint)columns, (uint)rows, 0, 0);
        }
        catch (Exception ex) when (ex is Renci.SshNet.Common.SshException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>Connexion SFTP (deuxième session PSMP), ouverte au premier usage du panneau Fichiers.</summary>
    public Task<RemoteFileBrowser> GetBrowserAsync()
    {
        if (_browser is { IsFaulted: true } or { IsCanceled: true } || _browser is { IsCompletedSuccessfully: true, Result.IsConnected: false })
        {
            if (_browser.IsCompletedSuccessfully)
            {
                _browser.Result.Dispose();
            }

            _browser = null;
        }

        return _browser ??= OpenBrowserAsync();
    }

    private async Task<RemoteFileBrowser> OpenBrowserAsync()
    {
        var sftp = await _connector.ConnectSftpAsync(_lifetime.Token);
        sftp.KeepAliveInterval = TimeSpan.FromSeconds(30);
        return new RemoteFileBrowser(sftp, _connector.ConnectScpAsync);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        try
        {
            _shell?.Dispose();
            _client?.Dispose();
            if (_browser is { IsCompletedSuccessfully: true })
            {
                _browser.Result.Dispose();
            }
        }
        catch (Exception ex) when (ex is Renci.SshNet.Common.SshException or ObjectDisposedException or InvalidOperationException)
        {
        }

        _lifetime.Dispose();
    }

    private void OnData(byte[] data)
    {
        var chars = new char[_decoder.GetCharCount(data, 0, data.Length)];
        _decoder.GetChars(data, 0, data.Length, chars, 0);
        _pending.Enqueue(new string(chars));
        _lastData = DateTime.UtcNow;
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
        {
            // Un seul rafraîchissement pour plusieurs paquets reçus en rafale.
            _dispatcher.BeginInvoke(DispatcherPriority.Background, Drain);
        }
    }

    private void Drain()
    {
        Interlocked.Exchange(ref _drainScheduled, 0);
        var sb = new StringBuilder();
        while (_pending.TryDequeue(out var text))
        {
            sb.Append(text);
        }

        if (sb.Length > 0)
        {
            Emulator.Feed(sb.ToString());
            ScreenUpdated?.Invoke();
        }
    }

    /// <summary>
    /// Dès que l'invite est affichée (pas de données depuis 700 ms) : place le shell dans le dossier de départ
    /// du serveur courant et installe PROMPT_COMMAND (séquence OSC 7), puis efface la commande tapée.
    /// Sans suivi du dossier, seul le « cd » (visible) est envoyé.
    /// </summary>
    private async Task PrepareShellAsync(string? startDirectory)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && State == SshSessionState.Connected)
        {
            await Task.Delay(250);
            if (_lastData != default && DateTime.UtcNow - _lastData > TimeSpan.FromMilliseconds(700))
            {
                if (!Emulator.IsAlternateScreen)
                {
                    Send(_followTerminal
                        ? WorkingDirectory.InjectionFor(Emulator.CursorColumn, Emulator.Columns, startDirectory)
                        : WorkingDirectory.ChangeDirectoryCommand(startDirectory!));
                }

                return;
            }
        }
    }

    private void SetState(SshSessionState state, string? error)
    {
        if (State == state && Error == error)
        {
            return;
        }

        State = state;
        Error = error;
        StateChanged?.Invoke();
    }
}
