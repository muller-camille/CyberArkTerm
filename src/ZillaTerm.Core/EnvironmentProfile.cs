using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core;

/// <summary>Réglage qu'un fichier d'environnement change.</summary>
public enum EnvironmentSetting
{
    PvwaUrl,
    AuthMethod,
    DefaultPsmp,
    PsmpServers,
    WindowsComponent,
    PlatformComponent,
    SharedList,
    HostKey,
    CentralFile,
    KeepPvwaSessionAlive,
    SshInApp,
    CheckForUpdates,
    UploadProtocol,
}

/// <summary>
/// Changement qu'appliquerait un fichier d'environnement. <see cref="Sensitive"/> : adresse qui recevra le mot de passe
/// CyberArk ou clé de serveur, à vérifier ; <see cref="Ignored"/> : non appliqué (clé différente de celle déjà acceptée).
/// </summary>
public sealed record EnvironmentChange(EnvironmentSetting Setting, string? Detail, string Current, string New, bool Sensitive,
    bool Ignored = false);

/// <summary>Raison du refus d'un fichier d'environnement.</summary>
public enum EnvironmentProblem
{
    TooLarge,
    InvalidJson,
    UnsupportedFormat,
    InvalidPvwa,
    InvalidPsmpAddress,
    InvalidPort,
    InvalidDomain,
    DuplicateDomain,
    InvalidComponent,
    InvalidHostKey,
    InvalidPath,
}

/// <summary>Fichier d'environnement refusé ; <see cref="Detail"/> nomme la valeur en cause.</summary>
public sealed class EnvironmentFileException(EnvironmentProblem problem, string detail)
    : Exception($"{problem}: {detail}")
{
    public EnvironmentProblem Problem { get; } = problem;

    public string Detail { get; } = detail;
}

/// <summary>Fichier d'environnement lu : son contenu et l'empreinte SHA-256 de ses octets.</summary>
public sealed record EnvironmentFile(EnvironmentProfile Profile, string Sha256);

/// <summary>
/// Environnement partagé (<c>ZillaTerm.env.json</c>) : réglages communs à une équipe pour un coffre CyberArk (PVWA,
/// PSMP, composants PSM, listes partagées, clés des PSMP, quelques options). Jamais de secret ni de donnée personnelle
/// (identifiant, « Mes serveurs », sessions récentes, coffre local) : un fichier qui en contiendrait n'en apporte pas,
/// seuls les champs ci-dessous sont lus. Un champ absent ne change rien.
/// </summary>
public sealed class EnvironmentProfile
{
    /// <summary>Nom du fichier lu à côté de l'exécutable.</summary>
    public const string FileName = "ZillaTerm.env.json";

    /// <summary>Nom du fichier sous l'ancien nom de l'application, encore lu à côté de l'exécutable.</summary>
    public const string LegacyFileName = AppSettings.LegacyName + ".env.json";

    public const int CurrentFormat = 1;

