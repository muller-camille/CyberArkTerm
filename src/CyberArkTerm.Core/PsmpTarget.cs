using System.Globalization;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core;

/// <summary>
/// Chaîne de connexion SSH via PSM for SSH (PSMP) :
/// <c>ssh &lt;utilisateur coffre&gt;@&lt;compte cible&gt;[#domaine]@&lt;adresse cible&gt;@&lt;PSMP&gt;</c>.
/// </summary>
public static class PsmpTarget
{
    private static readonly char[] Forbidden = [';', '"', '\'', '`', '|', '&', '<', '>', '^', '%', '!', '(', ')'];

    /// <summary>Partie « utilisateur » de la commande SSH (tout ce qui précède l'adresse du PSMP).</summary>
    public static string BuildLogin(string vaultUser, PvwaAccount account, string? remoteMachine = null)
    {
        var target = Require(account.UserName, CoreStrings.AccountHasNoUser);
        var address = Require(string.IsNullOrWhiteSpace(remoteMachine) ? account.Address : remoteMachine, CoreStrings.AccountHasNoAddress);
        var domain = account.LogonDomain.Trim();
        var login = $"{Require(vaultUser, CoreStrings.VaultUserUnknown)}@{target}{(domain.Length > 0 ? "#" + domain : "")}@{address}";
        Validate(login, CoreStrings.SshLoginWhat);
        return login;
    }

    /// <summary>Vérifie une valeur passée en argument à ssh / Windows Terminal (pas d'espace ni de métacaractère).</summary>
    public static void Validate(string value, string what)
    {
        if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || Forbidden.Contains(c)))
        {
            throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, CoreStrings.CharacterNotAllowed, what, value));
        }
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();
}
