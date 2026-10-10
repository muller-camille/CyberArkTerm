using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZillaTerm.Core.KeePass;

namespace ZillaTerm.Core.Interventions;

/// <summary>Nature d'un événement du journal d'intervention.</summary>
public enum InterventionKind
{
    /// <summary>Début de l'enregistrement (utilisateur, poste, version).</summary>
    Started,

    /// <summary>Fin de l'enregistrement.</summary>
    Stopped,

    /// <summary>Note saisie par l'utilisateur.</summary>
    Note,

    /// <summary>Ligne affichée par un terminal SSH (commande tapée et son écho, ou sortie d'une commande).</summary>
    Terminal,

    /// <summary>Session ouverte, fermée ou en échec (SSH, fichiers, PSM, Bureau à distance, VNC).</summary>
    Connection,

    /// <summary>Action sur un compte ou sur ZillaTerm (mot de passe copié, CPM, accès d'urgence…).</summary>
    Action,

    /// <summary>Transfert de fichiers, avec les empreintes SHA-256.</summary>
    Transfer,

    /// <summary>Capture d'écran (image PNG).</summary>
    Screenshot,
}

/// <summary>Événement du journal d'intervention.</summary>
/// <param name="Time">Heure UTC.</param>
/// <param name="Source">Session ou serveur concerné ; vide pour ZillaTerm lui-même.</param>
/// <param name="Image">Capture d'écran (PNG), pour <see cref="InterventionKind.Screenshot"/>.</param>
public sealed record InterventionEntry(DateTime Time, InterventionKind Kind, string Source, string Text, byte[]? Image = null);

/// <summary>Intervention relue : ses événements ; un bloc illisible, d'un autre journal ou déplacé la marque abîmée.</summary>
/// <param name="Unfinished">Pas d'événement de fin : ZillaTerm s'est arrêté pendant l'enregistrement.</param>
public sealed record InterventionRecording(string Path, IReadOnlyList<InterventionEntry> Entries, bool Damaged, bool Unfinished)
{
    public DateTime? Start => Entries.Count > 0 ? Entries[0].Time : null;

    public DateTime? End => Entries.Count > 0 ? Entries[^1].Time : null;
}

/// <summary>Intervention enregistrée (fichier), pour la liste des interventions précédentes.</summary>
public sealed record InterventionFile(string Path, DateTime Start, long Length);

