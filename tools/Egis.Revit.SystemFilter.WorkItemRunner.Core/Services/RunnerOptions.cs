using System.Security.Cryptography;
using System.Text;
using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>
/// Configuration de WorkItemRunner, lue dans l'environnement : identifiants APS (<see cref="ApsSettings"/>, comme
/// <c>deploy.py</c> et DaManager), <c>EGSF_REPO_ROOT</c> (définitions et schéma), <c>APS_BUCKET</c> (bucket OSS des
/// entrées et sorties) et <c>EGSF_RUNNER_DATA</c> (historique local des lancements).
/// </summary>
public sealed class RunnerOptions
{
    public const string RepositoryRootVariable = "EGSF_REPO_ROOT";
    public const string BucketVariable = "APS_BUCKET";
    public const string DataDirectoryVariable = "EGSF_RUNNER_DATA";
    public const string BucketPrefix = "egsf-workitems-";

    public required ApsSettings Aps { get; init; }
    public string? RepositoryRoot { get; init; }
    public string? BucketKey { get; init; }
    public required string DataDirectory { get; init; }

    public static RunnerOptions FromEnvironment(IReadOnlyDictionary<string, string?>? environment = null, string? startDirectory = null)
    {
        string? Get(string name)
        {
            var value = environment is null ? Environment.GetEnvironmentVariable(name) : environment.GetValueOrDefault(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var aps = ApsSettings.FromEnvironment(environment);
        return new RunnerOptions
        {
            Aps = aps,
            RepositoryRoot = Get(RepositoryRootVariable) ?? FindRepositoryRoot(startDirectory ?? AppContext.BaseDirectory),
            BucketKey = Get(BucketVariable) ?? DefaultBucketKey(aps.ClientId, aps.Nickname),
            DataDirectory = Get(DataDirectoryVariable) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Egis.Revit.SystemFilter", "WorkItemRunner"),
        };
    }

    /// <summary>Premier dossier parent contenant <c>Egis.Revit.SystemFilter.sln</c> et <c>src/Packaging/definitions</c>.</summary>
    public static string? FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Egis.Revit.SystemFilter.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Packaging", "definitions")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Bucket par défaut quand <c>APS_BUCKET</c> est absent. Les clés de bucket sont globales chez APS
    /// (<c>[-_.a-z0-9]{3,128}</c>, POST /oss/v2/buckets) : la clé est dérivée de façon déterministe de l'ID client,
    /// <c>egsf-workitems-</c> suivi des 20 premiers caractères hexadécimaux du SHA-256 de l'ID client (l'ID lui-même
    /// n'apparaît pas). Sans ID client (APS_TOKEN seul), elle dérive du nickname. <c>APS_BUCKET</c> reste recommandé.
    /// </summary>
    public static string? DefaultBucketKey(string? clientId, string? nickname = null)
    {
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientId.Trim()));
            return BucketPrefix + Convert.ToHexString(hash).ToLowerInvariant()[..20];
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            return null;
        }

        var builder = new StringBuilder(BucketPrefix);
        foreach (var c in nickname.Trim().ToLowerInvariant())
        {
            builder.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.' ? c : '-');
        }

        var key = builder.ToString();
        return key.Length > 128 ? key[..128] : key;
    }

    /// <summary>Règle OSS des clés de bucket : <c>[-_.a-z0-9]</c>, 3 à 128 caractères.</summary>
    public static bool IsValidBucketKey(string? key) =>
        key is { Length: >= 3 and <= 128 } && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.');

    /// <summary>Source des identifiants, sans jamais leur valeur.</summary>
    public string CredentialSource =>
        Aps.HasClientCredentials ? $"{ApsSettings.ClientIdVariable} / {ApsSettings.ClientSecretVariable} (OAuth 2-legged)"
        : Aps.HasToken ? $"{ApsSettings.TokenVariable} (jeton fourni)"
        : "aucun identifiant : définir APS_CLIENT_ID et APS_CLIENT_SECRET (ou APS_TOKEN)";

    public static IReadOnlyList<string> RevitVersions => DeploymentService.SupportedVersions;
}
