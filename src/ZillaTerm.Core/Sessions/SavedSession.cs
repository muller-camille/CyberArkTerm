namespace ZillaTerm.Core;

public enum ConnectMode
{
    /// <summary>Session PSM ouverte via un fichier RDP (bouton « Connect » du PVWA).</summary>
    Psm,

    /// <summary>Session SSH via PSM for SSH (PSMP).</summary>
    Ssh,

    /// <summary>Fichiers seuls via PSM for SSH (session PSMP SFTP, sans terminal).</summary>
    Sftp,
}

/// <summary>
/// Serveur de « Mes serveurs » : un compte CyberArk que l'utilisateur utilise régulièrement,
/// rangé dans un dossier, avec sa propre configuration de connexion.
/// </summary>
public sealed class SavedSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Compte CyberArk (ID PVWA).</summary>
    public string AccountId { get; set; } = "";

    /// <summary>PVWA d'origine : les sessions d'un autre PVWA ne sont pas affichées.</summary>
    public string PvwaHost { get; set; } = "";

    /// <summary>Nom affiché (par défaut « utilisateur@serveur »).</summary>
    public string Name { get; set; } = "";

    /// <summary>Dossier, sous la forme « Prod/Web » ; vide = racine.</summary>
    public string Folder { get; set; } = "";

    public ConnectMode Mode { get; set; } = ConnectMode.Psm;

    /// <summary>Composant PSM ; vide = celui déduit de la plateforme.</summary>
    public string? Component { get; set; }

    /// <summary>Machine cible (compte de domaine).</summary>
    public string? RemoteMachine { get; set; }

    /// <summary>Motif d'accès proposé par défaut.</summary>
    public string? Reason { get; set; }

    /// <summary>Dossier ouvert par le navigateur de fichiers à la connexion (SSH ou SFTP) ; vide = dossier personnel.</summary>
    public string? StartDirectory { get; set; }

    /// <summary>Fichiers suivis (tail -f) sur ce serveur, le plus récent en tête, pour les suivre à nouveau d'un clic.</summary>
    public List<string> TailFiles { get; set; } = [];

    public const int MaxTailFiles = 12;

    /// <summary>Mémorise un fichier suivi (en tête de liste).</summary>
    public void RememberTail(string path)
    {
        TailFiles ??= [];
        TailFiles.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));
        TailFiles.Insert(0, path);
        if (TailFiles.Count > MaxTailFiles)
        {
            TailFiles.RemoveRange(MaxTailFiles, TailFiles.Count - MaxTailFiles);
        }
    }

    // Copie des informations du compte, pour l'affichage tant que la liste du PVWA n'est pas chargée.
    public string? Address { get; set; }

    public string? UserName { get; set; }

    public string? PlatformId { get; set; }

    public string? SafeName { get; set; }

    /// <summary>
    /// Serveur de « Mes serveurs » pour ce compte ; type de connexion d'après la plateforme. Sans PSMP renseigné
    /// (<paramref name="hasPsmp"/> faux), PSM, comme un double-clic dans la liste des comptes. Pour une liste partagée,
    /// le PSMP des collègues n'est pas connu : il est supposé renseigné.
    /// </summary>
    public static SavedSession FromAccount(PvwaAccount account, string pvwaHost, string folder, bool hasPsmp = true) => new()
    {
        AccountId = account.Id,
        PvwaHost = pvwaHost,
        Name = $"{account.UserName}@{account.Address}",
        Folder = SessionFolders.Normalize(folder),
        Address = account.Address,
        UserName = account.UserName,
        PlatformId = account.PlatformId,
        SafeName = account.SafeName,
        Mode = AccountClassifier.DefaultMode(account, hasPsmp),
    };
}

/// <summary>Chemins de dossiers « A/B/C » de « Mes serveurs ».</summary>
public static class SessionFolders
{
    /// <summary>
    /// Niveaux de dossiers gardés : les suivants sont ignorés. Un chemin forgé de milliers de niveaux (liste partagée,
    /// fichier importé) épuiserait sinon la pile des parcours récursifs de l'arbre, et l'application s'arrêterait.
    /// </summary>
    public const int MaxDepth = 64;

    public static string Normalize(string? path)
    {
        var parts = (path ?? "").Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('/', parts.Length > MaxDepth ? parts[..MaxDepth] : parts);
    }

    public static string Parent(string path)
    {
        var normalized = Normalize(path);
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? "" : normalized[..slash];
    }

    public static string Name(string path)
    {
        var normalized = Normalize(path);
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    public static string Combine(string parent, string name) =>
        Normalize(Normalize(parent) + "/" + Normalize(name));

    /// <summary>Vrai si <paramref name="path"/> est <paramref name="folder"/> ou l'un de ses sous-dossiers.</summary>
    public static bool IsWithin(string path, string folder)
    {
        path = Normalize(path);
        folder = Normalize(folder);
        return folder.Length == 0
            || string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static string Rebase(string path, string oldFolder, string newFolder)
    {
        path = Normalize(path);
        oldFolder = Normalize(oldFolder);
        var rest = path.Length == oldFolder.Length ? "" : path[(oldFolder.Length + 1)..];
        return Combine(newFolder, rest);
    }
}

/// <summary>Dossier de l'arbre « Mes serveurs ».</summary>
public sealed class SessionFolderNode(string path)
{
    public string Path { get; } = path;

    public string Name => Path.Length == 0 ? "" : SessionFolders.Name(Path);

    public List<SessionFolderNode> Folders { get; } = [];

    public List<SavedSession> Sessions { get; } = [];

    public int TotalSessions => Sessions.Count + Folders.Sum(f => f.TotalSessions);
}
