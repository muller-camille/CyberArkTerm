using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core;

/// <summary>
/// Serveur d'une liste exportée ou partagée : la configuration de connexion d'un serveur « Mes serveurs » (compte
/// CyberArk, mode, composant, machine cible, dossier SFTP…), sans secret ni rien de personnel (fichiers suivis).
/// </summary>
public sealed class ServerEntry : IJsonOnDeserializing
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Lecture d'un fichier : sans « id », l'identifiant vient de <see cref="ServerListFile.Parse"/>, stable.</summary>
    void IJsonOnDeserializing.OnDeserializing() => Id = "";

    /// <summary>Compte CyberArk (ID PVWA) : il n'est utilisable que par ceux qui le voient dans le coffre.</summary>
    public string AccountId { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Dossier dans la liste, « Prod/Web » ; vide = racine.</summary>
    public string Folder { get; set; } = "";

    public ConnectMode Mode { get; set; } = ConnectMode.Psm;

    public string? Component { get; set; }

    public string? RemoteMachine { get; set; }

    /// <summary>Motif proposé ; jamais mis dans une liste partagée (il reste personnel).</summary>
    public string? Reason { get; set; }

    public string? StartDirectory { get; set; }

    public string? Address { get; set; }

    public string? UserName { get; set; }

    public string? PlatformId { get; set; }

    public string? SafeName { get; set; }

    /// <summary>Liste partagée : qui a ajouté le serveur, et quand (UTC).</summary>
    public string? AddedBy { get; set; }

    public DateTime? AddedAt { get; set; }

    public static ServerEntry From(SavedSession session) => new()
    {
        AccountId = session.AccountId,
        Name = session.Name,
        Folder = SessionFolders.Normalize(session.Folder),
        Mode = session.Mode,
        Component = session.Component,
        RemoteMachine = session.RemoteMachine,
        Reason = session.Reason,
        StartDirectory = session.StartDirectory,
        Address = session.Address,
        UserName = session.UserName,
        PlatformId = session.PlatformId,
        SafeName = session.SafeName,
    };

    /// <summary>
    /// Serveur « Mes serveurs » équivalent, pour le PVWA <paramref name="pvwaHost"/> ; avec <paramref name="keepId"/>,
    /// même identifiant (affichage d'une liste partagée), sinon un nouveau (import).
    /// </summary>
    public SavedSession ToSession(string pvwaHost, bool keepId = false) => new()
    {
        Id = keepId ? Id : Guid.NewGuid().ToString("N"),
        AccountId = AccountId,
        PvwaHost = pvwaHost,
        Name = Name.Length > 0 ? Name : $"{UserName}@{Address}",
        Folder = SessionFolders.Normalize(Folder),
        Mode = Mode,
        Component = Component,
        RemoteMachine = RemoteMachine,
        Reason = Reason,
        StartDirectory = StartDirectory,
        Address = Address,
        UserName = UserName,
        PlatformId = PlatformId,
        SafeName = SafeName,
    };

    /// <summary>Même serveur : même compte, même mode, même composant, même machine cible, même dossier.</summary>
    public static string Key(string accountId, ConnectMode mode, string? component, string? remoteMachine, string folder) =>
        string.Join('\u001f', accountId, mode, (component ?? "").Trim(), (remoteMachine ?? "").Trim(), SessionFolders.Normalize(folder))
            .ToUpperInvariant();

    public string Key() => Key(AccountId, Mode, Component, RemoteMachine, Folder);

    public static string Key(SavedSession session) =>
        Key(session.AccountId, session.Mode, session.Component, session.RemoteMachine, session.Folder);
}

/// <summary>Modification d'une liste partagée : qui, quand, quoi.</summary>
public sealed class SharedChange
{
    public int Revision { get; set; }

    /// <summary>Date (UTC).</summary>
    public DateTime At { get; set; }

    /// <summary>Auteur déclaré par CyberArkTerm : compte CyberArk et compte Windows.</summary>
    public string By { get; set; } = "";

    public SharedAction Action { get; set; }

    public string? Server { get; set; }

    public string? Folder { get; set; }

    /// <summary>Révision restaurée, nom de la liste créée…</summary>
    public string? Detail { get; set; }
}

public enum SharedAction
{
    Created,
    Added,
    Removed,
    Restored,
}

/// <summary>
/// Fichier de serveurs : export de « Mes serveurs », ou liste partagée (fichier JSON sur un partage réseau, avec sa
/// révision et le journal des modifications).
/// </summary>
public sealed class ServerListFile
{
    public const string ExportFormat = "CyberArkTerm.Servers";
    public const string SharedFormat = "CyberArkTerm.SharedServers";

    /// <summary>Taille maximale lue (un fichier plus gros n'est pas une liste de serveurs).</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>Modifications gardées dans le journal d'une liste partagée (les plus anciennes partent d'abord).</summary>
    public const int MaxChanges = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><see cref="ExportFormat"/> ou <see cref="SharedFormat"/>.</summary>
    public string Format { get; set; } = "";

    public int Version { get; set; } = 1;

    /// <summary>Nom affiché d'une liste partagée.</summary>
    public string? Name { get; set; }

    /// <summary>PVWA dont viennent les comptes (les ID de comptes ne valent que pour ce coffre).</summary>
    public string Pvwa { get; set; } = "";

