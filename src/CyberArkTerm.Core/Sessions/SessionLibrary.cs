using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core;

/// <summary>Opérations sur les dossiers et sessions de l'onglet « Courants » (stockés dans les préférences).</summary>
public static class SessionLibrary
{
    /// <summary>
    /// Arbre de l'onglet « Courants » pour le PVWA <paramref name="pvwaHost"/>. Avec une recherche
    /// (<paramref name="filter"/>), seuls les serveurs qui y répondent et leurs dossiers sont gardés.
    /// </summary>
    public static SessionFolderNode BuildTree(AppSettings settings, string pvwaHost, string? filter = null) =>
        BuildTree(settings.SessionFolderList, settings.Sessions.Where(s => IsForHost(s, pvwaHost)), filter);

    /// <summary>Arbre de dossiers (vides compris, sauf pendant une recherche) et de serveurs, triés par nom.</summary>
    public static SessionFolderNode BuildTree(IEnumerable<string> folders, IEnumerable<SavedSession> sessions, string? filter = null)
    {
        bool filtered = !string.IsNullOrWhiteSpace(filter);
        var root = new SessionFolderNode("");
        var nodes = new Dictionary<string, SessionFolderNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };

        SessionFolderNode Ensure(string path)
        {
            path = SessionFolders.Normalize(path);
            if (nodes.TryGetValue(path, out var node))
            {
                return node;
            }

            node = new SessionFolderNode(path);
            nodes[path] = node;
            Ensure(SessionFolders.Parent(path)).Folders.Add(node);
            return node;
        }

        foreach (var folder in filtered ? [] : folders)
        {
            Ensure(folder);
        }

        foreach (var session in sessions.Where(s => !filtered || Matches(s, filter)))
        {
            Ensure(session.Folder).Sessions.Add(session);
        }

