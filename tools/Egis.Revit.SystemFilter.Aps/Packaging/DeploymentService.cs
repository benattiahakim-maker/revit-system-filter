using Egis.Revit.SystemFilter.Aps.DesignAutomation;

namespace Egis.Revit.SystemFilter.Aps.Packaging;

/// <summary>
/// Publication des AppBundles et Activities : port fidèle des commandes <c>test</c>, <c>promote</c>, <c>rollback</c> et
/// <c>status</c> de <c>src/Packaging/scripts/deploy.py</c> (même séquence d'appels, mêmes messages). Le journal ne reçoit
/// ni jeton ni URL signée.
/// </summary>
public sealed class DeploymentService
{
    public const string TestAlias = "test";
    public const string ProdAlias = "prod";
    public static readonly IReadOnlyList<string> SupportedVersions = ["2025", "2026", "2027"];

    private readonly DesignAutomationClient _client;
    private readonly DefinitionLoader _definitions;
    private readonly Action<string> _log;
    private readonly Func<string, string, IReadOnlyList<string>> _checkBundle;

    /// <param name="checkBundle">Contrôle du ZIP (chemin, version) ; défaut <see cref="AppBundleChecker.Check(string, string)"/>.</param>
    public DeploymentService(
        DesignAutomationClient client,
        DefinitionLoader definitions,
        Action<string>? log = null,
        Func<string, string, IReadOnlyList<string>>? checkBundle = null)
    {
        _client = client;
        _definitions = definitions;
        _log = log ?? (_ => { });
        _checkBundle = checkBundle ?? AppBundleChecker.Check;
    }

    /// <summary>Nom du ZIP produit par <c>build-appbundle.sh</c>.</summary>
    public static string BundleFileName(string revitVersion) => $"Egis.Revit.SystemFilter_R{revitVersion}.zip";

    /// <summary>
    /// Commande <c>test</c> : engines vérifiés, puis pour chaque version ZIP contrôlé, nouvelle version d'AppBundle,
    /// téléversement, alias <c>test</c>, nouvelle version d'Activity (AppBundle <c>+test</c>) et alias <c>test</c>.
    /// </summary>
    public async Task PublishTestAsync(string nickname, IReadOnlyList<string> versions, string bundleDirectory, CancellationToken cancellationToken = default)
    {
        await _client.EnsureRevitEnginesAsync(versions, cancellationToken).ConfigureAwait(false);
        foreach (var version in versions)
        {
            var zipPath = Path.Combine(bundleDirectory, BundleFileName(version));
            if (!File.Exists(zipPath))
            {
                throw new ApsException($"{zipPath} introuvable : lancer d'abord build-appbundle.sh {version}");
            }

            var errors = _checkBundle(zipPath, version);
            if (errors.Count > 0)
            {
                throw new ApsException($"{zipPath} refusé par le contrôle : " + string.Join("; ", errors));
            }

            var bundle = _definitions.Load(version, DaResourceKind.AppBundle);
            var bundleId = bundle["id"]!.GetValue<string>();
            var existing = await _client.GetAliasesAsync(DaResourceKind.AppBundle, bundleId, cancellationToken).ConfigureAwait(false);
            var created = existing is null
                ? await _client.CreateAppBundleAsync(bundle, cancellationToken).ConfigureAwait(false)
                : await _client.CreateAppBundleVersionAsync(bundleId, bundle, cancellationToken).ConfigureAwait(false);
            await _client.UploadAppBundleAsync(created.UploadParameters, zipPath, cancellationToken).ConfigureAwait(false);
            await _client.SetAliasAsync(DaResourceKind.AppBundle, bundleId, TestAlias, created.Version, existing, cancellationToken).ConfigureAwait(false);

            var activity = _definitions.Load(version, DaResourceKind.Activity, nickname, TestAlias);
            var activityId = activity["id"]!.GetValue<string>();
            var existingActivity = await _client.GetAliasesAsync(DaResourceKind.Activity, activityId, cancellationToken).ConfigureAwait(false);
            var activityVersion = existingActivity is null
                ? await _client.CreateActivityAsync(activity, cancellationToken).ConfigureAwait(false)
                : await _client.CreateActivityVersionAsync(activityId, activity, cancellationToken).ConfigureAwait(false);
            await _client.SetAliasAsync(DaResourceKind.Activity, activityId, TestAlias, activityVersion, existingActivity, cancellationToken).ConfigureAwait(false);
            _log($"Revit {version} : {nickname}.{bundleId}+{TestAlias} -> v{created.Version}, {nickname}.{activityId}+{TestAlias} -> v{activityVersion}");
        }
    }

