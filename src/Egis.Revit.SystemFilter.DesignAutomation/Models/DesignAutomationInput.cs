using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.DesignAutomation.Models
{
    /// <summary>
    /// Paramètres d'entrée de l'activité Design Automation, désérialisés depuis le
    /// fichier JSON fourni par le workitem (équivalent headless de la sélection WPF).
    /// </summary>
    public sealed class DesignAutomationInput
    {
        [JsonPropertyName("systemTypeNames")]
        public List<string> SystemTypeNames { get; set; } = new List<string>();

        [JsonPropertyName("materialNames")]
        public List<string> MaterialNames { get; set; } = new List<string>();

        [JsonPropertyName("worksetNames")]
        public List<string> WorksetNames { get; set; } = new List<string>();

        [JsonPropertyName("includePsyWorksets")]
        public bool IncludePsyWorksets { get; set; }

        [JsonPropertyName("viewName")]
        public string ViewName { get; set; } = "Verif_Systemes";

        [JsonPropertyName("userPrefix")]
        public string UserPrefix { get; set; } = "DA";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>Charge les paramètres depuis un fichier JSON, ou des valeurs par défaut.</summary>
        public static DesignAutomationInput Load(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            {
                return new DesignAutomationInput();
            }

            var json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<DesignAutomationInput>(json, SerializerOptions)
                   ?? new DesignAutomationInput();
        }
    }
}
