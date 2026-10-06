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

    /// <summary>
    /// Adresse du PSM for SSH (PSMP) ; vide = connexions SSH désactivées. Renseignée, les comptes Unix s'ouvrent par
    /// défaut en SSH via le PSMP.
    /// </summary>
    public string PsmpAddress { get; set; } = "";

    public int PsmpPort { get; set; } = 22;

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

    /// <summary>Listes de serveurs partagées (fichiers sur un partage réseau) affichées dans l'onglet « Courants ».</summary>
    public List<string> SharedLists { get; set; } = [];

    /// <summary>Sessions SSH dans un onglet CyberArkTerm (terminal + navigateur de fichiers) plutôt que Windows Terminal.</summary>
    public bool SshInApp { get; set; } = true;

    /// <summary>Journal de débogage (déroulement des connexions, sans secret), désactivé par défaut.</summary>
    public bool DebugLogEnabled { get; set; }

    /// <summary>
    /// Protocole essayé d'abord pour déposer des fichiers sur le serveur ; refusé par le serveur, l'autre prend le
    /// relais. Nouvelle clé (l'ancienne, « UploadProtocol », valait SCP par défaut) : toutes les installations passent
    /// à SFTP une fois.
    /// </summary>
    public TransferProtocol PreferredUploadProtocol { get; set; } = TransferProtocol.Sftp;

    /// <summary>Proposer d'envoyer une seule archive .tar.gz quand on dépose au moins <see cref="ArchiveThreshold"/> fichiers.</summary>
    public bool OfferArchive { get; set; } = true;

    /// <summary>Nombre de fichiers à partir duquel l'archive .tar.gz est proposée.</summary>
    public int ArchiveThreshold { get; set; } = 200;

    /// <summary>
    /// Le suivi d'un fichier (tail -f) ouvre sa propre connexion SFTP (une session PSMP de plus) au lieu de partager
    /// celle de l'onglet Fichiers.
    /// </summary>
    public bool TailIndependentSession { get; set; }

    /// <summary>Suivi d'un fichier : couleur des lignes selon leur niveau (erreur, avertissement).</summary>
    public bool TailLevelColors { get; set; } = true;

    /// <summary>Suivi d'un fichier : mots surlignés, séparés par des virgules.</summary>
    public string TailHighlights { get; set; } = "";

    /// <summary>Suivi d'un fichier : mots qui déclenchent une alerte, séparés par des virgules.</summary>
    public string TailAlerts { get; set; } = "";

    /// <summary>Suivi d'un fichier : notification Windows (sans le contenu de la ligne) pour une alerte.</summary>
    public bool TailAlertNotify { get; set; } = true;

    /// <summary>
    /// Rechercher une nouvelle version au démarrage (au plus une fois par jour) : une requête vers GitHub, désactivée
    /// par défaut. La recherche reste possible à la demande depuis « À propos ».
    /// </summary>
    public bool CheckForUpdates { get; set; }

    /// <summary>Dernière recherche de nouvelle version (UTC).</summary>
    public DateTime LastUpdateCheck { get; set; }

    /// <summary>Palette de couleurs des terminaux SSH (identifiant d'une palette de <c>TerminalTheme</c>).</summary>
    public string TerminalTheme { get; set; } = "campbell";

    /// <summary>Taille de police par défaut des terminaux SSH (Ctrl+molette la change pour un terminal).</summary>
    public double TerminalFontSize { get; set; } = 14;

    /// <summary>Le clic droit dans le terminal colle le presse-papiers au lieu d'ouvrir le menu (Maj+clic droit l'ouvre).</summary>
    public bool TerminalRightClickPastes { get; set; }

    /// <summary>Outil de comparaison de fichiers (exécutable) proposé dans la fenêtre de comparaison ; vide = aucun.</summary>
    public string CompareTool { get; set; } = "";

    /// <summary>Arguments de l'outil de comparaison : {0} = fichier de gauche, {1} = fichier de droite.</summary>
    public string CompareToolArguments { get; set; } = DefaultCompareArguments;

    public const string DefaultCompareArguments = "\"{0}\" \"{1}\"";

    /// <summary>Installe PROMPT_COMMAND à l'ouverture d'une session SSH pour que le navigateur suive le dossier du terminal.</summary>
    public bool FollowTerminalFolder { get; set; } = true;

    public bool ShowHiddenFiles { get; set; }

    /// <summary>Tri de l'onglet Fichiers : colonne dont l'en-tête a été cliqué, et sens.</summary>
    public RemoteSortColumn FileSortColumn { get; set; } = RemoteSortColumn.Name;

    public bool FileSortDescending { get; set; }

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
        foreach (var recent in Recent.Where(r => !string.Equals(r.Mode, RecentSession.SshMode, StringComparison.OrdinalIgnoreCase)))
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
                settings.SharedLists ??= [];
                settings.KnownHosts ??= [];
                settings.TailHighlights ??= "";
                settings.TailAlerts ??= "";
                settings.CompareTool ??= "";
                settings.TerminalTheme ??= "campbell";
                settings.CompareToolArguments ??= DefaultCompareArguments;
                foreach (var session in settings.Sessions.OfType<SavedSession>())
                {
                    session.TailFiles ??= [];
                }

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

    /// <summary>Valeur de <see cref="Mode"/> pour une connexion SSH via PSMP.</summary>
    public const string SshMode = "SSH";

    /// <summary>Composant PSM utilisé, ou « SSH » pour une connexion via PSMP.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Machine cible choisie pour un compte de domaine.</summary>
    public string? RemoteMachine { get; set; }

    public DateTime When { get; set; }
}