        Sort(root);
        return root;
    }

    /// <summary>Recherche dans « Courants » : nom, serveur, utilisateur, dossier, mode, composant, machine cible, plateforme, safe.</summary>
    public static bool Matches(SavedSession session, string? query) => SearchQuery.Matches(
        query,
        session.Name,
        session.Address,
        session.UserName,
        session.Folder,
        ModeName(session.Mode),
        session.Component,
        session.RemoteMachine,
        session.PlatformId,
        session.SafeName);

    /// <summary>Nom court du type de connexion (recherche, affichage).</summary>
    public static string ModeName(ConnectMode mode) => mode switch
    {
        ConnectMode.Ssh => "SSH",
        ConnectMode.Sftp => "SFTP",
        _ => "PSM",
    };

    public static bool IsForHost(SavedSession session, string pvwaHost) =>
        session.PvwaHost.Length == 0 || string.Equals(session.PvwaHost, pvwaHost, StringComparison.OrdinalIgnoreCase);

    public static void AddFolder(AppSettings settings, string path)
    {
        path = SessionFolders.Normalize(path);
        while (path.Length > 0)
        {
            if (!settings.SessionFolderList.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                settings.SessionFolderList.Add(path);
            }

            path = SessionFolders.Parent(path);
        }
    }

    /// <summary>Renomme le dernier segment d'un dossier ; sous-dossiers et sessions suivent.</summary>
    public static string RenameFolder(AppSettings settings, string path, string newName)
    {
        path = SessionFolders.Normalize(path);
        newName = SessionFolders.Normalize(newName);
        if (path.Length == 0 || newName.Length == 0 || newName.Contains('/'))
        {
            throw new ArgumentException(CoreStrings.InvalidFolderName);
        }

        var target = SessionFolders.Combine(SessionFolders.Parent(path), newName);
        MoveFolder(settings, path, target);
        return target;
    }

    /// <summary>Déplace un dossier (avec son contenu) sous un autre chemin.</summary>
    public static void MoveFolder(AppSettings settings, string path, string target)
    {
        path = SessionFolders.Normalize(path);
        target = SessionFolders.Normalize(target);
        if (path.Length == 0 || SessionFolders.IsWithin(target, path) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(CoreStrings.FolderIntoItself);
        }

        for (int i = 0; i < settings.SessionFolderList.Count; i++)
        {
            if (SessionFolders.IsWithin(settings.SessionFolderList[i], path))
            {
                settings.SessionFolderList[i] = SessionFolders.Rebase(settings.SessionFolderList[i], path, target);
            }
        }

        foreach (var session in settings.Sessions.Where(s => s.Folder.Length > 0 && SessionFolders.IsWithin(s.Folder, path)))
        {
            session.Folder = SessionFolders.Rebase(session.Folder, path, target);
        }

        AddFolder(settings, target);
        Deduplicate(settings);
    }

    /// <summary>Serveurs de ce PVWA dans un dossier et ses sous-dossiers, recherche en cours ou non.</summary>
    public static int CountInFolder(AppSettings settings, string path, string pvwaHost)
    {
        path = SessionFolders.Normalize(path);
        return settings.Sessions.Count(s => InFolder(s, path) && IsForHost(s, pvwaHost));
    }

    /// <summary>
    /// Supprime un dossier, ses sous-dossiers et les serveurs de ce PVWA qu'ils contiennent ; renvoie le nombre de serveurs
    /// supprimés. Les serveurs d'un autre PVWA (invisibles ici) restent, avec leurs dossiers.
    /// </summary>
    public static int DeleteFolder(AppSettings settings, string path, string pvwaHost)
    {
        path = SessionFolders.Normalize(path);
        if (path.Length == 0)
        {
            throw new ArgumentException(CoreStrings.RootCannotBeDeleted);
        }

        int removed = settings.Sessions.RemoveAll(s => InFolder(s, path) && IsForHost(s, pvwaHost));
        var kept = settings.Sessions.Where(s => InFolder(s, path)).Select(s => SessionFolders.Normalize(s.Folder)).ToList();
        settings.SessionFolderList.RemoveAll(f => SessionFolders.IsWithin(f, path)
                                                  && !kept.Any(k => SessionFolders.IsWithin(k, f)));
        return removed;
    }

    private static bool InFolder(SavedSession session, string path) =>
        session.Folder.Length > 0 && SessionFolders.IsWithin(session.Folder, path);

    public static SavedSession AddSession(AppSettings settings, PvwaAccount account, string pvwaHost, string folder, bool hasPsmp = true)
    {
        var session = SavedSession.FromAccount(account, pvwaHost, folder, hasPsmp);
        AddFolder(settings, session.Folder);
        settings.Sessions.Add(session);
        return session;
    }

    /// <summary>
    /// Ajoute une connexion récente avec sa configuration : SSH, SFTP, ou PSM avec le composant utilisé, et la machine cible
    /// choisie (compte de domaine). Elle garde le nom affiché dans les connexions récentes (« utilisateur@machine »).
    /// </summary>
    public static SavedSession AddFromRecent(AppSettings settings, PvwaAccount account, RecentSession recent, string pvwaHost, string folder)
    {
        var session = AddSession(settings, account, pvwaHost, folder);
        var mode = recent.Mode.Trim();
        session.Mode = RecentMode(mode);
        session.Component = session.Mode != ConnectMode.Psm || mode.Length == 0 ? null : mode;
        session.RemoteMachine = string.IsNullOrWhiteSpace(recent.RemoteMachine) ? null : recent.RemoteMachine.Trim();
        if (!string.IsNullOrWhiteSpace(recent.Label))
        {
            session.Name = recent.Label.Trim();
        }

        return session;
    }

    /// <summary>Type de connexion d'une connexion récente (« SSH », « SFTP » ou le composant PSM).</summary>
    public static ConnectMode RecentMode(string mode) =>
        string.Equals(mode.Trim(), RecentSession.SshMode, StringComparison.OrdinalIgnoreCase) ? ConnectMode.Ssh
        : string.Equals(mode.Trim(), RecentSession.SftpMode, StringComparison.OrdinalIgnoreCase) ? ConnectMode.Sftp
        : ConnectMode.Psm;

    public static void MoveSession(AppSettings settings, SavedSession session, string folder)
    {
        session.Folder = SessionFolders.Normalize(folder);
        AddFolder(settings, session.Folder);
    }

    /// <summary>Reprend les anciens favoris comme sessions à la racine de « Courants » (une seule fois).</summary>
    public static void MigrateFavorites(AppSettings settings, IReadOnlyDictionary<string, PvwaAccount> accounts, string pvwaHost)
    {
        if (settings.Favorites.Count == 0)
        {
            return;
        }

        foreach (var id in settings.Favorites)
        {
            if (accounts.TryGetValue(id, out var account) && !settings.Sessions.Any(s => s.AccountId == id))
            {
                settings.Sessions.Add(SavedSession.FromAccount(account, pvwaHost, ""));
            }
        }

        settings.Favorites.Clear();
    }

    private static void Deduplicate(AppSettings settings)
    {
        var unique = settings.SessionFolderList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        settings.SessionFolderList.Clear();
        settings.SessionFolderList.AddRange(unique);
    }

    private static void Sort(SessionFolderNode node)
    {
        node.Folders.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        node.Sessions.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        foreach (var child in node.Folders)
        {
            Sort(child);
        }
    }
}
