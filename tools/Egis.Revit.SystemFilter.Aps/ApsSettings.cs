namespace Egis.Revit.SystemFilter.Aps;

/// <summary>
/// Paramètres APS lus dans les variables d'environnement. Mêmes noms que <c>deploy.py</c> (<c>APS_TOKEN</c>,
/// <c>APS_NICKNAME</c>, <c>APS_DA_BASE_URL</c>), complétés par <c>APS_CLIENT_ID</c> / <c>APS_CLIENT_SECRET</c> (EGR-19).
/// Les secrets ne sont jamais restitués par <see cref="ToString"/>.
/// </summary>
public sealed class ApsSettings
{
    public const string ClientIdVariable = "APS_CLIENT_ID";
    public const string ClientSecretVariable = "APS_CLIENT_SECRET";
    public const string TokenVariable = "APS_TOKEN";
    public const string NicknameVariable = "APS_NICKNAME";
    public const string DaBaseUrlVariable = "APS_DA_BASE_URL";

    public const string DefaultDaBaseUrl = "https://developer.api.autodesk.com/da/us-east/v3";
    public const string DefaultAuthenticationUrl = "https://developer.api.autodesk.com/authentication/v2/token";
    public const string DefaultOssBaseUrl = "https://developer.api.autodesk.com/oss/v2";

    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }

    /// <summary>Jeton 2-legged fourni tel quel (secours, comme <c>deploy.py</c>).</summary>
    public string? Token { get; init; }

    public string? Nickname { get; init; }
    public string DaBaseUrl { get; init; } = DefaultDaBaseUrl;
    public string AuthenticationUrl { get; init; } = DefaultAuthenticationUrl;
    public string OssBaseUrl { get; init; } = DefaultOssBaseUrl;

    public bool HasClientCredentials => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
    public bool HasToken => !string.IsNullOrEmpty(Token);

    /// <summary>Identifiants utilisables : couple ID client / secret, ou jeton.</summary>
    public bool HasCredentials => HasClientCredentials || HasToken;

    /// <summary>Message unique quand aucun identifiant n'est disponible (noms des variables seulement).</summary>
    public const string MissingCredentialsMessage = $"{ClientIdVariable} et {ClientSecretVariable} (ou {TokenVariable}) sont obligatoires.";

    /// <summary>Variables d'identification manquantes (noms seulement, jamais de valeur) ; vide si les identifiants suffisent.</summary>
    public IReadOnlyList<string> MissingCredentialVariables()
    {
        if (HasCredentials)
        {
            return [];
        }

        var alternative = $" (ou {TokenVariable})";
        return string.IsNullOrEmpty(ClientId) == string.IsNullOrEmpty(ClientSecret)
            ? [ClientIdVariable, ClientSecretVariable + alternative]
            : [(string.IsNullOrEmpty(ClientId) ? ClientIdVariable : ClientSecretVariable) + alternative];
    }

    /// <summary>Lit les variables d'environnement du processus, ou le dictionnaire fourni (tests).</summary>
    public static ApsSettings FromEnvironment(IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Get(string name)
        {
            var value = environment is null ? Environment.GetEnvironmentVariable(name) : environment.GetValueOrDefault(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return new ApsSettings
        {
            ClientId = Get(ClientIdVariable),
            ClientSecret = Get(ClientSecretVariable),
            Token = Get(TokenVariable),
            Nickname = Get(NicknameVariable),
            DaBaseUrl = Get(DaBaseUrlVariable) ?? DefaultDaBaseUrl,
        };
    }

    /// <summary>
    /// Fournisseur de jeton : identifiants client en priorité (OAuth 2-legged), sinon <c>APS_TOKEN</c>.
    /// </summary>
    /// <exception cref="ApsException">Aucun identifiant disponible.</exception>
    public IApsTokenProvider CreateTokenProvider(HttpClient httpClient, IEnumerable<string>? scopes = null, TimeProvider? timeProvider = null)
    {
        if (HasClientCredentials)
        {
            return new TwoLeggedTokenProvider(httpClient, ClientId!, ClientSecret!, scopes ?? ApsScopes.All, AuthenticationUrl, timeProvider);
        }

        if (HasToken)
        {
            return new StaticTokenProvider(Token!);
        }

        throw new ApsException(MissingCredentialsMessage);
    }

    /// <summary>Nickname obligatoire pour qualifier les identifiants d'Activity.</summary>
    public string RequireNickname() =>
        Nickname ?? throw new ApsException($"{NicknameVariable} est obligatoire.");

    public override string ToString() =>
        $"ApsSettings {{ ClientId = {(ClientId is null ? "<absent>" : "***")}, ClientSecret = {(ClientSecret is null ? "<absent>" : "***")}, " +
        $"Token = {(Token is null ? "<absent>" : "***")}, Nickname = {Nickname ?? "<absent>"}, DaBaseUrl = {DaBaseUrl} }}";
}
