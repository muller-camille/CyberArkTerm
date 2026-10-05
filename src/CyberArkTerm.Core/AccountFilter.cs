namespace CyberArkTerm.Core;

/// <summary>
/// Recherche plein texte côté client : chaque mot de la requête doit apparaître
/// (sans tenir compte de la casse) dans au moins un des champs affichés du compte.
/// </summary>
public static class AccountFilter
{
    public static bool Matches(PvwaAccount account, string? query) => SearchQuery.Matches(
        query,
        account.Address,
        account.UserName,
        account.Name,
        account.SafeName,
        account.PlatformId,
        account.LogonDomain,
        account.RemoteMachines);
}
