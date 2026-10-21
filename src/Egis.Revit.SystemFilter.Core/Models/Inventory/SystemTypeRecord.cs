using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>Type de système (parent racine de la hiérarchie).</summary>
    public sealed class SystemTypeRecord
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("domain")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public NetworkDomain Domain { get; set; }

        /// <summary>Classification du système (ex. SupplyAir, ReturnAir, Hydronic…).</summary>
        [JsonPropertyName("classification")]
        public string? Classification { get; set; }
    }
}
