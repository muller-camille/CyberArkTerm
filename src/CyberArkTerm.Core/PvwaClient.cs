using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CyberArkTerm.Core;

/// <summary>
/// Client minimal de l'API REST du PVWA CyberArk (v10 et ultérieures).
/// Un client correspond à une session : <see cref="LogonAsync"/>, appels, puis <see cref="LogoffAsync"/>.
/// </summary>
public sealed class PvwaClient : IDisposable
{
    /// <summary>Taille de page maximale acceptée par <c>GET API/Accounts</c>.</summary>
    public const int PageSize = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private string? _token;

    public PvwaClient(Uri baseUri, HttpMessageHandler handler)
    {
        BaseUri = baseUri;
        _http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public Uri BaseUri { get; }

    public bool IsAuthenticated => _token is not null;

    /// <summary>
    /// Crée un client pour l'URL saisie par l'utilisateur. L'authentification Windows
    /// réutilise la session Windows courante (Kerberos/NTLM).
    /// </summary>
    public static PvwaClient Create(string pvwaUrl, AuthMethod method)
    {
        var baseUri = NormalizeBaseUri(pvwaUrl);
        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = method == AuthMethod.Windows,
            UseCookies = true,
        };
        return new PvwaClient(baseUri, handler);
    }

    /// <summary>
    /// Transforme « pvwa.corp.local », « https://pvwa.corp.local/PasswordVault/v10/logon », etc.
    /// en « https://pvwa.corp.local/PasswordVault/ ».
    /// </summary>
    public static Uri NormalizeBaseUri(string input)
    {
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw new ArgumentException("L'adresse du PVWA est obligatoire.");
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException($"Adresse du PVWA invalide : « {input} ».");
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Le PVWA doit être joint en HTTPS : les identifiants transitent par cette connexion.");
        }

        var path = uri.AbsolutePath;
        int idx = path.IndexOf("/PasswordVault", StringComparison.OrdinalIgnoreCase);
        path = idx >= 0 ? path[..(idx + "/PasswordVault".Length)] : "/PasswordVault";

        return new UriBuilder(uri.Scheme, uri.Host, uri.Port, path + "/").Uri;
    }

    /// <summary>
    /// Ouvre une session. Pour une réponse à un challenge RADIUS, rappeler cette méthode sur le même
    /// client avec la réponse comme mot de passe (les cookies de la première tentative sont conservés).
    /// </summary>
    /// <exception cref="PvwaException">Échec d'authentification ; voir <see cref="PvwaException.IsRadiusChallenge"/>.</exception>
    public async Task LogonAsync(AuthMethod method, string? userName, string? password, CancellationToken ct = default)
    {
        object body = method == AuthMethod.Windows
            ? new { concurrentSession = true }
            : new { username = userName, password, concurrentSession = true };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"API/auth/{method}/Logon")
        {
            Content = JsonContent.Create(body),
        };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateErrorAsync(response, ct).ConfigureAwait(false);
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _token = ParseToken(text);
    }

    /// <summary>
    /// Charge tous les comptes visibles par l'utilisateur connecté (safes sur lesquels il a le droit « List »),
    /// page par page.
    /// </summary>
    /// <param name="progress">Reçoit (comptes chargés, total annoncé par le PVWA) après chaque page.</param>
    public async Task<List<PvwaAccount>> GetAccountsAsync(IProgress<(int Loaded, int Total)>? progress = null, CancellationToken ct = default)
    {
        var accounts = new List<PvwaAccount>();
        while (true)
        {
            var page = await GetAsync<AccountsPage>($"API/Accounts?offset={accounts.Count}&limit={PageSize}", ct).ConfigureAwait(false);
            var items = page.Value ?? [];
            accounts.AddRange(items);
            progress?.Report((accounts.Count, Math.Max(page.Count, accounts.Count)));

            bool more = page.Count > 0 ? accounts.Count < page.Count : page.NextLink is not null;
            if (items.Count == 0 || !more)
            {
                return accounts;
            }
        }
    }

    /// <summary>Ferme la session côté PVWA. Sans effet si aucune session n'est ouverte.</summary>
    public async Task LogoffAsync(CancellationToken ct = default)
    {
        if (_token is null)
        {
            return;
        }

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "API/Auth/Logoff");
        _token = null;
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    internal static string ParseToken(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? token = root.ValueKind switch
            {
                // API v10+ : le jeton est renvoyé comme une simple chaîne JSON.
                JsonValueKind.String => root.GetString(),
                // Ancienne API v9 : { "CyberArkLogonResult": "..." }.
                JsonValueKind.Object when root.TryGetProperty("CyberArkLogonResult", out var r) => r.GetString(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }
        catch (JsonException)
        {
        }

        throw new PvwaException(HttpStatusCode.OK, null, "Réponse inattendue du PVWA : aucun jeton de session reçu.");
    }

    private async Task<T> GetAsync<T>(string relativeUri, CancellationToken ct)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, relativeUri);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateErrorAsync(response, ct).ConfigureAwait(false);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new PvwaException(response.StatusCode, null, "Réponse vide du PVWA.");
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string relativeUri)
    {
        var token = _token ?? throw new InvalidOperationException("Aucune session ouverte sur le PVWA.");
        var request = new HttpRequestMessage(method, relativeUri);
        // Le PVWA attend le jeton brut, sans schéma (« Bearer » etc.).
        request.Headers.TryAddWithoutValidation("Authorization", token);
        return request;
    }

    private static async Task<PvwaException> CreateErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? code = null;
        string? message = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.NameEquals("ErrorCode") || p.NameEquals("errorCode"))
                    {
                        code = p.Value.GetString();
                    }
                    else if (p.NameEquals("ErrorMessage") || p.NameEquals("errorMessage"))
                    {
                        message = p.Value.GetString();
                    }
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // Corps non JSON (page d'erreur IIS, proxy...) : on se contente du statut HTTP.
        }

        var text = message ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        if (code is not null)
        {
            text = $"{text} ({code})";
        }

        return new PvwaException(response.StatusCode, code, text, message);
    }

    private sealed class AccountsPage
    {
        public List<PvwaAccount>? Value { get; set; }

        public int Count { get; set; }

        public string? NextLink { get; set; }
    }
}
