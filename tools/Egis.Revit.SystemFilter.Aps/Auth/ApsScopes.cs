namespace Egis.Revit.SystemFilter.Aps;

/// <summary>Scopes OAuth utilisés par les outils.</summary>
public static class ApsScopes
{
    /// <summary>Automation API (AppBundles, Activities, WorkItems).</summary>
    public const string Code = "code:all";

    /// <summary>OSS : création de bucket, lecture et écriture d'objets (EGR-20).</summary>
    public static readonly IReadOnlyList<string> Oss = ["bucket:create", "bucket:read", "data:read", "data:write", "data:create"];

    public static readonly IReadOnlyList<string> DesignAutomation = [Code];

    public static readonly IReadOnlyList<string> All = [Code, .. Oss];
}
