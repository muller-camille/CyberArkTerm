using CyberArkTerm.Core;

namespace CyberArkTerm.App;

/// <summary>Choix de l'utilisateur pour une connexion.</summary>
public sealed record ConnectRequest(
    ConnectMode Mode,
    string Component,
    string? RemoteMachine = null,
    string? Reason = null,
    string? TicketingSystem = null,
    string? TicketId = null,
    bool RememberComponent = false);
