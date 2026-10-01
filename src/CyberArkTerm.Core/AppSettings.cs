using System.Text.Json;

namespace CyberArkTerm.Core;

/// <summary>
/// Préférences mémorisées entre deux lancements. Le mot de passe n'est jamais enregistré.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string PvwaUrl { get; set; } = "";

    public string UserName { get; set; } = "";

    public AuthMethod AuthMethod { get; set; } = AuthMethod.CyberArk;

    /// <summary>Adresse du PSM for SSH (PSMP) ; vide = connexions SSH directes désactivées.</summary>
    public string PsmpAddress { get; set; } = "";

    public int PsmpPort { get; set; } = 22;

    /// <summary>Double-clic sur un compte Unix : SSH via PSMP plutôt que PSM (RDP).</summary>
    public bool PreferSshForUnix { get; set; }

    public GroupBy GroupBy { get; set; } = GroupBy.Safe;

    /// <summary>ID des comptes favoris.</summary>
    public List<string> Favorites { get; set; } = [];

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