    private const long MaxFileSize = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ExactEnumConverter() },
    };

    public int Format { get; set; } = CurrentFormat;

    /// <summary>Nom affiché (« Production »…).</summary>
    public string? Name { get; set; }

    public string? PvwaUrl { get; set; }

    public AuthMethod? AuthMethod { get; set; }

    public string? PsmpAddress { get; set; }

    public int? PsmpPort { get; set; }

    public List<PsmpServer>? PsmpServers { get; set; }

    public string? WindowsComponent { get; set; }

    public Dictionary<string, string>? ComponentByPlatform { get; set; }

    /// <summary>Listes partagées à ouvrir (ajoutées, jamais retirées).</summary>
    public List<string>? SharedLists { get; set; }

    /// <summary>Clés des PSMP (« hôte:port » → « type SHA256:empreinte ») : ajoutées, jamais à la place d'une clé déjà acceptée.</summary>
    public Dictionary<string, string>? HostKeys { get; set; }

    /// <summary>Fichier d'environnement central (partage réseau), relu à chaque démarrage.</summary>
    public string? CentralFile { get; set; }

    public bool? KeepPvwaSessionAlive { get; set; }

    public bool? SshInApp { get; set; }

    public bool? CheckForUpdates { get; set; }

    public TransferProtocol? UploadProtocol { get; set; }

    /// <summary>Environnement des réglages actuels, sans rien de personnel ; clés des PSMP configurés déjà acceptées.</summary>
    public static EnvironmentProfile FromSettings(AppSettings settings, string? name)
    {
        var psmps = settings.PsmpServers.Select(p => (Address: p.Address, Port: p.Port))
            .Prepend((Address: settings.PsmpAddress, Port: settings.PsmpPort))
            .Where(p => !string.IsNullOrWhiteSpace(p.Address));
        var hostKeys = psmps.Select(p => KnownHosts.Key(p.Address, p.Port))
            .Distinct()
            .Where(settings.KnownHosts.ContainsKey)
            .ToDictionary(k => k, k => settings.KnownHosts[k]);
        return new EnvironmentProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            PvwaUrl = NullIfEmpty(settings.PvwaUrl),
            AuthMethod = settings.AuthMethod,
            PsmpAddress = NullIfEmpty(settings.PsmpAddress),
            PsmpPort = string.IsNullOrWhiteSpace(settings.PsmpAddress) ? null : settings.PsmpPort,
            PsmpServers = settings.PsmpServers.Count == 0
                ? null
                : settings.PsmpServers.Select(p => new PsmpServer { Address = p.Address, Port = p.Port, Domain = p.Domain }).ToList(),
            WindowsComponent = NullIfEmpty(settings.WindowsComponent),
            ComponentByPlatform = settings.ComponentByPlatform.Count == 0 ? null : new Dictionary<string, string>(settings.ComponentByPlatform),
            SharedLists = settings.SharedLists.Count == 0 ? null : [.. settings.SharedLists],
            HostKeys = hostKeys.Count == 0 ? null : hostKeys,
            CentralFile = NullIfEmpty(settings.EnvironmentFile),
            KeepPvwaSessionAlive = settings.KeepPvwaSessionAlive,
            SshInApp = settings.SshInApp,
            CheckForUpdates = settings.CheckForUpdates,
            UploadProtocol = settings.PreferredUploadProtocol,
        };
    }

    /// <summary>Lit et vérifie un fichier ; <see cref="EnvironmentFileException"/> s'il est refusé.</summary>
    /// <exception cref="IOException">Fichier absent ou illisible (partage indisponible…).</exception>
    public static EnvironmentFile Load(string path)
    {
        if (new FileInfo(path).Length > MaxFileSize)
        {
            throw new EnvironmentFileException(EnvironmentProblem.TooLarge, path);
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.LongLength > MaxFileSize)
        {
            throw new EnvironmentFileException(EnvironmentProblem.TooLarge, path);
        }

        EnvironmentProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<EnvironmentProfile>(bytes, JsonOptions)
                      ?? throw new EnvironmentFileException(EnvironmentProblem.InvalidJson, path);
        }
        catch (JsonException ex)
        {
            throw new EnvironmentFileException(EnvironmentProblem.InvalidJson, ex.Message);
        }

        profile.Validate();
        return new EnvironmentFile(profile, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Refuse un fichier dont une valeur serait inutilisable ou dangereuse (adresse en http, nom invalide…).</summary>
    public void Validate()
    {
        if (Format is < 1 or > CurrentFormat)
        {
            Fail(EnvironmentProblem.UnsupportedFormat, Format.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Envoi par le PSMP : SCP ou SFTP seulement (FTP est réservé aux entrées KeePass, après confirmation).
        if ((AuthMethod is { } method && !Enum.IsDefined(method)) || UploadProtocol is not (null or TransferProtocol.Scp or TransferProtocol.Sftp))
        {
            Fail(EnvironmentProblem.InvalidJson, $"{AuthMethod} {UploadProtocol}".Trim());
        }

        if (PvwaUrl is not null)
        {
            try
            {
                PvwaClient.NormalizeBaseUri(PvwaUrl);
            }
            catch (ArgumentException)
            {
                Fail(EnvironmentProblem.InvalidPvwa, PvwaUrl);
            }
        }

        if (!string.IsNullOrWhiteSpace(PsmpAddress) && Uri.CheckHostName(PsmpAddress.Trim()) == UriHostNameType.Unknown)
        {
            Fail(EnvironmentProblem.InvalidPsmpAddress, PsmpAddress);
        }

        CheckPort(PsmpPort);
        var domains = new PsmpDomainCheck(PsmpAddress);
        foreach (var psmp in PsmpServers ?? [])
        {
            if (psmp is null || Uri.CheckHostName((psmp.Address ?? "").Trim()) == UriHostNameType.Unknown)
            {
                Fail(EnvironmentProblem.InvalidPsmpAddress, psmp?.Address ?? "");
            }

            CheckPort(psmp!.Port);
            switch (domains.Check(psmp.Address, psmp.Domain, out var domain))
            {
                case PsmpDomainProblem.Missing or PsmpDomainProblem.Invalid:
                    Fail(EnvironmentProblem.InvalidDomain, psmp.Address ?? "");
                    break;
                case PsmpDomainProblem.Duplicate:
                    Fail(EnvironmentProblem.DuplicateDomain, domain);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(WindowsComponent) && !AppSettings.IsValidComponentName(WindowsComponent))
        {
            Fail(EnvironmentProblem.InvalidComponent, WindowsComponent);
        }

        foreach (var (platform, component) in ComponentByPlatform ?? [])
        {
            if (string.IsNullOrWhiteSpace(platform) || !AppSettings.IsValidComponentName(component))
            {
                Fail(EnvironmentProblem.InvalidComponent, $"{platform} : {component}");
            }
        }

        foreach (var (host, key) in HostKeys ?? [])
        {
            if (!IsHostKeyEntry(host, key))
            {
                Fail(EnvironmentProblem.InvalidHostKey, host);
            }
        }

        foreach (var path in CentralFile is null ? SharedLists ?? [] : (SharedLists ?? []).Append(CentralFile))
        {
            if (!IsFullPath(path))
            {
                Fail(EnvironmentProblem.InvalidPath, path ?? "");
            }
        }
    }

    /// <summary>Changements par rapport aux réglages actuels (rien de ce qui est déjà identique).</summary>
    public IReadOnlyList<EnvironmentChange> Diff(AppSettings settings)
    {
        var changes = new List<EnvironmentChange>();
        void Add(EnvironmentSetting setting, string? detail, string current, string next, bool sensitive)
        {
            if (!string.Equals(current, next, StringComparison.Ordinal))
            {
                changes.Add(new EnvironmentChange(setting, detail, current, next, sensitive));
            }
        }

        if (PvwaUrl is not null && !SamePvwa(settings.PvwaUrl, PvwaUrl))
        {
            changes.Add(new EnvironmentChange(EnvironmentSetting.PvwaUrl, null, settings.PvwaUrl, PvwaUrl.Trim(), true));
        }

        if (AuthMethod is { } method)
        {
            Add(EnvironmentSetting.AuthMethod, null, settings.AuthMethod.ToString(), method.ToString(), false);
        }

        if (PsmpAddress is not null || PsmpPort is not null)
        {
            Add(EnvironmentSetting.DefaultPsmp, null, Endpoint(settings.PsmpAddress, settings.PsmpPort),
                Endpoint(PsmpAddress ?? settings.PsmpAddress, PsmpPort ?? settings.PsmpPort), true);
        }

        if (PsmpServers is not null)
        {
            Add(EnvironmentSetting.PsmpServers, null, Describe(settings.PsmpServers), Describe(PsmpServers), true);
        }

        if (WindowsComponent is not null)
        {
            Add(EnvironmentSetting.WindowsComponent, null, settings.WindowsComponent.Trim(), WindowsComponent.Trim(), false);
        }

        foreach (var (platform, component) in ComponentByPlatform ?? [])
        {
            var current = settings.ComponentByPlatform
                .FirstOrDefault(kv => string.Equals(kv.Key, platform, StringComparison.OrdinalIgnoreCase)).Value ?? "";
            Add(EnvironmentSetting.PlatformComponent, platform.Trim(), current, component.Trim(), false);
        }

        foreach (var path in (SharedLists ?? []).Where(p => !settings.SharedLists.Contains(p.Trim(), StringComparer.OrdinalIgnoreCase)))
        {
            changes.Add(new EnvironmentChange(EnvironmentSetting.SharedList, null, "", path.Trim(), false));
        }

        foreach (var (host, key) in HostKeys ?? [])
        {
            var id = host.Trim().ToLowerInvariant();
            var known = KnownHosts.KnownKeys(settings.KnownHosts, id).Select(k => KnownHosts.Format(k.Algorithm, k.Sha256)).ToList();
            if (known.Count == 0)
            {
                changes.Add(new EnvironmentChange(EnvironmentSetting.HostKey, id, "", key.Trim(), true));
            }
            else if (!known.Contains(key.Trim(), StringComparer.Ordinal))
            {
                // Un serveur dont une clé est déjà acceptée n'en reçoit jamais d'un fichier (ni à la place, ni d'un autre
                // type) : le changement se vérifie à la connexion.
                changes.Add(new EnvironmentChange(EnvironmentSetting.HostKey, id, string.Join(", ", known), key.Trim(), true, Ignored: true));
            }
        }

        if (CentralFile is not null)
        {
            Add(EnvironmentSetting.CentralFile, null, settings.EnvironmentFile.Trim(), CentralFile.Trim(), true);
        }

        AddFlag(EnvironmentSetting.KeepPvwaSessionAlive, settings.KeepPvwaSessionAlive, KeepPvwaSessionAlive);
        AddFlag(EnvironmentSetting.SshInApp, settings.SshInApp, SshInApp);
        AddFlag(EnvironmentSetting.CheckForUpdates, settings.CheckForUpdates, CheckForUpdates);
        if (UploadProtocol is { } protocol)
        {
            Add(EnvironmentSetting.UploadProtocol, null, settings.PreferredUploadProtocol.ToString().ToUpperInvariant(),
                protocol.ToString().ToUpperInvariant(), false);
        }

        return changes;

        void AddFlag(EnvironmentSetting setting, bool current, bool? next)
        {
            if (next is { } value)
            {
                Add(setting, null, current ? "true" : "false", value ? "true" : "false", false);
            }
        }
    }

    /// <summary>Applique l'environnement (validé) ; une clé de serveur déjà acceptée et différente reste inchangée.</summary>
    public void ApplyTo(AppSettings settings)
    {
        if (PvwaUrl is not null)
        {
            settings.PvwaUrl = PvwaUrl.Trim();
        }

        if (AuthMethod is { } method)
        {
            settings.AuthMethod = method;
        }

        if (PsmpAddress is not null)
        {
            settings.PsmpAddress = PsmpAddress.Trim();
        }

        if (PsmpPort is { } port)
        {
            settings.PsmpPort = port;
        }

        if (PsmpServers is not null)
        {
            settings.PsmpServers = PsmpServers
                .Select(p => new PsmpServer { Address = p.Address.Trim(), Port = p.Port, Domain = (p.Domain ?? "").Trim() })
                .ToList();
        }

        if (WindowsComponent is not null)
        {
            settings.WindowsComponent = WindowsComponent.Trim();
        }

        foreach (var (platform, component) in ComponentByPlatform ?? [])
        {
            settings.RememberComponent(platform.Trim(), component.Trim());
        }

        foreach (var path in (SharedLists ?? []).Select(p => p.Trim()))
        {
            if (!settings.SharedLists.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                settings.SharedLists.Add(path);
            }
        }

        foreach (var (host, key) in HostKeys ?? [])
        {
            var id = host.Trim().ToLowerInvariant();
            if (KnownHosts.KnownKeys(settings.KnownHosts, id).Count == 0)
            {
                settings.KnownHosts[id] = key.Trim();
            }
        }

        if (CentralFile is not null)
        {
            settings.EnvironmentFile = CentralFile.Trim();
        }

        settings.KeepPvwaSessionAlive = KeepPvwaSessionAlive ?? settings.KeepPvwaSessionAlive;
        settings.SshInApp = SshInApp ?? settings.SshInApp;
        settings.CheckForUpdates = CheckForUpdates ?? settings.CheckForUpdates;
        settings.PreferredUploadProtocol = UploadProtocol ?? settings.PreferredUploadProtocol;
    }

    /// <summary>Clé de fichier : « hôte:port » → « type SHA256:empreinte » (empreinte en base64).</summary>
    private static bool IsHostKeyEntry(string host, string key)
    {
        int colon = (host ?? "").LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(host![(colon + 1)..], out var port) || port is < 1 or > 65535
            || Uri.CheckHostName(host[..colon]) == UriHostNameType.Unknown)
        {
            return false;
        }

        var parts = (key ?? "").Trim().Split(' ');
        return parts.Length == 2 && parts[0].Length is > 0 and <= 64 && parts[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '@')
               && parts[1].StartsWith("SHA256:", StringComparison.Ordinal) && parts[1].Length > 7
               && parts[1][7..].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=');
    }

    /// <summary>Chemin complet (« C:\… » ou partage UNC « \\serveur\… ») : jamais relatif au dossier ou au lecteur courant.</summary>
    public static bool IsFullPath(string? path)
    {
        var p = (path ?? "").Trim();
        return p.IndexOfAny(Path.GetInvalidPathChars()) < 0
               && ((p.Length > 2 && p.StartsWith(@"\\", StringComparison.Ordinal) && p[2] is not ('\\' or '/'))
                   || (p.Length > 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] is ('\\' or '/')));
    }

    private static bool SamePvwa(string current, string next)
    {
        try
        {
            return PvwaClient.NormalizeBaseUri(current) == PvwaClient.NormalizeBaseUri(next);
        }
        catch (ArgumentException)
        {
            return string.Equals(current.Trim(), next.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Endpoint(string address, int port) =>
        string.IsNullOrWhiteSpace(address) ? "" : $"{address.Trim()}:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static string Describe(IEnumerable<PsmpServer> servers) => string.Join(", ", servers.Select(p =>
    {
        var domain = PsmpRouting.NormalizeDomain(p.Domain);
        return Endpoint(p.Address, p.Port) + (domain.Length > 0 && domain != PsmpRouting.DomainOf(p.Address) ? $" ({domain})" : "");
    }));

    private static void CheckPort(int? port)
    {
        if (port is { } value && value is < 1 or > 65535)
        {
            Fail(EnvironmentProblem.InvalidPort, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void Fail(EnvironmentProblem problem, string detail) => throw new EnvironmentFileException(problem, detail);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Énumérations par leur nom exact (casse ignorée) : ni nombre ni combinaison « LDAP, RADIUS ».</summary>
    private sealed class ExactEnumConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

        private sealed class Converter<T> : JsonConverter<T>
            where T : struct, Enum
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, text?.Trim(), StringComparison.OrdinalIgnoreCase));
                return name is null ? throw new JsonException($"{typeof(T).Name} : {text}") : Enum.Parse<T>(name);
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString());
        }
    }
}
