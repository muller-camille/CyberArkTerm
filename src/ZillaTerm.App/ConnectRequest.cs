using ZillaTerm.Core;

namespace ZillaTerm.App;

/// <summary>Choix de l'utilisateur pour une connexion.</summary>
public sealed record ConnectRequest(
    ConnectMode Mode,
    string Component,
    string? RemoteMachine = null,
    string? Reason = null,
    string? TicketingSystem = null,
    string? TicketId = null,
    bool RememberComponent = false,
    bool X11Forwarding = false);
