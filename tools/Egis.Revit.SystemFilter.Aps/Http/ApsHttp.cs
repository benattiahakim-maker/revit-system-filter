using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.Aps;

/// <summary>Réponse JSON d'un appel APS (équivalent de <c>Response</c> dans <c>deploy.py</c>).</summary>
public sealed record ApsResponse(HttpStatusCode Status, JsonNode? Body);

/// <summary>
/// Transport JSON authentifié (port de la classe <c>Http</c> de <c>deploy.py</c>). Les chemins relatifs sont résolus sur
/// l'URL de base ; une URL absolue est utilisée telle quelle. Le jeton n'est jamais journalisé et les erreurs ne citent que
/// le libellé de la requête.
/// </summary>
internal sealed class ApsHttp
{
    internal const int MaxErrorBodyLength = 500;
    // Corps lisibles et fidèles aux définitions (« + », accents non échappés), comme le texte envoyé par deploy.py une fois décodé.
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _httpClient;
    private readonly IApsTokenProvider _tokenProvider;
    private readonly string _baseUrl;

    public ApsHttp(HttpClient httpClient, IApsTokenProvider tokenProvider, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public Task<ApsResponse> CallAsync(HttpMethod method, string path, JsonNode? body = null, CancellationToken cancellationToken = default) =>
        CallAsync(method, path, body, [HttpStatusCode.OK], cancellationToken);

    public async Task<ApsResponse> CallAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        IReadOnlyCollection<HttpStatusCode> expected,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var url = path.StartsWith("https://", StringComparison.Ordinal) ? path : _baseUrl + path;
        using var request = new HttpRequestMessage(method, url);
        var token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Libellé sans chaîne de requête : jamais de paramètre signé dans un message.
        var label = path.StartsWith("https://", StringComparison.Ordinal) ? "<URL absolue>" : path.Split('?')[0];
        return await SendAsync(_httpClient, request, method.Method, label, expected, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Envoi sans authentification APS (URL signées S3, rapport de WorkItem).</summary>
    internal static async Task<ApsResponse> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        string method,
        string label,
        IReadOnlyCollection<HttpStatusCode> expected,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!expected.Contains(response.StatusCode))
        {
            throw new ApsException($"{method} {label} : HTTP {(int)response.StatusCode} {Truncate(text)}", response.StatusCode);
        }

        JsonNode? body = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                body = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                body = JsonValue.Create(text);
            }
        }

        return new ApsResponse(response.StatusCode, body);
    }

    internal static string Truncate(string text) => text.Length <= MaxErrorBodyLength ? text : text[..MaxErrorBodyLength];

    internal static JsonNode Require(ApsResponse response, string property, string label) =>
        response.Body?[property] ?? throw new ApsException($"{label} : « {property} » absent de la réponse.");
}
