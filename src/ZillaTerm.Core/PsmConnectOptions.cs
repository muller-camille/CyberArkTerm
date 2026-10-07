namespace ZillaTerm.Core;

/// <summary>Paramètres d'une demande de connexion PSM (<c>POST API/Accounts/{id}/PSMConnect</c>).</summary>
public sealed class PsmConnectOptions
{
    /// <summary>Composant de connexion PSM, par ex. <c>PSM-RDP</c>, <c>PSM-SSH</c>, <c>PSM-WinSCP</c>.</summary>
    public required string ConnectionComponent { get; init; }

    /// <summary>Motif d'accès, obligatoire si la plateforme l'exige.</summary>
    public string? Reason { get; init; }

    public string? TicketingSystemName { get; init; }

    public string? TicketId { get; init; }

    /// <summary>Machine cible pour un compte de domaine (paramètre <c>PSMRemoteMachine</c>).</summary>
    public string? RemoteMachine { get; init; }

    internal Dictionary<string, object> ToRequestBody()
    {
        var body = new Dictionary<string, object> { ["ConnectionComponent"] = ConnectionComponent.Trim() };
        if (!string.IsNullOrWhiteSpace(Reason))
        {
            body["reason"] = Reason.Trim();
        }

        if (!string.IsNullOrWhiteSpace(TicketingSystemName))
        {
            body["TicketingSystemName"] = TicketingSystemName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(TicketId))
        {
            body["TicketId"] = TicketId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(RemoteMachine))
        {
            body["ConnectionParams"] = new Dictionary<string, object>
            {
                ["PSMRemoteMachine"] = new Dictionary<string, object> { ["value"] = RemoteMachine.Trim(), ["ShouldSave"] = false },
            };
        }

        return body;
    }
}
