using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Localization;
using CyberArkTerm.Core.Ssh;

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
        _http = new HttpClient(new DebugLogHandler(handler)) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(60) };
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
            throw new ArgumentException(CoreStrings.PvwaAddressRequired);
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, CoreStrings.PvwaAddressInvalid, input));
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(CoreStrings.PvwaHttpsRequired);
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
            // Corps sérialisé à l'avance pour envoyer un Content-Length : JsonContent passe en
            // « chunked », que certains load balancers / WAF placés devant le PVWA rejettent.
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
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
            var page = await GetAsync<Page<PvwaAccount>>($"API/Accounts?offset={accounts.Count}&limit={PageSize}", ct).ConfigureAwait(false);
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

    /// <summary>
    /// Membres d'un safe (utilisateurs et groupes) avec leurs droits, page par page. Le PVWA ne les donne qu'à un
    /// utilisateur qui a le droit « View Safe Members » sur ce safe : sinon <see cref="PvwaException"/> (403 en général).
    /// </summary>
    /// <exception cref="PvwaException">Refus du PVWA (droit manquant, safe introuvable, API absente de cette version...).</exception>
    public async Task<List<SafeMember>> GetSafeMembersAsync(string safeName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeName);

        var members = new List<SafeMember>();
        var safe = Uri.EscapeDataString(safeName);
        while (true)
        {
            var page = await GetAsync<Page<SafeMember>>($"API/Safes/{safe}/Members?offset={members.Count}&limit={PageSize}", ct)
                .ConfigureAwait(false);
            var items = page.Value ?? [];
            members.AddRange(items);

            bool more = page.Count > 0 ? members.Count < page.Count : page.NextLink is not null;
            if (items.Count == 0 || !more)
            {
                return members;
            }
        }
    }

    /// <summary>
    /// Crée un compte dans un safe (droit « Add accounts » sur le safe, et en général « Update account content » pour
    /// fournir le mot de passe) et renvoie le compte créé. Le corps de la requête, qui contient le mot de passe, est
    /// effacé de la mémoire après l'envoi ; il n'est jamais écrit dans le journal de débogage.
    /// </summary>
    /// <exception cref="PvwaException">Refus du PVWA (droit manquant, plateforme inconnue, compte existant...).</exception>
    public async Task<PvwaAccount> AddAccountAsync(NewAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var body = account.ToJson();
        try
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, "API/Accounts");
            // Longueur connue : envoyé avec Content-Length (pas en « chunked », rejeté par certains load balancers).
            request.Content = new ReadOnlyMemoryContent(body.WrittenMemory);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateErrorAsync(response, ct).ConfigureAwait(false);
            }

            return await response.Content.ReadFromJsonAsync<PvwaAccount>(JsonOptions, ct).ConfigureAwait(false)
                ?? throw new PvwaException(response.StatusCode, null, CoreStrings.PvwaEmptyResponse);
        }
        finally
        {
            body.Clear();
        }
    }

    /// <summary>
    /// Demande au PVWA une connexion PSM pour le compte (équivalent du bouton « Connect » du PVWA)
    /// et renvoie le fichier RDP à ouvrir avec <c>mstsc.exe</c>.
    /// </summary>
    /// <exception cref="PvwaException">Refus du PVWA (composant inconnu, motif exigé, accès non autorisé...).</exception>
    public async Task<byte[]> PsmConnectAsync(string accountId, PsmConnectOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionComponent);

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"API/Accounts/{Uri.EscapeDataString(accountId)}/PSMConnect");
        // « octet-stream » : le PVWA renvoie le fichier RDP ; « json » renverrait les données PSM Gateway (HTML5).
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Content = new StringContent(JsonSerializer.Serialize(options.ToRequestBody()), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateErrorAsync(response, ct).ConfigureAwait(false);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return RdpFile.FromPsmConnectResponse(bytes, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Demande une clé SSH temporaire « MFA caching » pour s'authentifier au PSMP sans ressaisir
    /// mot de passe et MFA. Renvoie null si la fonctionnalité n'est pas activée sur le PVWA.
    /// </summary>
    public async Task<MfaSshKey?> GetMfaCachingSshKeyAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "API/Users/Secret/SSHKeys/Cache");
        request.Content = new StringContent("""{"formats":["OpenSSH","PEM","PPK"]}""", Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw await CreateErrorAsync(response, ct).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return MfaSshKey.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Requête légère (un compte au plus) qui garde la session PVWA active : le délai d'inactivité du PVWA repart
    /// de zéro. <see cref="PvwaException.IsUnauthorized"/> si la session a déjà expiré.
    /// </summary>
    public async Task KeepAliveAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "API/Accounts?offset=0&limit=1");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateErrorAsync(response, ct).ConfigureAwait(false);
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

        throw new PvwaException(HttpStatusCode.OK, null, CoreStrings.PvwaNoToken);
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
            ?? throw new PvwaException(response.StatusCode, null, CoreStrings.PvwaEmptyResponse);
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string relativeUri)
    {
        var token = _token ?? throw new InvalidOperationException(CoreStrings.PvwaNoSession);
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

        DebugLog.Write("pvwa", $"Erreur renvoyée par le PVWA : {text}");
        return new PvwaException(response.StatusCode, code, text, message);
    }

    /// <summary>Page d'une liste du PVWA (<c>value</c>, <c>count</c> = total, <c>nextLink</c>).</summary>
    private sealed class Page<T>
    {
        public List<T>? Value { get; set; }

        public int Count { get; set; }

        public string? NextLink { get; set; }
    }
}
