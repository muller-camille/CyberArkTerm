using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Diagnostics;
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
    private readonly SshConnector _connector;
    private readonly Dispatcher _dispatcher;
    private readonly bool _followTerminal;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sendLock = new();
    private Task _sending = Task.CompletedTask;
    private SshClient? _client;
    private ShellStream? _shell;
    private Task<RemoteFileBrowser>? _browser;
    private int _drainScheduled;
    private DateTime _lastData;
    private bool _userTyped;

    // Numéro de la connexion en cours : la préparation du shell d'une connexion précédente s'arrête à la reconnexion.
    private int _connection;

    /// <param name="account">Compte CyberArk ; null pour une connexion directe (accès d'urgence KeePass).</param>
    public SshSession(PvwaAccount? account, string label, SshConnector connector, Dispatcher dispatcher,
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

    public PvwaAccount? Account { get; }

    public SavedSession? Saved { get; }

    public string Label { get; }

    public TerminalEmulator Emulator { get; } = new(100, 30);

    public SshSessionState State { get; private set; } = SshSessionState.Connecting;

    public string? Error { get; private set; }

    /// <summary>Fichiers de cette session ouverts dans l'éditeur de texte (créé par la fenêtre principale).</summary>
    public RemoteEditor? Editor { get; set; }

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
        // Reconnexion : on libère la connexion précédente avant d'en ouvrir une nouvelle, et on oublie ce qu'elle a
        // reçu ou ce qui y a été tapé (sinon le suivi du dossier ne s'installerait pas, ou trop tôt).
        DisposeInBackground(_shell, _client, null);
        _shell = null;
        _client = null;
        int connection = ++_connection;
        _userTyped = false;
        _lastData = default;
        try
        {
            DebugLog.Write("ssh", $"{Label} : connexion");
            _client = await _connector.ConnectShellAsync(_lifetime.Token);
            DebugLog.Write("ssh", $"{Label} : connecté ({_client.ConnectionInfo.ServerVersion}, {_client.ConnectionInfo.CurrentServerEncryption}, bannière {!string.IsNullOrWhiteSpace(_connector.Banner)})");
            _client.KeepAliveInterval = TimeSpan.FromSeconds(30);
            if (connection > 1 && Emulator.CursorColumn > 0)
            {
                // Reconnexion : la nouvelle session commence sur une nouvelle ligne, après l'invite de la précédente.
                Emulator.Feed("\r\n");
            }

            if (!string.IsNullOrWhiteSpace(_connector.Banner))
            {
                // Bannière du PSMP (avertissement d'enregistrement) affichée en gris.
                Emulator.Feed("\x1b[90m" + _connector.Banner.TrimEnd().Replace("\r\n", "\n").Replace("\n", "\r\n") + "\x1b[0m\r\n");
            }

            _shell = _client.CreateShellStream("xterm-256color", (uint)Emulator.Columns, (uint)Emulator.Rows, 0, 0, 65536);
            _shell.DataReceived += (_, e) => OnData(e.Data);
            _shell.Closed += (_, _) =>
            {
                DebugLog.Write("ssh", $"{Label} : session fermée par le serveur");
                _dispatcher.BeginInvoke(() => SetState(SshSessionState.Closed, Strings.SessionClosedByServer));
            };
            _shell.ErrorOccurred += (_, e) =>
            {
                DebugLog.Write("ssh", $"{Label} : erreur de la session", e.Exception);
                _dispatcher.BeginInvoke(() => SetState(SshSessionState.Failed, e.Exception.Message));
            };
            SetState(SshSessionState.Connected, null);
            ScreenUpdated?.Invoke();
            if (_followTerminal || Saved?.StartDirectory is not null)
            {
                _ = PrepareShellAsync(Saved?.StartDirectory, connection);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            DebugLog.Write("ssh", $"{Label} : échec de la connexion", ex);
            SetState(SshSessionState.Failed, ex is OperationCanceledException ? Strings.ConnectionCancelled : ex.Message);
            throw;
        }
    }

    /// <summary>Saisie de l'utilisateur dans le terminal.</summary>
    public void SendInput(string text)
    {
        _userTyped = true;
        Send(text);
    }

    /// <summary>
    /// Installe le suivi du dossier dans le shell courant (case « Suivre » cochée, nouveau shell après « sudo -i »...).
    /// Seulement si le shell attend une commande, pour ne rien écrire au milieu d'une saisie ou d'un éditeur.
    /// </summary>
    public bool InstallFolderTracking()
    {
        if (!_followTerminal || State != SshSessionState.Connected || !IsAtPrompt())
        {
            return false;
        }

        Send(WorkingDirectory.InjectionFor(Emulator.CursorColumn, Emulator.Columns));
        return true;
    }

    public void Send(string text)
    {
        if (_shell is not { } shell || State != SshSessionState.Connected)
        {
            return;
        }

        Enqueue(shell, closeOnError: true, s =>
        {
            s.Write(text);
            s.Flush();
        });
    }

    public void Resize(int columns, int rows)
    {
        if (_shell is { } shell)
        {
            Enqueue(shell, closeOnError: false, s => s.ChangeWindowSize((uint)columns, (uint)rows, 0, 0));
        }
    }

    /// <summary>
    /// Envoi au serveur hors du thread de l'interface, dans l'ordre des demandes : une écriture attend le réseau et la
    /// fenêtre SSH du serveur, et ne doit pas figer l'application si le serveur ou le PSMP ne lit plus.
    /// </summary>
    private void Enqueue(ShellStream shell, bool closeOnError, Action<ShellStream> write)
    {
        lock (_sendLock)
        {
            _sending = _sending.ContinueWith(_ =>
            {
                try
                {
                    write(shell);
                }
                catch (Exception ex) when (ex is Renci.SshNet.Common.SshException or ObjectDisposedException or InvalidOperationException or IOException)
                {
                    DebugLog.Write("ssh", $"{Label} : échec de l'envoi au serveur", ex);
                    if (closeOnError)
                    {
                        _dispatcher.BeginInvoke(() =>
                        {
                            if (_shell == shell)
                            {
                                SetState(SshSessionState.Closed, ex.Message);
                            }
                        });
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Fermeture des connexions hors du thread de l'interface (la déconnexion attend le réseau). Une écriture en
    /// attente échoue alors, sans conséquence.
    /// </summary>
    private void DisposeInBackground(ShellStream? shell, SshClient? client, RemoteFileBrowser? browser)
    {
        if (shell is null && client is null && browser is null)
        {
            return;
        }

        var label = Label;
        _ = Task.Run(() =>
        {
            try
            {
                shell?.Dispose();
                client?.Dispose();
                browser?.Dispose();
            }
            catch (Exception ex) when (ex is Renci.SshNet.Common.SshException or ObjectDisposedException or InvalidOperationException or IOException)
            {
                DebugLog.Write("ssh", $"{label} : erreur à la fermeture", ex);
            }
        });
    }

    /// <summary>Connexion SFTP (deuxième session PSMP), ouverte au premier usage du panneau Fichiers.</summary>
    public Task<RemoteFileBrowser> GetBrowserAsync()
    {
        if (_browser is { IsFaulted: true } or { IsCanceled: true } || _browser is { IsCompletedSuccessfully: true, Result.IsConnected: false })
        {
            if (_browser.IsCompletedSuccessfully)
            {
                DisposeInBackground(null, null, _browser.Result);
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
        Editor?.Dispose();
        _lifetime.Cancel();
        DisposeInBackground(_shell, _client, _browser is { IsCompletedSuccessfully: true } browser ? browser.Result : null);
        _shell = null;
        _client = null;
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
    /// Installe le suivi du dossier (et le « cd » de départ) dès que le shell du serveur cible affiche son invite.
    /// Avec un vrai PSMP, la connexion à la cible peut prendre plusieurs secondes après la bannière, et ce qui est
    /// envoyé avant que le shell soit prêt est perdu : on attend donc une invite, pas seulement un silence.
    /// Sans suivi du dossier, seul le « cd » (visible) est envoyé.
    /// </summary>
    private async Task PrepareShellAsync(string? startDirectory, int connection)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline && State == SshSessionState.Connected && connection == _connection)
        {
            await Task.Delay(250);
            if (_userTyped)
            {
                // L'utilisateur a pris la main : on n'écrit pas dans sa ligne (la case « Suivre » permet d'installer le suivi).
                return;
            }

            if (_lastData != default && DateTime.UtcNow - _lastData > TimeSpan.FromMilliseconds(400) && IsAtPrompt())
            {
                Send(_followTerminal
                    ? WorkingDirectory.InjectionFor(Emulator.CursorColumn, Emulator.Columns, startDirectory)
                    : WorkingDirectory.ChangeDirectoryCommand(startDirectory!));
                return;
            }
        }
    }

    private bool IsAtPrompt() =>
        !Emulator.IsAlternateScreen && Emulator.CursorColumn > 0
        && WorkingDirectory.LooksLikePrompt(Emulator.GetText(Emulator.CursorRow, 0, Emulator.CursorRow, Emulator.CursorColumn - 1));

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
