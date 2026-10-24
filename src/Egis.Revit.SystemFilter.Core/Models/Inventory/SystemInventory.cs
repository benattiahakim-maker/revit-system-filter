using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>
    /// Export normalisé des réseaux MEP : trois collections à plat
    /// (types, systèmes, éléments) reliées par clés étrangères (enfant → parent).
    /// </summary>
    public sealed class SystemInventory
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public ModelInfo Model { get; set; } = new ModelInfo();

        [JsonPropertyName("summary")]
        public InventorySummary Summary { get; set; } = new InventorySummary();

        /// <summary>Format 1.1 : maquettes lues (hôte puis liens chargés). Omis (null) en 1.0.</summary>
        [JsonPropertyName("models")]
        public List<ModelRecord>? Models { get; set; }

        /// <summary>Format 1.1 : pièces placées de l'hôte et des liens chargés. Omis (null) en 1.0.</summary>
        [JsonPropertyName("rooms")]
        public List<PlacedRoomRecord>? Rooms { get; set; }

        [JsonPropertyName("systemTypes")]
        public List<SystemTypeRecord> SystemTypes { get; set; } = new List<SystemTypeRecord>();

        [JsonPropertyName("systems")]
        public List<SystemRecord> Systems { get; set; } = new List<SystemRecord>();

        [JsonPropertyName("elements")]
        public List<NetworkElementRecord> Elements { get; set; } = new List<NetworkElementRecord>();
    }

    /// <summary>Informations sur la maquette exportée.</summary>
    public sealed class ModelInfo
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("revitVersion")]
        public string? RevitVersion { get; set; }
    }

    /// <summary>Compteurs de synthèse.</summary>
    public sealed class InventorySummary
    {
        [JsonPropertyName("systemTypeCount")]
        public int SystemTypeCount { get; set; }

        [JsonPropertyName("systemCount")]
        public int SystemCount { get; set; }

        [JsonPropertyName("elementCount")]
        public int ElementCount { get; set; }
    }
}
