namespace CyberArkTerm.Core;

/// <summary>Profil de droits proposé pour un membre de safe.</summary>
public enum SafeProfile
{
    ReadOnly,
    AccountUser,
    AccountManager,
    Full,

    /// <summary>Droits qui ne correspondent à aucun profil.</summary>
    Custom,
}

/// <summary>
/// Profils de droits d'un membre de safe, du plus restreint au plus large, et droits sensibles (ceux qui donnent la main
/// sur le safe ou sur ses comptes) : on choisit un profil au lieu de cocher les 22 droits un par un.
/// </summary>
public static class SafeProfiles
{
    private static readonly string[] ReadOnlyRights =
        [nameof(SafePermissions.ListAccounts), nameof(SafePermissions.ViewAuditLog), nameof(SafePermissions.ViewSafeMembers)];

    /// <summary>Se connecter aux comptes (PSM, PSMP) sans voir leurs mots de passe.</summary>
    private static readonly string[] AccountUserRights = [.. ReadOnlyRights, nameof(SafePermissions.UseAccounts)];

    private static readonly string[] AccountManagerRights =
    [
        .. AccountUserRights,
        nameof(SafePermissions.RetrieveAccounts), nameof(SafePermissions.AddAccounts), nameof(SafePermissions.UpdateAccountContent), nameof(SafePermissions.UpdateAccountProperties),
        nameof(SafePermissions.InitiateCPMAccountManagementOperations), nameof(SafePermissions.SpecifyNextAccountContent),
        nameof(SafePermissions.RenameAccounts), nameof(SafePermissions.DeleteAccounts), nameof(SafePermissions.UnlockAccounts),
        nameof(SafePermissions.CreateFolders), nameof(SafePermissions.DeleteFolders), nameof(SafePermissions.MoveAccountsAndFolders),
    ];

    /// <summary>
    /// Droits sensibles : gérer le safe ou ses membres (s'accorder tout le reste), accéder sans validation, supprimer
    /// des comptes.
    /// </summary>
    public static IReadOnlyList<string> Sensitive { get; } =
    [
        nameof(SafePermissions.ManageSafe), nameof(SafePermissions.ManageSafeMembers),
        nameof(SafePermissions.AccessWithoutConfirmation), nameof(SafePermissions.DeleteAccounts),
    ];

    public static IReadOnlyList<SafeProfile> Choices { get; } =
        [SafeProfile.ReadOnly, SafeProfile.AccountUser, SafeProfile.AccountManager, SafeProfile.Full];

    /// <summary>Droits accordés par un profil (noms de l'API).</summary>
    public static IReadOnlySet<string> Rights(SafeProfile profile) => profile switch
    {
        SafeProfile.ReadOnly => ReadOnlyRights.ToHashSet(),
        SafeProfile.AccountUser => AccountUserRights.ToHashSet(),
        SafeProfile.AccountManager => AccountManagerRights.ToHashSet(),
        SafeProfile.Full => new SafePermissions().All().Select(p => p.Name).ToHashSet(),
        _ => new HashSet<string>(),
    };

    /// <summary>Profil qui correspond exactement à ces droits, sinon <see cref="SafeProfile.Custom"/>.</summary>
    public static SafeProfile Match(IEnumerable<string> granted)
    {
        var set = granted.ToHashSet();
        return Choices.FirstOrDefault(p => Rights(p).SetEquals(set), SafeProfile.Custom);
    }

    /// <summary>Droits sensibles accordés par <paramref name="after"/> et absents de <paramref name="before"/>.</summary>
    public static IReadOnlyList<string> SensitiveAdded(IEnumerable<string> before, IEnumerable<string> after)
    {
        var had = before.ToHashSet();
        var has = after.ToHashSet();
        return Sensitive.Where(p => has.Contains(p) && !had.Contains(p)).ToList();
    }
}
