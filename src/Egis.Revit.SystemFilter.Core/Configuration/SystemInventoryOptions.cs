namespace Egis.Revit.SystemFilter.Core.Configuration
{
    /// <summary>Portée de l'export d'inventaire des réseaux MEP.</summary>
    public sealed class SystemInventoryOptions
    {
        /// <summary>Inclure les systèmes de gaines (CVCD air).</summary>
        public bool IncludeDuctSystems { get; init; } = true;

        /// <summary>Inclure les systèmes de tuyauterie (CVC eau / plomberie).</summary>
        public bool IncludePipingSystems { get; init; } = true;

        public static SystemInventoryOptions Default { get; } = new SystemInventoryOptions();
    }
}
