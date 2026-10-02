using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core.KeePass;

/// <summary>
/// Contenu déchiffré d'un coffre KeePass. Le XML est gardé tel quel (champs inconnus, pièces jointes, réglages
/// des autres clients) et seules les entrées touchées changent. Les valeurs protégées (mots de passe...) ne sont
/// jamais en clair dans le XML en mémoire : elles restent masquées à part et ne sont révélées qu'à la demande.
/// </summary>
public sealed class KeePassDatabase : IDisposable
{
    private static readonly string[] StandardFields = ["Title", "UserName", "Password", "URL", "Notes"];
    private static readonly DateTime KeePassEpoch = new(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string EmptyUuid = "AAAAAAAAAAAAAAAAAAAAAA==";

    private XDocument _xml = new();

    internal uint Version { get; init; }

    internal Guid CipherId { get; set; }

    internal bool Compressed { get; set; }

    internal byte[] TransformSeed { get; set; } = [];

    internal ulong TransformRounds { get; set; }

    internal uint InnerStreamId { get; set; }

    internal byte[] KdfParameters { get; set; } = [];

    internal byte[]? PublicCustomData { get; set; }

    internal List<(byte Id, byte[] Data)> OtherHeaderFields { get; } = [];

    /// <summary>Pièces jointes (KDBX 4, en-tête interne), dans leur ordre d'origine.</summary>
    internal List<(byte Flags, SecretBytes Data)> Binaries { get; } = [];

    internal SecretBytes TransformedKey { get; set; } = new([]);

    /// <summary>Empreinte de l'en-tête enregistrée dans le XML (KDBX 3.1).</summary>
    internal byte[]? HeaderHash { get; private set; }

    /// <summary>« KDBX 4.0 », « KDBX 3.1 »...</summary>
    public string FormatName => $"KDBX {Version >> 16}.{Version & 0xFFFF}";

    public string Name => Meta?.Element("DatabaseName")?.Value ?? "";

    /// <summary>Dossiers du coffre (« Serveurs/Prod »), hors corbeille ; « » est la racine.</summary>
    public IReadOnlyList<string> Groups =>
        RootGroup is { } root ? [.. AllGroups(root).Where(g => !g.InRecycleBin).Select(g => g.Path)] : [];

    /// <summary>Entrées du coffre, hors corbeille et historique.</summary>
    public IReadOnlyList<KeePassEntry> Entries
    {
        get
        {
            if (RootGroup is not { } root)
            {
                return [];
            }

            return [.. AllGroups(root).Where(g => !g.InRecycleBin)
                .SelectMany(g => g.Element.Elements("Entry").Select(e => ToEntry(e, g.Path)))];
        }
    }

    private bool IsV4 => Version >= 0x00040000;

    private XElement? Meta => _xml.Root?.Element("Meta");

    private XElement? RootGroup => _xml.Root?.Element("Root")?.Element("Group");

    /// <summary>Mot de passe de l'entrée, en clair : à n'appeler qu'au moment de s'en servir.</summary>
    public string? RevealPassword(string entryId) => FindEntry(entryId) is { } entry ? GetField(entry, "Password") : null;

    /// <summary>Valeur d'un champ (« Title », « UserName », champ personnalisé...) ; les champs protégés sont révélés.</summary>
    public string? GetField(string entryId, string field) => FindEntry(entryId) is { } entry ? GetField(entry, field) : null;

    /// <summary>Ajoute une entrée dans le dossier <paramref name="groupPath"/> (créé si besoin) ; renvoie son identifiant.</summary>
    public string AddEntry(KeePassEntryData data, string groupPath = "")
    {
        var group = EnsureGroup(groupPath);
        var now = DateTime.UtcNow;
        var id = NewUuid();
        var entry = new XElement("Entry",
            new XElement("UUID", id),
            new XElement("IconID", "0"),
            new XElement("ForegroundColor"),
            new XElement("BackgroundColor"),
            new XElement("OverrideURL"),
            new XElement("Tags"),
            NewTimes(now));
        foreach (var field in StandardFields)
        {
            entry.Add(NewString(field, ""));
        }

        entry.Add(new XElement("AutoType",
            new XElement("Enabled", "True"),
            new XElement("DataTransferObfuscation", "0")));
        entry.Add(new XElement("History"));
        Apply(entry, data with { Password = data.Password ?? "" });
        // Les entrées avant les sous-dossiers, comme KeePass.
        if (group.Elements("Group").FirstOrDefault() is { } firstGroup)
        {
            firstGroup.AddBeforeSelf(entry);
        }
        else
        {
            group.Add(entry);
        }

        return id;
    }

    /// <summary>
    /// Modifie une entrée ; l'ancienne version va dans son historique. <paramref name="expectedModified"/> est la date
    /// de modification lue avant l'édition : si l'entrée a changé depuis, rien n'est fait (<see cref="KeePassError.Conflict"/>).
    /// </summary>
    public void UpdateEntry(string entryId, KeePassEntryData data, DateTime expectedModified)
    {
        var entry = FindCurrent(entryId, expectedModified, data.Title);
        AddToHistory(entry);
        Apply(entry, data);
        var now = DateTime.UtcNow;
        SetTime(entry, "LastModificationTime", now);
        SetTime(entry, "LastAccessTime", now);
    }

    /// <summary>Supprime une entrée : dans la corbeille si elle est activée, définitivement sinon (ou si elle y est déjà).</summary>
    public void DeleteEntry(string entryId, DateTime expectedModified)
    {
        var entry = FindCurrent(entryId, expectedModified, "");
        var now = DateTime.UtcNow;
        var bin = RecycleBinEnabled ? EnsureRecycleBin(now) : null;
        if (bin is not null && !entry.Ancestors("Group").Contains(bin))
        {
            if (Version >= 0x00040001 && entry.Parent?.Element("UUID")?.Value is { } previous)
            {
                SetChild(entry, "PreviousParentGroup", previous, after: "Tags");
            }

            entry.Remove();
            bin.Add(entry);
            SetTime(entry, "LocationChanged", now);
            return;
        }

        entry.Remove();
        var root = _xml.Root!.Element("Root")!;
        var deleted = root.Element("DeletedObjects");
        if (deleted is null)
        {
            deleted = new XElement("DeletedObjects");
            root.Add(deleted);
        }

        deleted.Add(new XElement("DeletedObject", new XElement("UUID", entryId), new XElement("DeletionTime", FormatTime(now))));
    }

    public void Dispose()
    {
        foreach (var value in _xml.Descendants().SelectMany(e => e.Annotations<ProtectedValue>()))
        {
            value.Dispose();
        }

        foreach (var (_, data) in Binaries)
        {
            data.Dispose();
        }

        Binaries.Clear();
        TransformedKey.Dispose();
        _xml = new XDocument();
    }

    internal void LoadXml(ReadOnlyMemory<byte> xml, StreamCipher protection)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 0 };
        using (var reader = XmlReader.Create(new MemoryStream(xml.ToArray(), writable: false), settings))
        {
            _xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        if (_xml.Root?.Name.LocalName != "KeePassFile")
        {
            throw new FormatException("KeePassFile");
        }

        // Les valeurs protégées sont masquées par le flot interne dans l'ordre du document, historique compris.
        foreach (var element in _xml.Descendants().Where(IsProtected).ToList())
        {
            var bytes = element.Value.Length == 0 ? [] : Convert.FromBase64String(element.Value);
            protection.Xor(bytes);
            element.AddAnnotation(new ProtectedValue(bytes));
            CryptographicOperations.ZeroMemory(bytes);
            element.Value = "";
        }

        HeaderHash = Meta?.Element("HeaderHash")?.Value is { Length: > 0 } hash ? Convert.FromBase64String(hash) : null;
    }

