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
        var target = Require(account.UserName, "Le compte n'a pas de nom d'utilisateur.");
        var address = Require(string.IsNullOrWhiteSpace(remoteMachine) ? account.Address : remoteMachine, "Le compte n'a pas d'adresse cible.");
        var domain = account.LogonDomain.Trim();
        var login = $"{Require(vaultUser, "Utilisateur du coffre inconnu.")}@{target}{(domain.Length > 0 ? "#" + domain : "")}@{address}";
        Validate(login, "l'identifiant SSH");
        return login;
    }

    /// <summary>Vérifie une valeur passée en argument à ssh / Windows Terminal (pas d'espace ni de métacaractère).</summary>
    public static void Validate(string value, string what)
    {
        if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || Forbidden.Contains(c)))
        {
            throw new ArgumentException($"Caractère non autorisé dans {what} : « {value} ».");
        }
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();
}
