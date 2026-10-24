namespace Egis.Revit.SystemFilter.Core.Configuration
{
    /// <summary>Portée de l'export d'inventaire des réseaux MEP.</summary>
    public sealed class SystemInventoryOptions
    {
        /// <summary>Inclure les systèmes de gaines (CVCD air).</summary>
        public bool IncludeDuctSystems { get; init; } = true;

        /// <summary>Inclure les systèmes de tuyauterie (CVC eau / plomberie).</summary>
        public bool IncludePipingSystems { get; init; } = true;

        /// <summary>Extraire les pièces, les maquettes et les boîtes englobantes (format 1.1). Les conflits sont calculés par le backend.</summary>
        public bool IncludeRooms { get; init; } = true;

        public static SystemInventoryOptions Default { get; } = new SystemInventoryOptions();
    }
}
