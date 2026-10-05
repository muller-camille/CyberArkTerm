using CyberArkTerm.App.Localization;

namespace CyberArkTerm.App;

/// <summary>Libellés traduits des droits sur un safe (ressources « SafePerm » + nom du droit dans l'API du PVWA).</summary>
internal static class SafePermissionText
{
    /// <summary>« Ajouter des comptes » pour « AddAccounts » ; le nom de l'API si aucune traduction n'existe.</summary>
    public static string Label(string permission) =>
        Strings.ResourceManager.GetString("SafePerm" + permission, Strings.Culture) ?? permission;
}