    /// <summary>Dernier enregistrement (UTC).</summary>
    public DateTime Saved { get; set; }

    /// <summary>Révision d'une liste partagée, augmentée à chaque modification.</summary>
    public int Revision { get; set; }

    /// <summary>Dossiers, y compris vides.</summary>
    public List<string> Folders { get; set; } = [];

    public List<ServerEntry> Servers { get; set; } = [];

    public List<SharedChange> Changes { get; set; } = [];

    [JsonIgnore]
    public bool IsShared => Format == SharedFormat;

    /// <summary>Export des serveurs « Mes serveurs » du PVWA <paramref name="pvwaHost"/> (dossiers compris).</summary>
    public static ServerListFile Export(AppSettings settings, string pvwaHost) => new()
    {
        Format = ExportFormat,
        Pvwa = pvwaHost,
        Saved = DateTime.UtcNow,
        Folders = settings.SessionFolderList.Select(SessionFolders.Normalize).Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
        Servers = settings.Sessions.Where(s => SessionLibrary.IsForHost(s, pvwaHost)).Select(ServerEntry.From).ToList(),
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static string StableId(ServerEntry server) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            server.AccountId, server.Name, server.Folder, server.Mode, server.Component, server.RemoteMachine, server.StartDirectory))))[..32]
            .ToLowerInvariant();

    /// <summary>Lit un fichier de serveurs (export ou liste partagée).</summary>
    /// <exception cref="InvalidDataException">Pas un fichier de serveurs CyberArkTerm, ou version trop récente.</exception>
    public static ServerListFile Parse(string json)
    {
        ServerListFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ServerListFile>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(CoreStrings.ServerListInvalid, e);
        }

        if (file is null || file.Format is not (ExportFormat or SharedFormat))
        {
            throw new InvalidDataException(CoreStrings.ServerListInvalid);
        }

        if (file.Version > 1)
        {
            throw new InvalidDataException(string.Format(System.Globalization.CultureInfo.CurrentCulture, CoreStrings.ServerListTooNew, file.Version));
        }

        file.Folders ??= [];
        file.Servers ??= [];
        file.Changes ??= [];
        file.Servers.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.AccountId));
        foreach (var server in file.Servers)
        {
            server.Name ??= "";
            server.Folder = SessionFolders.Normalize(server.Folder);
            // Fichier écrit à la main sans « id » : un identifiant tiré de l'entrée, le même à chaque lecture (le retrait
            // d'un serveur relit le fichier et le retrouve par son identifiant).
            server.Id = string.IsNullOrWhiteSpace(server.Id) ? StableId(server) : server.Id;
            if (file.IsShared)
            {
                // Le motif d'accès n'est jamais partagé : un motif ajouté à la main dans le fichier serait envoyé au PVWA
                // comme celui de chaque utilisateur, sans qu'il le voie.
                server.Reason = null;
            }
        }

        file.Folders = file.Folders.Select(SessionFolders.Normalize).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return file;
    }

    /// <summary>Dossiers de la liste : ceux déclarés et ceux des serveurs, avec leurs parents.</summary>
    public IEnumerable<string> AllFolders()
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders.Concat(Servers.Select(s => s.Folder)))
        {
            for (var f = SessionFolders.Normalize(folder); f.Length > 0; f = SessionFolders.Parent(f))
            {
                all.Add(f);
            }
        }

        return all;
    }
}

/// <summary>Import d'un fichier de serveurs dans « Mes serveurs ».</summary>
public sealed class ServerImport
{
    private ServerImport(List<ServerEntry> added, int duplicates, List<string> folders)
    {
        Added = added;
        Duplicates = duplicates;
        NewFolders = folders;
    }

    /// <summary>Serveurs absents de « Mes serveurs ».</summary>
    public IReadOnlyList<ServerEntry> Added { get; }

    /// <summary>Serveurs déjà présents (même compte, mode, composant, machine cible et dossier) : ignorés.</summary>
    public int Duplicates { get; }

    public IReadOnlyList<string> NewFolders { get; }

    /// <summary>Serveurs importés qui ouvrent un compte sur une machine cible : à montrer avant d'importer.</summary>
    public IEnumerable<ServerEntry> WithTargetMachine => Added.Where(s => !string.IsNullOrWhiteSpace(s.RemoteMachine));

    public static ServerImport Plan(AppSettings settings, string pvwaHost, ServerListFile file)
    {
        var existing = settings.Sessions.Where(s => SessionLibrary.IsForHost(s, pvwaHost)).Select(ServerEntry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var added = new List<ServerEntry>();
        int duplicates = 0;
        foreach (var server in file.Servers)
        {
            if (existing.Add(server.Key()))
            {
                added.Add(server);
            }
            else
            {
                duplicates++;
            }
        }

        var known = settings.SessionFolderList.Select(SessionFolders.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = file.AllFolders().Where(f => !known.Contains(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new ServerImport(added, duplicates, folders);
    }

    /// <summary>Ajoute les dossiers et serveurs prévus ; renvoie le nombre de serveurs ajoutés.</summary>
    public int Apply(AppSettings settings, string pvwaHost)
    {
        foreach (var folder in NewFolders)
        {
            SessionLibrary.AddFolder(settings, folder);
        }

        foreach (var server in Added)
        {
            settings.Sessions.Add(server.ToSession(pvwaHost));
        }

        return Added.Count;
    }
}
