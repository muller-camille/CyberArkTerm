using System.Net;
using System.Text;

namespace CyberArkTerm.Core.Tests;

/// <summary>Faux PVWA : enregistre les requêtes reçues et répond via un délégué.</summary>
internal sealed class FakePvwa : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakePvwa(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<(HttpMethod Method, string PathAndQuery, string? Authorization, string Body)> Requests { get; } = [];

    public List<long?> ContentLengths { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public PvwaClient CreateClient() => new(new Uri("https://pvwa.test/PasswordVault/"), this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ContentLengths.Add(request.Content?.Headers.ContentLength);
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        string? auth = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, auth, body));
        return _respond(request);
    }
}
