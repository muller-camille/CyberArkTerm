using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CyberArkTerm.Core;

/// <summary>
/// Compte à créer dans un safe (<c>POST /PasswordVault/API/Accounts</c>). Le mot de passe est un tableau que
/// l'appelant efface après l'envoi ; il n'est jamais converti en chaîne.
/// </summary>
public sealed class NewAccount
{
    public required string SafeName { get; init; }

    public required string PlatformId { get; init; }

    public required string Address { get; init; }

    public required string UserName { get; init; }

    /// <summary>Nom du compte dans le coffre ; vide = choisi par le PVWA.</summary>
    public string? Name { get; init; }

    /// <summary>Domaine de connexion (comptes de domaine), propriété « LogonDomain » de la plateforme.</summary>
    public string? LogonDomain { get; init; }

    /// <summary>Mot de passe ; vide = compte créé sans mot de passe.</summary>
    public char[]? Secret { get; init; }

    /// <summary>Mot de passe géré par le CPM (changement, vérification).</summary>
    public bool AutomaticManagement { get; init; } = true;

    /// <summary>Motif de la gestion manuelle, quand <see cref="AutomaticManagement"/> est désactivé.</summary>
    public string? ManualManagementReason { get; init; }

    /// <summary>Machines autorisées (« srv1;srv2 ») : l'accès PSM est alors limité à ces machines.</summary>
    public string? RemoteMachines { get; init; }

    /// <summary>
    /// Corps JSON de la requête, écrit directement en UTF-8 (le mot de passe ne passe par aucune chaîne). Le tampon est
    /// dimensionné d'avance pour ne pas être réalloué (une copie abandonnée garderait le mot de passe) ; l'appelant
    /// l'efface avec <see cref="ArrayBufferWriter{T}.Clear"/> après l'envoi. Le mot de passe est écrit en dernier.
    /// </summary>
    internal ArrayBufferWriter<byte> ToJson()
    {
        string?[] fields = [SafeName, PlatformId, Address, UserName, Name, LogonDomain, ManualManagementReason, RemoteMachines];
        int capacity = 1024 + 6 * (fields.Sum(f => f?.Length ?? 0) + (Secret?.Length ?? 0));
        var buffer = new ArrayBufferWriter<byte>(capacity);
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            WriteIfAny(json, "name", Name);
            json.WriteString("address", Address.Trim());
            json.WriteString("userName", UserName.Trim());
            json.WriteString("platformId", PlatformId.Trim());
            json.WriteString("safeName", SafeName.Trim());
            json.WriteString("secretType", "password");
            if (!string.IsNullOrWhiteSpace(LogonDomain))
            {
                json.WriteStartObject("platformAccountProperties");
                json.WriteString("LogonDomain", LogonDomain.Trim());
                json.WriteEndObject();
            }

            json.WriteStartObject("secretManagement");
            json.WriteBoolean("automaticManagementEnabled", AutomaticManagement);
            if (!AutomaticManagement)
            {
                WriteIfAny(json, "manualManagementReason", ManualManagementReason);
            }

            json.WriteEndObject();
            if (!string.IsNullOrWhiteSpace(RemoteMachines))
            {
                json.WriteStartObject("remoteMachinesAccess");
                json.WriteString("remoteMachines", NormalizeMachines(RemoteMachines));
                json.WriteBoolean("accessRestrictedToRemoteMachines", true);
                json.WriteEndObject();
            }

            if (Secret is { Length: > 0 } secret)
            {
                json.WriteString("secret", secret);
            }

            json.WriteEndObject();
        }

        return buffer;
    }

    /// <summary>« srv1, srv2 ;srv3 » → « srv1;srv2;srv3 ».</summary>
    public static string NormalizeMachines(string machines) =>
        string.Join(';', machines.Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static void WriteIfAny(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            json.WriteString(name, value.Trim());
        }
    }
}