    /// <summary>
    /// Commande <c>promote</c> : alias <c>prod</c> de l'AppBundle sur la version en <c>test</c>, nouvelle version d'Activity
    /// (AppBundle <c>+prod</c>) et alias <c>prod</c>. Les versions <c>prod</c> précédentes sont journalisées (retour arrière).
    /// </summary>
    public async Task PromoteAsync(string nickname, IReadOnlyList<string> versions, CancellationToken cancellationToken = default)
    {
        foreach (var version in versions)
        {
            var bundleId = _definitions.IdOf(version, DaResourceKind.AppBundle);
            var current = await _client.GetAliasesAsync(DaResourceKind.AppBundle, bundleId, cancellationToken).ConfigureAwait(false) ?? new Dictionary<string, int>();
            if (!current.TryGetValue(TestAlias, out var testVersion))
            {
                throw new ApsException($"{bundleId} : aucun alias {TestAlias} à promouvoir");
            }

            int? previousBundle = current.TryGetValue(ProdAlias, out var pb) ? pb : null;
            await _client.SetAliasAsync(DaResourceKind.AppBundle, bundleId, ProdAlias, testVersion, current, cancellationToken).ConfigureAwait(false);

            var activity = _definitions.Load(version, DaResourceKind.Activity, nickname, ProdAlias);
            var activityId = activity["id"]!.GetValue<string>();
            var currentActivity = await _client.GetAliasesAsync(DaResourceKind.Activity, activityId, cancellationToken).ConfigureAwait(false) ?? new Dictionary<string, int>();
            int? previousActivity = currentActivity.TryGetValue(ProdAlias, out var pa) ? pa : null;
            var created = await _client.CreateActivityVersionAsync(activityId, activity, cancellationToken).ConfigureAwait(false);
            await _client.SetAliasAsync(DaResourceKind.Activity, activityId, ProdAlias, created, currentActivity, cancellationToken).ConfigureAwait(false);
            _log($"Revit {version} : {bundleId}+{ProdAlias} -> v{testVersion} (avant : v{Show(previousBundle)}), " +
                 $"{activityId}+{ProdAlias} -> v{created} (avant : v{Show(previousActivity)})");
            if (previousBundle is null || previousActivity is null)
            {
                _log("  Première promotion : pas de version prod antérieure (retour arrière impossible par alias).");
            }
            else
            {
                _log($"  Retour arrière : rollback --kind appbundle --revit {version} --to-version {previousBundle} ; " +
                     $"rollback --kind activity --revit {version} --to-version {previousActivity}");
            }
        }
    }

    /// <summary>Commande <c>rollback</c> : repointe un alias (prod par défaut) sur une version antérieure.</summary>
    public async Task RollbackAsync(DaResourceKind kind, string version, int toVersion, string alias = ProdAlias, CancellationToken cancellationToken = default)
    {
        var itemId = _definitions.IdOf(version, kind);
        await _client.UpdateAliasAsync(kind, itemId, alias, toVersion, cancellationToken).ConfigureAwait(false);
        _log($"{itemId}+{alias} -> v{toVersion}");
    }

    /// <summary>Commande <c>status</c> (lecture seule) : alias de chaque AppBundle et Activity, null si inexistant.</summary>
    public async Task<IReadOnlyList<(string Version, DaResourceKind Kind, string Id, IReadOnlyDictionary<string, int>? Aliases)>> StatusAsync(
        IReadOnlyList<string> versions,
        CancellationToken cancellationToken = default)
    {
        var result = new List<(string, DaResourceKind, string, IReadOnlyDictionary<string, int>?)>();
        foreach (var version in versions)
        {
            foreach (var kind in new[] { DaResourceKind.AppBundle, DaResourceKind.Activity })
            {
                var itemId = _definitions.IdOf(version, kind);
                var aliases = await _client.GetAliasesAsync(kind, itemId, cancellationToken).ConfigureAwait(false);
                result.Add((version, kind, itemId, aliases));
                _log($"{itemId} : {(aliases is null ? "None" : "{" + string.Join(", ", aliases.Select(a => $"'{a.Key}': {a.Value}")) + "}")}");
            }
        }

        return result;
    }

    private static string Show(int? version) => version?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "None";
}
