namespace ZillaTerm.Core;

/// <summary>Opération demandée au CPM sur un compte (traitée de façon asynchrone par le CPM).</summary>
public enum CpmAction
{
    /// <summary>Vérifie que le mot de passe du coffre ouvre bien le compte sur le serveur.</summary>
    Verify,

    /// <summary>Change le mot de passe sur le serveur et dans le coffre.</summary>
    Change,

    /// <summary>Remet le mot de passe du serveur en accord avec le coffre, avec le compte de réconciliation.</summary>
    Reconcile,
}

/// <summary>Motif, ticket et usage d'une récupération de mot de passe (<c>POST /API/Accounts/{id}/Password/Retrieve</c>).</summary>
public sealed class RetrieveOptions
{
    public string? Reason { get; init; }

    public string? TicketingSystemName { get; init; }

    public string? TicketId { get; init; }

    internal Dictionary<string, object> ToRequestBody()
    {
        // « copy » : l'audit du coffre indique une copie, comme le bouton « Copy » du PVWA.
        var body = new Dictionary<string, object> { ["ActionType"] = "copy", ["isUse"] = false };
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

        return body;
    }
}
