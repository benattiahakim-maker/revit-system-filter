using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Egis.Revit.SystemFilter.Aps;

/// <summary>
/// OAuth 2-legged (client credentials) sur <c>POST /authentication/v2/token</c> : identifiants en en-tête Basic, jeton
/// mis en cache jusqu'à une minute avant son expiration. Le jeton et le secret ne sont jamais journalisés.
/// </summary>
public sealed class TwoLeggedTokenProvider : IApsTokenProvider
{
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient;
    private readonly string _basicCredentials;
    private readonly string _scope;
    private readonly Uri _tokenUrl;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public TwoLeggedTokenProvider(
        HttpClient httpClient,
        string clientId,
        string clientSecret,
        IEnumerable<string> scopes,
        string tokenUrl = ApsSettings.DefaultAuthenticationUrl,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentException.ThrowIfNullOrEmpty(clientSecret);
        _httpClient = httpClient;
        _basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        _scope = string.Join(' ', scopes);
        _tokenUrl = new Uri(tokenUrl);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is not null && _timeProvider.GetUtcNow() < _expiresAt)
            {
                return _token;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["scope"] = _scope,
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicCredentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ApsException($"Authentification APS (POST /authentication/v2) : HTTP {(int)response.StatusCode} {ApsHttp.Truncate(text)}", response.StatusCode);
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var token = root.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() : null;
            if (string.IsNullOrEmpty(token))
            {
                throw new ApsException("Authentification APS (POST /authentication/v2) : access_token absent de la réponse.");
            }

            var lifetime = root.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromMinutes(5);
            _token = token;
            _expiresAt = _timeProvider.GetUtcNow() + lifetime - RenewalMargin;
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public override string ToString() => $"TwoLeggedTokenProvider {{ Scope = {_scope} }}";
}
