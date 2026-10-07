using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberArkTerm.Core;

/// <summary>
/// Membre d'un safe (utilisateur ou groupe) et ses droits, tel que renvoyé par
/// <c>GET /PasswordVault/API/Safes/{safe}/Members</c>.
/// </summary>
public sealed class SafeMember
{
    public string MemberName { get; set; } = "";

    /// <summary>« User » ou « Group ».</summary>
    public string? MemberType { get; set; }

    /// <summary>Utilisateur prédéfini du coffre (Administrator, Auditors...).</summary>
    public bool IsPredefinedUser { get; set; }

    /// <summary>Date de fin d'appartenance au safe : secondes Unix, ou texte selon la version du PVWA ; null = sans fin.</summary>
    public JsonElement? MembershipExpirationDate { get; set; }

    public SafePermissions Permissions { get; set; } = new();

    [JsonIgnore]
    public bool IsGroup => string.Equals(MemberType, "Group", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fin d'appartenance au safe (heure locale), si le PVWA en indique une.</summary>
    [JsonIgnore]
    public DateTime? Expires => MembershipExpirationDate switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt64(out var seconds) => UnixTime.ToLocal(seconds),
        { ValueKind: JsonValueKind.String } s when DateTime.TryParse(s.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) => date.ToLocalTime(),
        _ => null,
    };
}

/// <summary>Droits d'un membre sur un safe (noms de l'API du PVWA).</summary>
public sealed class SafePermissions
{
    public bool UseAccounts { get; set; }

    public bool RetrieveAccounts { get; set; }

    public bool ListAccounts { get; set; }

    public bool AddAccounts { get; set; }

    public bool UpdateAccountContent { get; set; }

    public bool UpdateAccountProperties { get; set; }

    public bool InitiateCPMAccountManagementOperations { get; set; }

    public bool SpecifyNextAccountContent { get; set; }

    public bool RenameAccounts { get; set; }

    public bool DeleteAccounts { get; set; }

    public bool UnlockAccounts { get; set; }

    public bool ManageSafe { get; set; }

    public bool ManageSafeMembers { get; set; }

    public bool BackupSafe { get; set; }

    public bool ViewAuditLog { get; set; }

    public bool ViewSafeMembers { get; set; }

    public bool AccessWithoutConfirmation { get; set; }

    public bool CreateFolders { get; set; }

    public bool DeleteFolders { get; set; }

    public bool MoveAccountsAndFolders { get; set; }

    public bool RequestsAuthorizationLevel1 { get; set; }

    public bool RequestsAuthorizationLevel2 { get; set; }

    /// <summary>Accorde ou retire un droit désigné par son nom dans l'API (« AddAccounts »...).</summary>
    public void Set(string permission, bool granted) =>
        (typeof(SafePermissions).GetProperty(permission) ?? throw new ArgumentException(permission, nameof(permission)))
            .SetValue(this, granted);

    /// <summary>Droits accordés, dans l'ordre de l'écran « Membres » du PVWA, par leur nom dans l'API.</summary>
    public IEnumerable<string> Granted()
    {
        foreach (var (name, granted) in All())
        {
            if (granted)
            {
                yield return name;
            }
        }
    }

    /// <summary>Tous les droits, dans l'ordre de l'écran « Membres » du PVWA, par leur nom dans l'API.</summary>
    public IEnumerable<(string Name, bool Granted)> All() =>
    [
        (nameof(UseAccounts), UseAccounts),
        (nameof(RetrieveAccounts), RetrieveAccounts),
        (nameof(ListAccounts), ListAccounts),
        (nameof(AddAccounts), AddAccounts),
        (nameof(UpdateAccountContent), UpdateAccountContent),
        (nameof(UpdateAccountProperties), UpdateAccountProperties),
        (nameof(InitiateCPMAccountManagementOperations), InitiateCPMAccountManagementOperations),
        (nameof(SpecifyNextAccountContent), SpecifyNextAccountContent),
        (nameof(RenameAccounts), RenameAccounts),
        (nameof(DeleteAccounts), DeleteAccounts),
        (nameof(UnlockAccounts), UnlockAccounts),
        (nameof(ManageSafe), ManageSafe),
        (nameof(ManageSafeMembers), ManageSafeMembers),
        (nameof(BackupSafe), BackupSafe),
        (nameof(ViewAuditLog), ViewAuditLog),
        (nameof(ViewSafeMembers), ViewSafeMembers),
        (nameof(AccessWithoutConfirmation), AccessWithoutConfirmation),
        (nameof(CreateFolders), CreateFolders),
        (nameof(DeleteFolders), DeleteFolders),
        (nameof(MoveAccountsAndFolders), MoveAccountsAndFolders),
        (nameof(RequestsAuthorizationLevel1), RequestsAuthorizationLevel1),
        (nameof(RequestsAuthorizationLevel2), RequestsAuthorizationLevel2),
    ];
}

/// <summary>
/// Membre à ajouter à un safe (<c>POST /API/Safes/{safe}/Members</c>) ou dont les droits changent
/// (<c>PUT /API/Safes/{safe}/Members/{membre}</c>).
/// </summary>
public sealed class SafeMemberChange
{
    public required string MemberName { get; init; }

    /// <summary>« User » ou « Group ».</summary>
    public string MemberType { get; init; } = "User";

    /// <summary>Où chercher le membre : « Vault » (utilisateurs du coffre) ou le nom de l'annuaire LDAP.</summary>
    public string SearchIn { get; init; } = "Vault";

    /// <summary>Fin d'appartenance au safe ; null = sans fin.</summary>
    public DateTime? Expires { get; init; }

    public required SafePermissions Permissions { get; init; }

    internal Dictionary<string, object?> ToAddBody() => new()
    {
        ["memberName"] = MemberName.Trim(),
        ["searchIn"] = string.IsNullOrWhiteSpace(SearchIn) ? "Vault" : SearchIn.Trim(),
        ["memberType"] = MemberType,
        ["membershipExpirationDate"] = ExpirationSeconds,
        ["permissions"] = Permissions,
    };

    internal Dictionary<string, object?> ToUpdateBody() => new()
    {
        ["membershipExpirationDate"] = ExpirationSeconds,
        ["permissions"] = Permissions,
    };

    /// <summary>Fin de la journée choisie (heure locale), en secondes Unix.</summary>
    private long? ExpirationSeconds => Expires is { } date ? UnixTime.EndOfDay(date, TimeZoneInfo.Local) : null;
}
