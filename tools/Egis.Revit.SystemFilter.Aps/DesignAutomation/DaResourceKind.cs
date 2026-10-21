namespace Egis.Revit.SystemFilter.Aps.DesignAutomation;

/// <summary>Ressource versionnée de l'Automation API.</summary>
public enum DaResourceKind
{
    AppBundle,
    Activity,
}

public static class DaResourceKindExtensions
{
    /// <summary>Segment d'URL (<c>appbundles</c>, <c>activities</c>).</summary>
    public static string PathSegment(this DaResourceKind kind) => kind switch
    {
        DaResourceKind.AppBundle => "appbundles",
        DaResourceKind.Activity => "activities",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Nom du fichier de définition (<c>appbundle</c>, <c>activity</c>) dans <c>src/Packaging/definitions</c>.</summary>
    public static string DefinitionName(this DaResourceKind kind) => kind switch
    {
        DaResourceKind.AppBundle => "appbundle",
        DaResourceKind.Activity => "activity",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
