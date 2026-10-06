using System.IO;
using System.Net.Sockets;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Vnc;

namespace CyberArkTerm.App.Services;

public enum VncSessionState
{
    Connecting,
    Connected,
    Closed,
    Failed,
}

/// <summary>
/// Session VNC d'un onglet : connexion (et reconnexion) au serveur, réception de l'écran en arrière-plan. Les
/// événements arrivent depuis d'autres fils.
/// </summary>
public sealed class VncSession(string label, string host, int port, Func<string?> password) : IDisposable
{
    private CancellationTokenSource? _lifetime;
    private bool _disposed;

    public string Label { get; } = label;

    public string Host { get; } = host;

    public int Port { get; } = port;

    public RfbClient? Client { get; private set; }

    public VncSessionState State { get; private set; } = VncSessionState.Connecting;

    public string? Error { get; private set; }

    public bool IsConnected => State == VncSessionState.Connected;

    /// <summary>État changé (depuis n'importe quel fil).</summary>
    public event Action? StateChanged;

    /// <summary>Nouveau client connecté (connexion ou reconnexion), avant le premier écran.</summary>
    public event Action<RfbClient>? ClientChanged;

    /// <summary>Connexion, ou reconnexion après une fermeture (le client précédent est libéré).</summary>
    public async Task ConnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        var lifetime = _lifetime = new CancellationTokenSource();
        SetState(VncSessionState.Connecting, null);
        RfbClient client;
        try
        {
            client = await RfbClient.ConnectAsync(Host, Port, password, lifetime.Token);
        }
        catch (Exception e) when (e is RfbException or SocketException or IOException or OperationCanceledException)
        {
            if (!lifetime.IsCancellationRequested)
            {
                DebugLog.Write("vnc", $"Connexion à {Host}:{Port} impossible", e);
                SetState(VncSessionState.Failed, ErrorText.Describe(e));
            }

            return;
        }

        if (lifetime.IsCancellationRequested)
        {
            client.Dispose();
            return;
        }

        Client = client;
        DebugLog.Write("vnc", $"Connecté à {Host}:{Port} : RFB {client.ProtocolVersion}, {client.Framebuffer.Width}x{client.Framebuffer.Height}");
        ClientChanged?.Invoke(client);
        SetState(VncSessionState.Connected, null);
        try
        {
            await Task.Run(() => client.RunAsync(lifetime.Token));
        }
        catch (Exception e) when (e is RfbException or SocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            if (!lifetime.IsCancellationRequested)
            {
                DebugLog.Write("vnc", $"Session {Host}:{Port} interrompue", e);
                SetState(VncSessionState.Closed, ErrorText.Describe(e));
            }
        }
    }

    private void SetState(VncSessionState state, string? error)
    {
        State = state;
        Error = error;
        StateChanged?.Invoke();
    }

    private void Stop()
    {
        _lifetime?.Cancel();
        _lifetime = null;
        Client?.Dispose();
        Client = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        SetState(VncSessionState.Closed, null);
    }
}
