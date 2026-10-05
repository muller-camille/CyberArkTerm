using System.Text.Json;
using CyberArkTerm.Core.KeePass;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core;

/// <summary>
/// Préférences mémorisées entre deux lancements. Le mot de passe n'est jamais enregistré.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Langue de l'interface (« fr », « en », « it ») ; vide = langue de Windows.</summary>
    public string Language { get; set; } = "";

    public string PvwaUrl { get; set; } = "";

    public string UserName { get; set; } = "";

    public AuthMethod AuthMethod { get; set; } = AuthMethod.CyberArk;

    /// <summary>Adresse du PSM for SSH (PSMP) ; vide = connexions SSH directes désactivées.</summary>
    public string PsmpAddress { get; set; } = "";

    public int PsmpPort { get; set; } = 22;

    /// <summary>Double-clic sur un compte Unix : SSH via PSMP plutôt que PSM (RDP).</summary>
    public bool PreferSshForUnix { get; set; }

    public GroupBy GroupBy { get; set; } = GroupBy.Safe;

    /// <summary>Anciens favoris (remplacés par l'onglet « Courants », migrés au chargement des comptes).</summary>
    public List<string> Favorites { get; set; } = [];

    /// <summary>Dossiers de l'onglet « Courants » (« Prod/Web »...), y compris les dossiers vides.</summary>
    public List<string> SessionFolderList { get; set; } = [];

    /// <summary>Serveurs de l'onglet « Courants », avec leur configuration.</summary>
    public List<SavedSession> Sessions { get; set; } = [];

    /// <summary>Requête légère régulière pour que la session PVWA n'expire pas par inactivité.</summary>
    public bool KeepPvwaSessionAlive { get; set; } = true;

    /// <summary>Coffres KeePass affichés comme dossiers de l'onglet « Courants » (accès d'urgence hors CyberArk).</summary>
    public List<KeePassFolder> KeePassFolders { get; set; } = [];

    /// <summary>Sessions SSH dans un onglet CyberArkTerm (terminal + navigateur de fichiers) plutôt que Windows Terminal.</summary>
    public bool SshInApp { get; set; } = true;

    /// <summary>Sessions Bureau à distance (PSM) dans un onglet CyberArkTerm plutôt que dans mstsc.</summary>
    public bool RdpInApp { get; set; } = true;

    /// <summary>
    /// Applications distantes (RemoteApp) : leur fenêtre principale s'affiche dans l'onglet, leurs menus et boîtes de
    /// dialogue au-dessus ; sinon toutes leurs fenêtres s'ouvrent à part, sur le bureau.
    /// </summary>
    public bool RemoteAppInTab { get; set; } = true;

    /// <summary>
    /// Composants PSM en application distante (RemoteApp) ouverts comme un bureau, dans l'onglet, plutôt qu'en
    /// fenêtres séparées ; le serveur PSM doit accepter les sessions en bureau. Désactivé par défaut (un PSM les a
    /// refusées), et désactivé de lui-même quand un PSM ferme une telle session aussitôt ouverte.
    /// </summary>
    public bool PsmRemoteAppAsDesktop { get; set; }

    /// <summary>Journal de débogage (déroulement des connexions, sans secret), désactivé par défaut.</summary>
    public bool DebugLogEnabled { get; set; }

    /// <summary>Protocole utilisé pour déposer des fichiers sur le serveur.</summary>
    public TransferProtocol UploadProtocol { get; set; } = TransferProtocol.Scp;

    /// <summary>Installe PROMPT_COMMAND à l'ouverture d'une session SSH pour que le navigateur suive le dossier du terminal.</summary>
    public bool FollowTerminalFolder { get; set; } = true;

    public bool ShowHiddenFiles { get; set; }

    /// <summary>Éditeur de texte pour « Modifier » dans l'onglet Fichiers (chemin d'un exécutable) ; vide = Bloc-notes.</summary>
    public string TextEditor { get; set; } = "";

    /// <summary>Empreintes des clés d'hôte PSMP acceptées (« hôte:port » → « algorithme SHA256:... »).</summary>
    public Dictionary<string, string> KnownHosts { get; set; } = [];

    public List<RecentSession> Recent { get; set; } = [];

    /// <summary>Composant PSM choisi par l'utilisateur, par ID de plateforme.</summary>
    public Dictionary<string, string> ComponentByPlatform { get; set; } = [];

    public const int MaxRecent = 15;

    public bool IsFavorite(string accountId) => Favorites.Contains(accountId, StringComparer.Ordinal);

    public void ToggleFavorite(string accountId)
    {
        if (Favorites.RemoveAll(id => id == accountId) == 0)
        {
            Favorites.Add(accountId);
        }
    }

    public void AddRecent(RecentSession session)
    {
        Recent.RemoveAll(r => r.AccountId == session.AccountId && r.Mode == session.Mode);
        Recent.Insert(0, session);
        if (Recent.Count > MaxRecent)
        {
            Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
        }
    }

    /// <summary>Composant mémorisé pour la plateforme du compte, sinon celui déduit de la plateforme.</summary>
    public string ResolveComponent(PvwaAccount account)
    {
        foreach (var (platform, component) in ComponentByPlatform)
        {
            if (string.Equals(platform, account.PlatformId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(component))
            {
                return component;
            }
        }

        return AccountClassifier.DefaultComponent(account);
    }

    /// <summary>
    /// Composants proposés dans les listes : celui mémorisé pour la plateforme <paramref name="platformId"/>, puis ceux
    /// déjà utilisés (mémorisés, serveurs de « Courants », connexions récentes), puis les composants usuels de CyberArk.
    /// Un PVWA peut nommer les siens autrement (par ex. WIN-PSM) : saisi une fois, un composant est ensuite proposé.
    /// </summary>
    public IReadOnlyList<string> KnownComponents(string? platformId)
    {
        var list = new List<string>();
        void Add(string? component)
        {
            var name = component?.Trim();
            if (!string.IsNullOrEmpty(name) && !list.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(name);
            }
        }

        foreach (var (platform, component) in ComponentByPlatform)
        {
            if (string.Equals(platform, platformId, StringComparison.OrdinalIgnoreCase))
            {
                Add(component);
            }
        }

        foreach (var component in ComponentByPlatform.Values)
        {
            Add(component);
        }

        foreach (var session in Sessions.Where(s => s.Mode == ConnectMode.Psm))
        {
            Add(session.Component);
        }

        // Le mode d'une connexion récente est son composant PSM, ou « SSH » (PSMP).
        foreach (var recent in Recent.Where(r => !string.Equals(r.Mode, "SSH", StringComparison.OrdinalIgnoreCase)))
        {
            Add(recent.Mode);
        }

        foreach (var component in AccountClassifier.CommonComponents)
        {
            Add(component);
        }

        return list;
    }

    public void RememberComponent(string? platformId, string component)
    {
        if (string.IsNullOrWhiteSpace(platformId))
        {
            return;
        }

        foreach (var key in ComponentByPlatform.Keys.Where(k => string.Equals(k, platformId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ComponentByPlatform.Remove(key);
        }

        ComponentByPlatform[platformId] = component;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CyberArkTerm",
        "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
                settings.Favorites ??= [];
                settings.Recent ??= [];
                settings.ComponentByPlatform ??= [];
                settings.SessionFolderList ??= [];
                settings.Sessions ??= [];
                settings.KnownHosts ??= [];
                return settings;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fichier illisible ou corrompu : on repart des valeurs par défaut.
        }

        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

/// <summary>Connexion lancée récemment, affichée sur l'écran d'accueil.</summary>
public sealed class RecentSession
{
    public string AccountId { get; set; } = "";

    /// <summary>Libellé affiché, par ex. « adm-t0@srv01.corp.local ».</summary>
    public string Label { get; set; } = "";

    /// <summary>Composant PSM utilisé, ou « SSH » pour une connexion via PSMP.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Machine cible choisie pour un compte de domaine.</summary>
    public string? RemoteMachine { get; set; }

    public DateTime When { get; set; }
}
