using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberArkTerm.Core;

/// <summary>
/// Compte privilégié tel que renvoyé par <c>GET /PasswordVault/API/Accounts</c>.
/// </summary>
public sealed class PvwaAccount
{
    public string Id { get; set; } = "";

    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? UserName { get; set; }

    public string? PlatformId { get; set; }

    public string? SafeName { get; set; }

    public string? SecretType { get; set; }

    public Dictionary<string, JsonElement>? PlatformAccountProperties { get; set; }

    public RemoteMachinesAccess? RemoteMachinesAccess { get; set; }

    public SecretManagement? SecretManagement { get; set; }

    /// <summary>Date de création, en secondes Unix.</summary>
    public long? CreatedTime { get; set; }

    [JsonIgnore]
    public string LogonDomain => GetPlatformProperty("LogonDomain");

    /// <summary>Machines sur lesquelles le compte peut ouvrir une session PSM (séparées par des « ; »).</summary>
    [JsonIgnore]
    public string RemoteMachines => RemoteMachinesAccess?.RemoteMachines ?? "";

    [JsonIgnore]
    public DateTime? Created => CreatedTime is long t ? DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime : null;

    public string GetPlatformProperty(string key)
    {
        if (PlatformAccountProperties is null)
        {
            return "";
        }

        foreach (var (name, value) in PlatformAccountProperties)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
            }
        }

        return "";
    }
}

public sealed class RemoteMachinesAccess
{
    public string? RemoteMachines { get; set; }

    public bool AccessRestrictedToRemoteMachines { get; set; }
}

/// <summary>Gestion du mot de passe par le CPM (dates en secondes Unix).</summary>
public sealed class SecretManagement
{
    public bool AutomaticManagementEnabled { get; set; }

    /// <summary>Résultat de la dernière opération du CPM (« success », « failure »...).</summary>
    public string? Status { get; set; }

    public string? ManualManagementReason { get; set; }

    public long? LastModifiedTime { get; set; }

    public long? LastVerifiedTime { get; set; }

    public long? LastReconciledTime { get; set; }

    /// <summary>La dernière opération du CPM (changement, vérification, réconciliation) a échoué.</summary>
    [JsonIgnore]
    public bool Failed => Status?.Contains("fail", StringComparison.OrdinalIgnoreCase) == true;

    [JsonIgnore]
    public DateTime? LastModified => Local(LastModifiedTime);

    [JsonIgnore]
    public DateTime? LastVerified => Local(LastVerifiedTime);

    [JsonIgnore]
    public DateTime? LastReconciled => Local(LastReconciledTime);

    private static DateTime? Local(long? seconds) => seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value).LocalDateTime : null;
}
