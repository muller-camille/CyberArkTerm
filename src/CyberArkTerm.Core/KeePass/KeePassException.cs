namespace CyberArkTerm.Core.KeePass;

public enum KeePassError
{
    /// <summary>Le fichier n'est pas un coffre KeePass 2 (.kdbx).</summary>
    NotKeePass,

    /// <summary>Version, chiffrement ou dérivation de clé non pris en charge.</summary>
    Unsupported,

    /// <summary>Mot de passe maître ou fichier clé incorrect.</summary>
    InvalidKey,

    /// <summary>Fichier clé endommagé (somme de contrôle fausse).</summary>
    InvalidKeyFile,

    /// <summary>Fichier endommagé ou modifié hors de KeePass.</summary>
    Corrupted,

    /// <summary>L'entrée a été modifiée ou supprimée dans le coffre entre-temps.</summary>
    Conflict,

    /// <summary>Le fichier change sans cesse pendant l'enregistrement (autre programme).</summary>
    Busy,
}

/// <summary>Erreur de lecture ou d'écriture d'un coffre KeePass ; le message est prêt à afficher.</summary>
public sealed class KeePassException(KeePassError kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public KeePassError Kind { get; } = kind;
}
