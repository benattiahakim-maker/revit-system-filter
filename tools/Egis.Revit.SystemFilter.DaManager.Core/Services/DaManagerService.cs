using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.DaManager.Services;

/// <summary>
/// Opérations de DaManager sur APS. Les méthodes <c>Plan…</c> ne font que des lectures ; les écritures (<c>Publish</c>,
/// <c>Promote</c>, <c>SetAlias</c>, <c>Cleanup</c>) ne sont appelées par le ViewModel qu'après confirmation du plan.
/// </summary>
public sealed class DaManagerService(DesignAutomationClient client, DefinitionLoader definitions, Action<string> log)
{
    private readonly DeploymentService _deployment = new(client, definitions, log);
    private readonly VersionCleanupService _cleanup = new(client, log);

    public DefinitionLoader Definitions => definitions;

    public Task<IReadOnlyList<string>> GetEnginesAsync(CancellationToken cancellationToken = default) => client.GetEnginesAsync(cancellationToken);

    public Task<IReadOnlyList<string>> ListAsync(DaResourceKind kind, CancellationToken cancellationToken = default) => client.ListAsync(kind, cancellationToken);

    public async Task<ResourceState> ReadAsync(string revitVersion, DaResourceKind kind, CancellationToken cancellationToken = default)
    {
        var id = definitions.IdOf(revitVersion, kind);
        var aliases = await client.GetAliasesAsync(kind, id, cancellationToken).ConfigureAwait(false);
        if (aliases is null)
        {
            return new ResourceState(revitVersion, kind, id, false, [], new Dictionary<string, int>());
        }

        var versions = await client.GetVersionsAsync(kind, id, cancellationToken).ConfigureAwait(false);
        return new ResourceState(revitVersion, kind, id, true, [.. versions.Order()], aliases);
    }

    public async Task<IReadOnlyList<ResourceState>> ReadAllAsync(IEnumerable<string> revitVersions, CancellationToken cancellationToken = default)
    {
        var states = new List<ResourceState>();
        foreach (var version in revitVersions)
        {
            states.Add(await ReadAsync(version, DaResourceKind.AppBundle, cancellationToken).ConfigureAwait(false));
            states.Add(await ReadAsync(version, DaResourceKind.Activity, cancellationToken).ConfigureAwait(false));
        }

        return states;
    }

    /// <summary>Plan de publication : ZIP présents et contrôlés localement, état lu sur APS. Aucune écriture.</summary>
    public async Task<OperationPlan> PlanPublishAsync(string nickname, IReadOnlyList<string> revitVersions, string bundleDirectory, string? commit, CancellationToken cancellationToken = default)
    {
        var items = new List<(string, ResourceState, ResourceState, string, long)>();
        foreach (var version in revitVersions)
        {
            var zip = Path.Combine(bundleDirectory, DeploymentService.BundleFileName(version));
            if (!File.Exists(zip))
            {
                throw new ApsException($"{zip} introuvable : lancer d'abord build-appbundle.sh {version}");
            }

            var errors = AppBundleChecker.Check(zip, version);
            if (errors.Count > 0)
            {
                throw new ApsException($"{zip} refusé par le contrôle : " + string.Join("; ", errors));
            }

            definitions.Load(version, DaResourceKind.Activity, nickname, DeploymentService.TestAlias); // gabarits complets
            items.Add((
                version,
                await ReadAsync(version, DaResourceKind.AppBundle, cancellationToken).ConfigureAwait(false),
                await ReadAsync(version, DaResourceKind.Activity, cancellationToken).ConfigureAwait(false),
                zip,
                new FileInfo(zip).Length));
        }

        return PlanBuilder.Publish(nickname, commit, items);
    }

    public Task PublishAsync(string nickname, IReadOnlyList<string> revitVersions, string bundleDirectory, CancellationToken cancellationToken = default) =>
        _deployment.PublishTestAsync(nickname, revitVersions, bundleDirectory, cancellationToken);

    public async Task<OperationPlan> PlanPromoteAsync(string nickname, IReadOnlyList<string> revitVersions, string? commit, CancellationToken cancellationToken = default)
    {
        var items = new List<(string, ResourceState, ResourceState)>();
        foreach (var version in revitVersions)
        {
            definitions.Load(version, DaResourceKind.Activity, nickname, DeploymentService.ProdAlias);
            items.Add((
                version,
                await ReadAsync(version, DaResourceKind.AppBundle, cancellationToken).ConfigureAwait(false),
                await ReadAsync(version, DaResourceKind.Activity, cancellationToken).ConfigureAwait(false)));
        }

        return PlanBuilder.Promote(nickname, commit, items);
    }

    public Task PromoteAsync(string nickname, IReadOnlyList<string> revitVersions, CancellationToken cancellationToken = default) =>
        _deployment.PromoteAsync(nickname, revitVersions, cancellationToken);

    public async Task SetAliasAsync(ResourceState resource, string alias, int version, CancellationToken cancellationToken = default)
    {
        // Relecture : l'état affiché peut dater ; POST ou PATCH selon l'état réel.
        var aliases = await client.GetAliasesAsync(resource.Kind, resource.Id, cancellationToken).ConfigureAwait(false);
        await client.SetAliasAsync(resource.Kind, resource.Id, alias, version, aliases, cancellationToken).ConfigureAwait(false);
        log($"{resource.Id}+{alias} -> v{version}");
    }

    public Task<CleanupPlan> PlanCleanupAsync(ResourceState resource, int keepLatest, IEnumerable<int> protectedVersions, CancellationToken cancellationToken = default) =>
        _cleanup.PlanAsync(resource.Kind, resource.Id, keepLatest, protectedVersions, cancellationToken);

    public Task<IReadOnlyList<int>> CleanupAsync(CleanupPlan plan, CancellationToken cancellationToken = default) =>
        _cleanup.ExecuteAsync(plan, cancellationToken);
}
