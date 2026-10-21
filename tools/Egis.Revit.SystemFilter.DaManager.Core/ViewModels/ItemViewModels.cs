using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.DaManager.Mvvm;
using Egis.Revit.SystemFilter.DaManager.Services;

namespace Egis.Revit.SystemFilter.DaManager.ViewModels;

/// <summary>Version Revit cochée ou non pour les publications et promotions.</summary>
public sealed class RevitVersionOption(string version, bool isSelected = true) : ObservableObject
{
    private bool _isSelected = isSelected;

    public string Version { get; } = version;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Ligne du tableau des ressources (AppBundle ou Activity d'une version Revit).</summary>
public sealed class ResourceItemViewModel(ResourceState state)
{
    public ResourceState State { get; } = state;

    public string RevitVersion => State.RevitVersion;

    public string KindLabel => State.Kind == DaResourceKind.AppBundle ? "AppBundle" : "Activity";

    public string Id => State.Id;

    public int VersionCount => State.Versions.Count;

    public string VersionsText => State.Exists ? Compress(State.Versions) : "inexistant";

    public string AliasesText => State.Aliases.Count == 0
        ? "—"
        : string.Join(", ", State.Aliases.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key} → v{a.Value}"));

    /// <summary>Proche du quota APS de 100 versions.</summary>
    public bool IsNearQuota => State.Versions.Count >= ResourceState.VersionQuota - 10;

    /// <summary>« 1-5, 7, 9-10 ».</summary>
    internal static string Compress(IReadOnlyList<int> versions)
    {
        if (versions.Count == 0)
        {
            return "aucune";
        }

        var parts = new List<string>();
        var sorted = versions.Order().ToList();
        var start = sorted[0];
        var previous = start;
        foreach (var version in sorted.Skip(1).Append(int.MinValue))
        {
            if (version == previous + 1)
            {
                previous = version;
                continue;
            }

            parts.Add(start == previous ? $"v{start}" : $"v{start}-v{previous}");
            start = previous = version;
        }

        return string.Join(", ", parts);
    }
}

/// <summary>Entrée du journal des opérations (déjà masquée).</summary>
public sealed record LogEntry(DateTimeOffset Time, string Level, string Message)
{
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
