using System.Text.RegularExpressions;

namespace Egis.Revit.SystemFilter.Logging;

/// <summary>
/// Masquage des secrets avant écriture (section 5) : tokens, mots de passe, clés, en-têtes
/// d'autorisation et URL signées (paramètres de requête retirés).
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "***";

    /// <summary>Indique si une clé de donnée désigne un secret (comparaison insensible à la casse, séparateurs ignorés).</summary>
    public static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("_", string.Empty).Replace("-", string.Empty).Replace(".", string.Empty);
        return SensitiveKeyPattern().IsMatch(normalized);
    }

    /// <summary>Masque les secrets reconnaissables dans un texte libre.</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = UrlQueryPattern().Replace(text, m => m.Groups["base"].Value + "?" + Mask);
        result = AuthorizationHeaderPattern().Replace(result, m => m.Groups["prefix"].Value + Mask);
        result = BearerPattern().Replace(result, m => m.Groups["scheme"].Value + " " + Mask);
        result = BasicTokenPattern().Replace(result, m => m.Groups["scheme"].Value + " " + Mask);
        result = JwtPattern().Replace(result, Mask);
        result = KeyValuePattern().Replace(result, m => m.Groups["key"].Value + m.Groups["sep"].Value + Mask);
        return result;
    }

    [GeneratedRegex(@"(token|password|passwd|pwd|secret|apikey|authorization|credential|signature|clientsecret|accesskey|privatekey|cookie|sessionid)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyPattern();

    // URL http(s) avec chaîne de requête (URL signées S3/OSS, jetons en paramètre).
    [GeneratedRegex(@"(?<base>https?://[^\s?""'<>]+)\?[^\s""'<>]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlQueryPattern();

    // En-tête Authorization: <schéma> <valeur> (quel que soit le schéma).
    [GeneratedRegex(@"(?<prefix>\bAuthorization[""']?\s*[:=]\s*[""']?(?:[A-Za-z]+\s+)?)[A-Za-z0-9\-._~+/=]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"(?<scheme>\bBearer)\s+(?!\*\*\*)[A-Za-z0-9\-._~+/=]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    // « Basic » suivi d'un jeton long (base64, 16 caractères min.) ; « Basic Wall » n'est pas masqué.
    [GeneratedRegex(@"(?<scheme>\bBasic)\s+(?=[A-Za-z0-9+/=]{16,})[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BasicTokenPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();

    // clé=valeur ou "clé": "valeur" pour des clés sensibles.
    [GeneratedRegex(@"(?<key>[""']?\b[\w\-]*(?:token|password|passwd|pwd|secret|api[_\-]?key|client[_\-]?secret|access[_\-]?key|credential)[\w\-]*[""']?)(?<sep>\s*[:=]\s*[""']?)[^\s""',;&}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValuePattern();
}
