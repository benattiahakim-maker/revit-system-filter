using Egis.Revit.SystemFilter.Aps.DesignAutomation;

namespace Egis.Revit.SystemFilter.DaManager.Services;

/// <summary>État d'un AppBundle ou d'une Activity du dépôt sur APS (lecture seule).</summary>
public sealed record ResourceState(
    string RevitVersion,
    DaResourceKind Kind,
    string Id,
    bool Exists,
    IReadOnlyList<int> Versions,
    IReadOnlyDictionary<string, int> Aliases)
{
    /// <summary>Quota APS : 100 versions par type de ressource (docs/PUBLISHING.md section 8).</summary>
    public const int VersionQuota = 100;

    public int? AliasVersion(string alias) => Aliases.TryGetValue(alias, out var version) ? version : null;
}
