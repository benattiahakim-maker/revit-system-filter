using System.Net;
using System.Text;

namespace Egis.Revit.SystemFilter.Aps.Tests;

/// <summary>Requête reçue par <see cref="FakeHttpHandler"/>, corps et en-têtes copiés avant libération.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? ContentType, byte[] Body, IReadOnlyDictionary<string, string> Headers)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public string PathAndQuery => Uri.PathAndQuery;

    public override string ToString() => $"{Method} {Uri}";
}

/// <summary>
/// Gestionnaire HTTP simulé : chaque réponse attendue est déclarée dans l'ordre (méthode + prédicat d'URL). Une requête
/// imprévue ou hors séquence fait échouer le test ; aucune requête ne sort de la machine.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpMethod Method, Func<Uri, bool> Match, string Description, Func<RecordedRequest, HttpResponseMessage> Respond)> _expected = new();

    public List<RecordedRequest> Requests { get; } = [];

    public int Pending => _expected.Count;

    public FakeHttpHandler Expect(HttpMethod method, string url, HttpStatusCode status, string? json = null) =>
        Expect(method, uri => uri.AbsoluteUri == url, url, _ => Json(status, json));

    public FakeHttpHandler Expect(HttpMethod method, Func<Uri, bool> match, string description, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _expected.Enqueue((method, match, description, respond));
        return this;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string? json = null) => new(status)
    {
        Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.ToString(),
            body,
            headers);
        Requests.Add(recorded);
        if (_expected.Count == 0)
        {
            throw new InvalidOperationException($"Requête imprévue : {recorded}");
        }

        var (method, match, description, respond) = _expected.Dequeue();
        if (method != request.Method || !match(request.RequestUri!))
        {
            throw new InvalidOperationException($"Attendu {method} {description}, reçu {recorded}");
        }

        return respond(recorded);
    }
}
