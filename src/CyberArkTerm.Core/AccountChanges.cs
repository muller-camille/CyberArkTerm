namespace CyberArkTerm.Core;

/// <summary>Valeurs saisies dans « Modifier le compte » (le safe et le mot de passe ne se changent pas ici).</summary>
public sealed record AccountEdit(
    string Name,
    string Address,
    string UserName,
    string PlatformId,
    string LogonDomain,
    string RemoteMachines,
    bool AutomaticManagement,
    string ManualManagementReason);

/// <summary>Opération JSON Patch envoyée à <c>PATCH /API/Accounts/{id}</c>.</summary>
public sealed record PatchOperation(string Op, string Path, object? Value = null);

/// <summary>Différences entre un compte et les valeurs saisies, sous forme d'opérations JSON Patch.</summary>
public static class AccountChanges
{
    /// <summary>
    /// Seuls les champs modifiés sont envoyés : « add » pour une propriété jusque-là vide, « remove » pour la vider,
    /// « replace » sinon. Un nom vidé est ignoré (le compte garde le sien). Sans modification, la liste est vide.
    /// </summary>
    public static List<PatchOperation> Diff(PvwaAccount original, AccountEdit edit)
    {
        var ops = new List<PatchOperation>();
        void Replace(string path, string? before, string after)
        {
            if (after.Length > 0 && !string.Equals(before?.Trim(), after, StringComparison.Ordinal))
            {
                ops.Add(new PatchOperation("replace", path, after));
            }
        }

        void Optional(string path, string? before, string after)
        {
            before = before?.Trim() ?? "";
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                return;
            }

            ops.Add(before.Length == 0 ? new PatchOperation("add", path, after)
                : after.Length == 0 ? new PatchOperation("remove", path)
                : new PatchOperation("replace", path, after));
        }

        Replace("/name", original.Name, edit.Name.Trim());
        Replace("/address", original.Address, edit.Address.Trim());
        Replace("/userName", original.UserName, edit.UserName.Trim());
        Replace("/platformId", original.PlatformId, edit.PlatformId.Trim());
        Optional("/platformAccountProperties/LogonDomain", original.LogonDomain, edit.LogonDomain.Trim());

        var machines = string.IsNullOrWhiteSpace(edit.RemoteMachines) ? "" : NewAccount.NormalizeMachines(edit.RemoteMachines);
        Optional("/remoteMachinesAccess/remoteMachines", original.RemoteMachines, machines);
        bool restricted = original.RemoteMachinesAccess?.AccessRestrictedToRemoteMachines == true;
        if (restricted != (machines.Length > 0))
        {
            ops.Add(new PatchOperation("replace", "/remoteMachinesAccess/accessRestrictedToRemoteMachines", machines.Length > 0));
        }

        var management = original.SecretManagement;
        if ((management?.AutomaticManagementEnabled ?? true) != edit.AutomaticManagement)
        {
            ops.Add(new PatchOperation("replace", "/secretManagement/automaticManagementEnabled", edit.AutomaticManagement));
        }

        if (!edit.AutomaticManagement)
        {
            Optional("/secretManagement/manualManagementReason", management?.ManualManagementReason, edit.ManualManagementReason.Trim());
        }

        return ops;
    }
}
