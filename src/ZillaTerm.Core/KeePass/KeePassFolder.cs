namespace ZillaTerm.Core.KeePass;

/// <summary>
/// Coffre KeePass affiché comme un dossier de « Mes serveurs ». Seuls l'emplacement du fichier et celui du
/// fichier clé sont enregistrés dans les préférences ; le mot de passe maître ne l'est jamais (au mieux, dans le
/// coffre local chiffré, sous <see cref="Id"/>).
/// </summary>
public sealed class KeePassFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Nom affiché ; par défaut le nom du fichier.</summary>
    public string Name { get; set; } = "";

    /// <summary>Chemin du fichier .kdbx.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Fichier clé, si le coffre en demande un.</summary>
    public string? KeyFilePath { get; set; }

    /// <summary>Le coffre demande un mot de passe maître (faux : fichier clé seul).</summary>
    public bool UsesPassword { get; set; } = true;

    /// <summary>Le mot de passe maître est gardé dans le coffre local.</summary>
    public bool RememberPassword { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => Name.Length > 0 ? Name : Path.GetFileNameWithoutExtension(FilePath);
}
