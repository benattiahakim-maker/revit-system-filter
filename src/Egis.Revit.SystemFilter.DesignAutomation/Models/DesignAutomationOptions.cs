using Egis.Revit.SystemFilter.Core.Configuration;
using Egis.Revit.SystemFilter.DesignAutomation.Io;

namespace Egis.Revit.SystemFilter.DesignAutomation.Models
{
    /// <summary>Passage des réglages (settings.json, sans Revit) aux options du service Core.</summary>
    public static class DesignAutomationOptions
    {
        public static SystemInventoryOptions ToInventoryOptions(this JobSettings settings) => new SystemInventoryOptions
        {
            IncludeDuctSystems = settings.IncludeDuctSystems,
            IncludePipingSystems = settings.IncludePipingSystems,
        };
    }
}
