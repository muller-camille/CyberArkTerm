using System.Diagnostics;
using System.Globalization;

namespace ZillaTerm.Core.Diagnostics;

/// <summary>
/// Note chaque requête au PVWA dans le journal de débogage : méthode, adresse, statut, durée, type et taille de la
/// réponse. Jamais les en-têtes (jeton de session) ni les corps (identifiants, fichiers .rdp).
/// </summary>
internal sealed class DebugLogHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!DebugLog.Enabled)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var target = request.RequestUri is { IsAbsoluteUri: true } uri
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped)
            : request.RequestUri?.ToString();
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = response.Content.Headers;
            DebugLog.Write("pvwa", string.Create(CultureInfo.InvariantCulture,
                $"{request.Method} {target} -> {(int)response.StatusCode} {response.ReasonPhrase} ({watch.ElapsedMilliseconds} ms, {content.ContentType?.MediaType ?? "?"}, {content.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "?"} octets)"));
            return response;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            DebugLog.Write("pvwa", string.Create(CultureInfo.InvariantCulture, $"{request.Method} {target} -> échec après {watch.ElapsedMilliseconds} ms"), e);
            throw;
        }
    }
}
