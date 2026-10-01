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

public sealed class SecretManagement
{
    public bool AutomaticManagementEnabled { get; set; }

    public string? Status { get; set; }

    public string? ManualManagementReason { get; set; }

    public long? LastModifiedTime { get; set; }
}