    internal byte[] SerializeXml(StreamCipher protection)
    {
        var protectedElements = _xml.Descendants().Where(IsProtected).ToList();
        try
        {
            foreach (var element in protectedElements)
            {
                var bytes = element.Annotation<ProtectedValue>()?.Reveal() ?? Encoding.UTF8.GetBytes(element.Value);
                protection.Xor(bytes);
                element.Value = Convert.ToBase64String(bytes);
                CryptographicOperations.ZeroMemory(bytes);
            }

            var output = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None,
            };
            using (var writer = XmlWriter.Create(output, settings))
            {
                _xml.Save(writer);
            }

            return output.ToArray();
        }
        finally
        {
            foreach (var element in protectedElements)
            {
                element.Value = "";
            }
        }
    }

    internal void SetHeaderHash(byte[] hash)
    {
        HeaderHash = hash;
        if (Meta is { } meta)
        {
            SetChild(meta, "HeaderHash", Convert.ToBase64String(hash), after: "Generator");
        }
    }

    private static bool IsProtected(XElement element) =>
        string.Equals((string?)element.Attribute("Protected"), "True", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dossiers du coffre et leur chemin, en signalant la corbeille et son contenu.</summary>
    private IEnumerable<(XElement Element, string Path, bool InRecycleBin)> AllGroups(XElement root)
    {
        var binUuid = RecycleBinUuid;
        return Walk(root, "", false);

        IEnumerable<(XElement, string, bool)> Walk(XElement group, string path, bool inBin)
        {
            yield return (group, path, inBin);
            foreach (var child in group.Elements("Group"))
            {
                var name = child.Element("Name")?.Value ?? "";
                var childPath = path.Length == 0 ? name : $"{path}/{name}";
                var isBin = inBin || (binUuid.Length > 0 && child.Element("UUID")?.Value == binUuid);
                foreach (var item in Walk(child, childPath, isBin))
                {
                    yield return item;
                }
            }
        }
    }

    private string RecycleBinUuid => Meta?.Element("RecycleBinUUID")?.Value is { } id && id != EmptyUuid ? id : "";

    private bool RecycleBinEnabled =>
        !string.Equals(Meta?.Element("RecycleBinEnabled")?.Value, "False", StringComparison.OrdinalIgnoreCase);

    private KeePassEntry ToEntry(XElement entry, string groupPath)
    {
        var custom = entry.Elements("String")
            .Where(s => !StandardFields.Contains(s.Element("Key")?.Value) && s.Element("Value") is { } v && !IsProtected(v))
            .ToDictionary(s => s.Element("Key")!.Value, s => s.Element("Value")!.Value, StringComparer.Ordinal);
        return new KeePassEntry
        {
            Id = entry.Element("UUID")?.Value ?? "",
            Title = GetField(entry, "Title") ?? "",
            UserName = GetField(entry, "UserName") ?? "",
            Url = GetField(entry, "URL") ?? "",
            Notes = GetField(entry, "Notes") ?? "",
            Tags = entry.Element("Tags")?.Value ?? "",
            Group = groupPath,
            HasPassword = FieldElement(entry, "Password") is { } p && (p.Annotation<ProtectedValue>()?.Length > 0 || p.Value.Length > 0),
            Modified = ParseTime(entry.Element("Times")?.Element("LastModificationTime")?.Value),
            CustomFields = custom,
        };
    }

    private static XElement? FieldElement(XElement entry, string field) =>
        entry.Elements("String").FirstOrDefault(s => s.Element("Key")?.Value == field)?.Element("Value");

    private static string? GetField(XElement entry, string field)
    {
        if (FieldElement(entry, field) is not { } value)
        {
            return null;
        }

        if (value.Annotation<ProtectedValue>() is { } secret)
        {
            var bytes = secret.Reveal();
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        return value.Value;
    }

    private XElement? FindEntry(string entryId) =>
        RootGroup?.Descendants("Entry").FirstOrDefault(e => e.Element("UUID")?.Value == entryId && e.Parent?.Name != "History");

    /// <summary>L'entrée, si elle n'a pas changé depuis <paramref name="expectedModified"/> ; sinon conflit.</summary>
    private XElement FindCurrent(string entryId, DateTime expectedModified, string title)
    {
        var entry = FindEntry(entryId);
        if (entry is null || ParseTime(entry.Element("Times")?.Element("LastModificationTime")?.Value) != expectedModified)
        {
            var name = entry is not null ? GetField(entry, "Title") ?? title : title;
            throw new KeePassException(KeePassError.Conflict, string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassConflict, name));
        }

        return entry;
    }

    /// <summary>Nombre de versions précédentes gardées pour l'entrée.</summary>
    internal int HistoryCount(string entryId) => FindEntry(entryId)?.Element("History")?.Elements("Entry").Count() ?? 0;

    /// <summary>Mot de passe d'une version précédente (0 = la plus ancienne).</summary>
    internal string? HistoryPassword(string entryId, int index) =>
        FindEntry(entryId)?.Element("History")?.Elements("Entry").ElementAtOrDefault(index) is { } old ? GetField(old, "Password") : null;

    /// <summary>Entrées placées dans la corbeille.</summary>
    internal IReadOnlyList<string> RecycledTitles =>
        RootGroup is { } root
            ? [.. AllGroups(root).Where(g => g.InRecycleBin).SelectMany(g => g.Element.Elements("Entry")).Select(e => GetField(e, "Title") ?? "")]
            : [];

    private void Apply(XElement entry, KeePassEntryData data)
    {
        SetField(entry, "Title", data.Title);
        SetField(entry, "UserName", data.UserName);
        SetField(entry, "URL", data.Url);
        SetField(entry, "Notes", data.Notes);
        if (data.Password is not null)
        {
            SetField(entry, "Password", data.Password);
        }
    }

    private void SetField(XElement entry, string field, string text)
    {
        text = XmlSafe(text);
        var value = FieldElement(entry, field);
        if (value is null)
        {
            var element = NewString(field, "");
            if (entry.Elements("String").LastOrDefault() is { } last)
            {
                last.AddAfterSelf(element);
            }
            else
            {
                entry.Add(element);
            }

            value = element.Element("Value")!;
        }

        if (IsProtected(value))
        {
            foreach (var old in value.Annotations<ProtectedValue>().ToList())
            {
                old.Dispose();
                value.RemoveAnnotations<ProtectedValue>();
            }

            var bytes = Encoding.UTF8.GetBytes(text);
            value.AddAnnotation(new ProtectedValue(bytes));
            CryptographicOperations.ZeroMemory(bytes);
            value.Value = "";
        }
        else
        {
            value.Value = text;
        }
    }

    /// <summary>Nouveau champ ; protégé selon les réglages du coffre (le mot de passe l'est toujours).</summary>
    private XElement NewString(string field, string text)
    {
        var protect = field == "Password" || string.Equals(
            Meta?.Element("MemoryProtection")?.Element(field switch
            {
                "Title" => "ProtectTitle",
                "UserName" => "ProtectUserName",
                "URL" => "ProtectURL",
                "Notes" => "ProtectNotes",
                _ => "-",
            })?.Value, "True", StringComparison.OrdinalIgnoreCase);
        var value = new XElement("Value", text);
        if (protect)
        {
            value.SetAttributeValue("Protected", "True");
            value.AddAnnotation(new ProtectedValue(Encoding.UTF8.GetBytes(text)));
            value.Value = "";
        }

        return new XElement("String", new XElement("Key", field), value);
    }

    /// <summary>Copie de l'entrée (sans son historique) ajoutée à l'historique, limité comme le coffre le demande.</summary>
    private void AddToHistory(XElement entry)
    {
        var copy = CloneWithSecrets(entry);
        copy.Element("History")?.Remove();
        var history = entry.Element("History");
        if (history is null)
        {
            history = new XElement("History");
            entry.Add(history);
        }

        history.Add(copy);
        var max = int.TryParse(Meta?.Element("HistoryMaxItems")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 10;
        if (max >= 0)
        {
            foreach (var old in history.Elements("Entry").SkipLast(max).ToList())
            {
                DisposeSecrets(old);
                old.Remove();
            }
        }
    }

    private static XElement CloneWithSecrets(XElement source)
    {
        var copy = new XElement(source);
        // Les annotations (valeurs protégées) ne sont pas copiées par XElement : on les recopie élément par élément.
        foreach (var (from, to) in source.DescendantsAndSelf().Zip(copy.DescendantsAndSelf()))
        {
            if (from.Annotation<ProtectedValue>() is { } secret)
            {
                to.AddAnnotation(secret.Clone());
            }
        }

        return copy;
    }

    private static void DisposeSecrets(XElement element)
    {
        foreach (var secret in element.DescendantsAndSelf().SelectMany(e => e.Annotations<ProtectedValue>()))
        {
            secret.Dispose();
        }
    }

    private XElement EnsureGroup(string path)
    {
        var group = RootGroup ?? throw new KeePassException(KeePassError.Corrupted,
            string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassCorrupted, "Root/Group"));
        foreach (var name in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var child = group.Elements("Group").FirstOrDefault(g => g.Element("Name")?.Value == name);
            if (child is null)
            {
                child = NewGroup(name, DateTime.UtcNow);
                group.Add(child);
            }

            group = child;
        }

        return group;
    }

    private XElement EnsureRecycleBin(DateTime now)
    {
        var id = RecycleBinUuid;
        if (id.Length > 0 && RootGroup?.Descendants("Group").FirstOrDefault(g => g.Element("UUID")?.Value == id) is { } existing)
        {
            return existing;
        }

        var bin = NewGroup("Recycle Bin", now, iconId: "43", searchable: false);
        RootGroup!.Add(bin);
        var meta = Meta!;
        SetChild(meta, "RecycleBinUUID", bin.Element("UUID")!.Value, after: "RecycleBinEnabled");
        SetChild(meta, "RecycleBinChanged", FormatTime(now), after: "RecycleBinUUID");
        return bin;
    }

    private XElement NewGroup(string name, DateTime now, string iconId = "48", bool searchable = true) =>
        new("Group",
            new XElement("UUID", NewUuid()),
            new XElement("Name", XmlSafe(name)),
            new XElement("Notes"),
            new XElement("IconID", iconId),
            NewTimes(now),
            new XElement("IsExpanded", "True"),
            new XElement("DefaultAutoTypeSequence"),
            new XElement("EnableAutoType", searchable ? "null" : "false"),
            new XElement("EnableSearching", searchable ? "null" : "false"),
            new XElement("LastTopVisibleEntry", EmptyUuid));

    private XElement NewTimes(DateTime now) =>
        new("Times",
            new XElement("CreationTime", FormatTime(now)),
            new XElement("LastModificationTime", FormatTime(now)),
            new XElement("LastAccessTime", FormatTime(now)),
            new XElement("ExpiryTime", FormatTime(now)),
            new XElement("Expires", "False"),
            new XElement("UsageCount", "0"),
            new XElement("LocationChanged", FormatTime(now)));

    private void SetTime(XElement entry, string name, DateTime time)
    {
        var times = entry.Element("Times");
        if (times is null)
        {
            times = NewTimes(time);
            entry.Add(times);
        }

        SetChild(times, name, FormatTime(time), after: null);
    }

    /// <summary>Remplace la valeur d'un enfant, ou le crée après <paramref name="after"/> (sinon à la fin).</summary>
    private static void SetChild(XElement parent, string name, string value, string? after)
    {
        if (parent.Element(name) is { } existing)
        {
            existing.Value = value;
        }
        else if (after is not null && parent.Element(after) is { } previous)
        {
            previous.AddAfterSelf(new XElement(name, value));
        }
        else
        {
            parent.Add(new XElement(name, value));
        }
    }

    /// <summary>KDBX 4 : secondes depuis l'an 1 en base64 ; KDBX 3.1 : date ISO.</summary>
    private string FormatTime(DateTime time)
    {
        time = new DateTime(time.Ticks - (time.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        if (!IsV4)
        {
            return time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, (long)(time - KeePassEpoch).TotalSeconds);
        return Convert.ToBase64String(bytes);
    }

    internal static DateTime ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateTime.MinValue;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var iso))
        {
            return iso;
        }

        try
        {
            var bytes = Convert.FromBase64String(text);
            return bytes.Length == 8 ? KeePassEpoch.AddSeconds(BinaryPrimitives.ReadInt64LittleEndian(bytes)) : DateTime.MinValue;
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }

    private static string NewUuid() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>Retire les caractères interdits en XML (caractères de contrôle saisis par erreur).</summary>
    private static string XmlSafe(string text) =>
        text.All(XmlConvert.IsXmlChar) && !text.Any(char.IsSurrogate) ? text : RemoveInvalid(text);

    private static string RemoveInvalid(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text, i, 2);
                i++;
            }
            else if (XmlConvert.IsXmlChar(text[i]))
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    /// <summary>Valeur protégée d'un élément du XML, masquée en mémoire.</summary>
    private sealed class ProtectedValue(ReadOnlySpan<byte> value) : IDisposable
    {
        private readonly SecretBytes _secret = new(value);

        public int Length => _secret.Length;

        public byte[] Reveal() => _secret.Reveal();

        public ProtectedValue Clone()
        {
            var bytes = _secret.Reveal();
            try
            {
                return new ProtectedValue(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        public void Dispose() => _secret.Dispose();
    }
}

/// <summary>Entrée d'un coffre KeePass telle qu'affichée (sans son mot de passe).</summary>
public sealed class KeePassEntry
{
    /// <summary>UUID KeePass en base64, tel qu'il figure dans le fichier.</summary>
    public required string Id { get; init; }

    public string Title { get; init; } = "";

    public string UserName { get; init; } = "";

    public string Url { get; init; } = "";

    public string Notes { get; init; } = "";

    /// <summary>Étiquettes, séparées par « ; » ou « , ».</summary>
    public string Tags { get; init; } = "";

    /// <summary>Dossier dans le coffre (« Serveurs/Prod ») ; « » pour la racine.</summary>
    public string Group { get; init; } = "";

    public bool HasPassword { get; init; }

    /// <summary>Date de dernière modification (UTC), qui sert aussi à détecter une modification concurrente.</summary>
    public DateTime Modified { get; init; }

    /// <summary>Champs personnalisés non protégés (« Port », « Protocol »...).</summary>
    public IReadOnlyDictionary<string, string> CustomFields { get; init; } = new Dictionary<string, string>();
}

/// <summary>Valeurs saisies pour créer ou modifier une entrée. <see cref="Password"/> null : inchangé.</summary>
public sealed record KeePassEntryData(string Title, string UserName, string? Password, string Url, string Notes);