/// <summary>
/// Journal d'une intervention : ce qui s'est passé pendant l'enregistrement (texte des terminaux, connexions, actions,
/// transferts, captures), pour s'en souvenir et rédiger le compte rendu.
/// </summary>
/// <remarks>
/// <para>
/// Le fichier est une suite de blocs chiffrés ajoutés à la fin (rien n'est réécrit) : un en-tête, puis pour chaque bloc
/// sa longueur (4 octets) et son contenu protégé par <see cref="ISecretProtector"/> (DPAPI, utilisateur Windows seul).
/// Un bloc contient des événements JSON, l'identifiant du journal et son numéro d'ordre : un bloc d'un autre journal,
/// déplacé ou retiré est signalé à la lecture. Les événements arrivent de plusieurs fils : ils sont gardés en mémoire et
/// écrits ensemble au plus tard toutes les <see cref="FlushInterval"/>.
/// </para>
/// <para>
/// Le texte des terminaux est plafonné (<see cref="MaxTerminalText"/>) : au-delà, une ligne le signale et le reste n'est
/// plus gardé (une commande qui affiche des gigaoctets ne remplit pas le disque).
/// </para>
/// </remarks>
public sealed class InterventionJournal : IDisposable
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    /// <summary>Texte des terminaux gardé au plus par intervention (caractères).</summary>
    public const long MaxTerminalText = 64L * 1024 * 1024;

    /// <summary>Plus grand bloc lu (une capture d'écran en 4K tient largement).</summary>
    private const int MaxChunk = 64 * 1024 * 1024;

    private static readonly byte[] Magic = "ZTINTV1\n"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISecretProtector _protector;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly object _lock = new();
    private readonly List<InterventionEntry> _pending = [];
    private readonly Timer _timer;
    private FileStream? _file;
    private long _sequence;
    private readonly long _terminalTextLimit;
    private long _terminalText;
    private bool _terminalCapped;

    private InterventionJournal(string path, FileStream file, ISecretProtector protector, long terminalTextLimit)
    {
        Path = path;
        _file = file;
        _protector = protector;
        _terminalTextLimit = terminalTextLimit;
        _timer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
    }

    public string Path { get; }

    /// <summary>Nombre d'événements écrits ou en attente.</summary>
    public int Count { get; private set; }

    /// <summary>Le disque a refusé une écriture : les événements suivants sont perdus (voir <see cref="Failure"/>).</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Dossier des interventions de l'utilisateur.</summary>
    public static string DefaultDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZillaTerm", "Interventions");

    /// <summary>Commence une intervention : nouveau fichier, nommé d'après l'heure de début.</summary>
    /// <param name="terminalTextLimit">Texte des terminaux gardé au plus (caractères).</param>
    public static InterventionJournal Start(string directory, ISecretProtector protector, DateTime now, long terminalTextLimit = MaxTerminalText)
    {
        Directory.CreateDirectory(directory);
        string name = now.ToUniversalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string path = System.IO.Path.Combine(directory, $"intervention-{name}.zij");
        for (int i = 2; File.Exists(path); i++)
        {
            path = System.IO.Path.Combine(directory, $"intervention-{name}-{i}.zij");
        }

        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        file.Write(Magic);
        file.Flush(flushToDisk: true);
        return new InterventionJournal(path, file, protector, terminalTextLimit);
    }

    /// <summary>Ajoute un événement (depuis n'importe quel fil).</summary>
    public void Write(InterventionEntry entry)
    {
        lock (_lock)
        {
            if (_file is null)
            {
                return;
            }

            if (entry.Kind == InterventionKind.Terminal)
            {
                if (_terminalCapped)
                {
                    return;
                }

                _terminalText += entry.Text.Length;
                if (_terminalText > _terminalTextLimit)
                {
                    _terminalCapped = true;
                    entry = entry with { Text = "…" };
                }
            }

            _pending.Add(entry);
            Count++;
        }
    }

    /// <summary>Le plafond du texte des terminaux est atteint : plus aucune ligne n'est gardée.</summary>
    public bool TerminalTextCapped
    {
        get
        {
            lock (_lock)
            {
                return _terminalCapped;
            }
        }
    }

    /// <summary>Écrit ce qui attend (un bloc chiffré), jusque sur le disque.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_file is null || _pending.Count == 0 || Failure is not null)
            {
                return;
            }

            var chunk = new Chunk(_id, _sequence, [.. _pending]);
            _pending.Clear();
            try
            {
                byte[] plain = JsonSerializer.SerializeToUtf8Bytes(chunk, JsonOptions);
                byte[] protectedBytes = _protector.Protect(plain);
                Array.Clear(plain);
                Span<byte> length = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(length, protectedBytes.Length);
                _file.Write(length);
                _file.Write(protectedBytes);
                _file.Flush(flushToDisk: true);
                _sequence++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                Failure = e;
            }
        }
    }

    /// <summary>Termine l'intervention : dernier événement, écriture, fermeture du fichier.</summary>
    public void Stop(InterventionEntry last)
    {
        Write(last);
        Dispose();
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
        lock (_lock)
        {
            _file?.Dispose();
            _file = null;
        }
    }

    /// <summary>Interventions du dossier, la plus récente en tête.</summary>
    public static IReadOnlyList<InterventionFile> List(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = new List<InterventionFile>();
        foreach (var info in new DirectoryInfo(directory).EnumerateFiles("intervention-*.zij"))
        {
            var stamp = info.Name["intervention-".Length..^".zij".Length];
            if (stamp.Length >= 15
                && DateTime.TryParseExact(stamp[..15], "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var start))
            {
                files.Add(new InterventionFile(info.FullName, start, info.Length));
            }
        }

        return [.. files.OrderByDescending(f => f.Start).ThenByDescending(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>Relit une intervention. Un fichier tronqué ou abîmé rend ce qui a pu être lu, marqué <c>Damaged</c>.</summary>
    /// <exception cref="InvalidDataException">Pas un journal d'intervention.</exception>
    public static InterventionRecording Read(string path, ISecretProtector protector)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var magic = new byte[Magic.Length];
        if (file.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) != magic.Length || !magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a ZillaTerm intervention journal.");
        }

        var entries = new List<InterventionEntry>();
        bool damaged = false;
        string? id = null;
        long expected = 0;
        var length = new byte[4];
        while (true)
        {
            int read = file.ReadAtLeast(length, 4, throwOnEndOfStream: false);
            if (read == 0)
            {
                break;
            }

            int size = read == 4 ? BinaryPrimitives.ReadInt32LittleEndian(length) : -1;
            if (size <= 0 || size > MaxChunk || size > file.Length - file.Position)
            {
                damaged = true;
                break;
            }

            var data = new byte[size];
            file.ReadExactly(data);
            Chunk? chunk;
            try
            {
                byte[] plain = protector.Unprotect(data);
                chunk = JsonSerializer.Deserialize<Chunk>(plain, JsonOptions);
                Array.Clear(plain);
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException)
            {
                damaged = true;
                continue;
            }

            if (chunk?.Entries is null || chunk.Id is null || (id is not null && chunk.Id != id))
            {
                // Bloc vide ou d'un autre journal : ignoré.
                damaged = true;
                continue;
            }

            if (chunk.Sequence != expected)
            {
                // Bloc retiré ou déplacé : ce qui reste est gardé.
                damaged = true;
            }

            id ??= chunk.Id;
            expected = chunk.Sequence + 1;
            entries.AddRange(chunk.Entries.Where(e => e is not null && e.Text is not null && e.Source is not null));
        }

        return new InterventionRecording(path, entries, damaged, entries.Count == 0 || entries[^1].Kind != InterventionKind.Stopped);
    }

    private sealed record Chunk(string Id, long Sequence, List<InterventionEntry> Entries);
}
