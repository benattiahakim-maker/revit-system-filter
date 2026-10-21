using System.Globalization;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;

namespace Egis.Revit.SystemFilter.Aps.Packaging;

/// <summary>Raison pour laquelle une version est conservée.</summary>
[Flags]
public enum KeepReason
{
    None = 0,

    /// <summary>Parmi les N versions les plus récentes.</summary>
    Latest = 1,

    /// <summary>Référencée par au moins un alias (test, prod…) : jamais supprimée.</summary>
    Aliased = 2,

    /// <summary>Protégée explicitement (versions notées au journal de publication, docs/PUBLISHING.md section 8).</summary>
    Protected = 4,
}

/// <summary>Version conservée et ses raisons ; <see cref="Aliases"/> liste les alias qui la référencent.</summary>
public sealed record KeptVersion(int Version, KeepReason Reasons, IReadOnlyList<string> Aliases);

/// <summary>Plan de nettoyage d'un AppBundle ou d'une Activity. Rien n'est supprimé tant que le plan n'est pas exécuté.</summary>
public sealed record CleanupPlan(DaResourceKind Kind, string Id, int KeepLatest, IReadOnlyList<int> ToDelete, IReadOnlyList<KeptVersion> Kept)
{
    public bool IsEmpty => ToDelete.Count == 0;

    /// <summary>Description lisible du plan (affichée avant confirmation).</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string>
        {
            $"{Kind.PathSegment()}/{Id} : {ToDelete.Count + Kept.Count} version(s), {Kept.Count} conservée(s), {ToDelete.Count} à supprimer.",
        };
        foreach (var kept in Kept)
        {
            var reasons = new List<string>();
            if (kept.Reasons.HasFlag(KeepReason.Aliased))
            {
                reasons.Add("alias " + string.Join(", ", kept.Aliases));
            }

            if (kept.Reasons.HasFlag(KeepReason.Latest))
            {
                reasons.Add($"parmi les {KeepLatest} plus récentes");
            }

            if (kept.Reasons.HasFlag(KeepReason.Protected))
            {
                reasons.Add("protégée (journal)");
            }

            lines.Add($"  conserver v{kept.Version} ({string.Join(" ; ", reasons)})");
        }

        lines.AddRange(ToDelete.Select(v => $"  SUPPRIMER v{v} : DELETE /{Kind.PathSegment()}/{Id}/versions/{v.ToString(CultureInfo.InvariantCulture)}"));
        return lines;
    }
}

/// <summary>
/// Planification pure du nettoyage des versions (quota APS : 100 versions d'AppBundles et 100 d'Activities, voir
/// docs/PUBLISHING.md section 8). Règles : une version référencée par un alias n'est JAMAIS supprimée ; les
/// <c>keepLatest</c> versions les plus récentes (au moins 1) et les versions protégées sont conservées.
/// </summary>
public static class VersionCleanupPlanner
{
    public static CleanupPlan Plan(
        DaResourceKind kind,
        string id,
        IEnumerable<int> versions,
        IReadOnlyDictionary<string, int> aliases,
        int keepLatest,
        IEnumerable<int>? protectedVersions = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keepLatest, 1);
        ArgumentNullException.ThrowIfNull(aliases);
        var all = versions.Distinct().OrderDescending().ToList();
        var latest = all.Take(keepLatest).ToHashSet();
        var protectedSet = (protectedVersions ?? []).ToHashSet();
        var aliasByVersion = aliases
            .GroupBy(a => a.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(a => a.Key).Order(StringComparer.Ordinal).ToList());

        var kept = new List<KeptVersion>();
        var toDelete = new List<int>();
        foreach (var version in all.Order())
        {
            var reasons = KeepReason.None;
            if (latest.Contains(version))
            {
                reasons |= KeepReason.Latest;
            }

            if (aliasByVersion.ContainsKey(version))
            {
                reasons |= KeepReason.Aliased;
            }

            if (protectedSet.Contains(version))
            {
                reasons |= KeepReason.Protected;
            }

            if (reasons == KeepReason.None)
            {
                toDelete.Add(version);
            }
            else
            {
                kept.Add(new KeptVersion(version, reasons, aliasByVersion.GetValueOrDefault(version) ?? []));
            }
        }

        return new CleanupPlan(kind, id, keepLatest, toDelete, kept);
    }
}

/// <summary>
/// Lecture de l'état et exécution d'un <see cref="CleanupPlan"/> déjà confirmé. Avant de supprimer, les alias sont relus :
/// si une version du plan est devenue référencée (ou a disparu des versions), rien n'est supprimé.
/// </summary>
public sealed class VersionCleanupService(DesignAutomationClient client, Action<string>? log = null)
{
    private readonly Action<string> _log = log ?? (_ => { });

    /// <summary>Lit versions et alias puis calcule le plan (lecture seule).</summary>
    public async Task<CleanupPlan> PlanAsync(DaResourceKind kind, string id, int keepLatest, IEnumerable<int>? protectedVersions = null, CancellationToken cancellationToken = default)
    {
        var aliases = await client.GetAliasesAsync(kind, id, cancellationToken).ConfigureAwait(false)
            ?? throw new ApsException($"{kind.PathSegment()}/{id} : ressource inexistante");
        var versions = await client.GetVersionsAsync(kind, id, cancellationToken).ConfigureAwait(false);
        return VersionCleanupPlanner.Plan(kind, id, versions, aliases, keepLatest, protectedVersions);
    }

    /// <summary>Supprime les versions du plan, après revérification des alias. Retourne les versions supprimées.</summary>
    public async Task<IReadOnlyList<int>> ExecuteAsync(CleanupPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsEmpty)
        {
            return [];
        }

        var aliases = await client.GetAliasesAsync(plan.Kind, plan.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new ApsException($"{plan.Kind.PathSegment()}/{plan.Id} : ressource inexistante");
        var aliased = plan.ToDelete.Where(v => aliases.Values.Contains(v)).ToList();
        if (aliased.Count > 0)
        {
            throw new ApsException($"{plan.Kind.PathSegment()}/{plan.Id} : version(s) {string.Join(", ", aliased.Select(v => "v" + v))} référencée(s) par un alias depuis le plan ; rien n'a été supprimé.");
        }

        var deleted = new List<int>();
        foreach (var version in plan.ToDelete)
        {
            await client.DeleteVersionAsync(plan.Kind, plan.Id, version, cancellationToken).ConfigureAwait(false);
            deleted.Add(version);
            _log($"{plan.Id} : v{version} supprimée");
        }

        return deleted;
    }
}
