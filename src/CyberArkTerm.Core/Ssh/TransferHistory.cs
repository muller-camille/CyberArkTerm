using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Transfert passé : ce qui a été transféré, où, quand, et la vérification de chaque fichier.</summary>
public sealed class TransferRecord
{
    /// <summary>Fin du transfert (UTC).</summary>
    public DateTime Time { get; init; }

    public bool Upload { get; init; }

    /// <summary>Session d'origine (compte@cible).</summary>
    public string Server { get; init; } = "";

    /// <summary>Ce qui a été transféré : nom du fichier ou du dossier, ou nombre d'éléments.</summary>
    public string Label { get; init; } = "";

    /// <summary>Dossier du serveur (envoi) ou emplacement sur ce poste (téléchargement).</summary>
    public string Destination { get; init; } = "";

    /// <summary>SCP ou SFTP pour un envoi ; null pour un téléchargement ou un glisser vers l'Explorateur.</summary>
    public string? Protocol { get; init; }

    public TransferState State { get; init; }

    public string? Error { get; init; }

    public int FileCount { get; init; }

    /// <summary>Vérification de chaque fichier (les premiers seulement si <see cref="FilesTruncated"/>).</summary>
    public List<TransferCheck> Files { get; init; } = [];

    /// <summary>Trop de fichiers pour tous les garder : seuls les premiers figurent dans <see cref="Files"/>.</summary>
    public bool FilesTruncated { get; init; }

    /// <summary>Fichiers différents de l'original.</summary>
    [JsonIgnore]
    public int Different => Files.Count(f => f.Verified && !f.Matches);

    /// <summary>Fichiers identiques des deux côtés.</summary>
    [JsonIgnore]
    public int Identical => Files.Count(f => f.Matches);
}

/// <summary>
/// Historique des transferts de l'onglet Fichiers, conservé sur ce poste (fichier JSON du profil Windows, à côté des
/// préférences) : les derniers transferts d'abord, sans le contenu des fichiers ni aucun secret. Les sommes SHA-256
/// gardées permettent de revérifier un fichier plus tard.
/// </summary>
public sealed class TransferHistory
{
    /// <summary>Nombre de transferts gardés.</summary>
    public const int MaxRecords = 200;

    /// <summary>Fichiers gardés au plus pour un transfert.</summary>
    public const int MaxFilesPerRecord = 500;

    /// <summary>Fichiers gardés au plus pour tout l'historique (les plus anciens transferts partent d'abord).</summary>
    public const int MaxFiles = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly object _writeLock = new();
    private readonly string? _path;
    private long _version;
    private long _savedVersion;

    public TransferHistory(string? path = null) => _path = path;

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath)!, "transfers.json");

    /// <summary>Transferts, du plus récent au plus ancien.</summary>
    public List<TransferRecord> Records { get; private set; } = [];

    /// <summary>Lit l'historique ; un fichier absent, illisible ou corrompu donne un historique vide.</summary>
    public static TransferHistory Load(string path)
    {
        var history = new TransferHistory(path);
        try
        {
            if (File.Exists(path))
            {
                history.Records = JsonSerializer.Deserialize<List<TransferRecord>>(File.ReadAllText(path), JsonOptions) ?? [];
                history.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            history.Records = [];
        }

        return history;
    }

    /// <summary>Ajoute un transfert en tête et retire les plus anciens au-delà des limites.</summary>
    public void Add(TransferRecord record)
    {
        if (record.Files.Count > MaxFilesPerRecord)
        {
            record = new TransferRecord
            {
                Time = record.Time,
                Upload = record.Upload,
                Server = record.Server,
                Label = record.Label,
                Destination = record.Destination,
                Protocol = record.Protocol,
                State = record.State,
                Error = record.Error,
                FileCount = record.FileCount,
                // Les fichiers à revoir d'abord : ce sont eux qui comptent dans l'historique.
                Files = [.. record.Files.OrderBy(f => f.Matches).Take(MaxFilesPerRecord)],
                FilesTruncated = true,
            };
        }

        lock (_lock)
        {
            Records.Insert(0, record);
            Trim();
            _version++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Records.Clear();
            _version++;
        }
    }

    /// <summary>
    /// Enregistre l'historique (écriture dans un fichier temporaire, puis remplacement). Plusieurs enregistrements
    /// simultanés : un état plus ancien que celui déjà écrit n'est jamais réécrit par-dessus.
    /// </summary>
    public void Save()
    {
        if (_path is null)
        {
            return;
        }

        string json;
        long version;
        lock (_lock)
        {
            json = JsonSerializer.Serialize(Records, JsonOptions);
            version = _version;
        }

        lock (_writeLock)
        {
            if (version < _savedVersion)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
            _savedVersion = version;
        }
    }

    private void Trim()
    {
        if (Records.Count > MaxRecords)
        {
            Records.RemoveRange(MaxRecords, Records.Count - MaxRecords);
        }

        int files = 0;
        for (int i = 0; i < Records.Count; i++)
        {
            files += Records[i].Files.Count;
            if (files > MaxFiles && i > 0)
            {
                Records.RemoveRange(i, Records.Count - i);
                break;
            }
        }
    }
}
