namespace CyberArkTerm.App;

public enum ConnectMode
{
    /// <summary>Session PSM ouverte via un fichier RDP (bouton « Connect » du PVWA).</summary>
    Psm,

    /// <summary>Session SSH directe via PSM for SSH (PSMP).</summary>
    Ssh,
}

/// <summary>Choix de l'utilisateur pour une connexion.</summary>
public sealed record ConnectRequest(
    ConnectMode Mode,
    string Component,
    string? RemoteMachine = null,
    string? Reason = null,
    string? TicketingSystem = null,
    string? TicketId = null,
    bool RememberComponent = false);
