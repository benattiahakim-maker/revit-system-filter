using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Egis.Revit.SystemFilter.Core.Configuration;

namespace Egis.Revit.SystemFilter.DesignAutomation.Models
{
    /// <summary>
    /// Options facultatives du workitem, lues depuis <c>options.json</c> si présent.
    /// En l'absence du fichier, l'export porte sur tous les réseaux.
    /// </summary>
    public sealed class DesignAutomationOptions
    {
        [JsonPropertyName("includeDuctSystems")]
        public bool IncludeDuctSystems { get; set; } = true;

        [JsonPropertyName("includePipingSystems")]
        public bool IncludePipingSystems { get; set; } = true;

        [JsonPropertyName("outputFileName")]
        public string OutputFileName { get; set; } = "systems-inventory.json";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static DesignAutomationOptions Default { get; } = new DesignAutomationOptions();

        public static DesignAutomationOptions Load(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            {
                return new DesignAutomationOptions();
            }

            var json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<DesignAutomationOptions>(json, SerializerOptions)
                   ?? new DesignAutomationOptions();
        }

        public SystemInventoryOptions ToInventoryOptions() => new SystemInventoryOptions
        {
            IncludeDuctSystems = IncludeDuctSystems,
            IncludePipingSystems = IncludePipingSystems,
        };
    }
}
