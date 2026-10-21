using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>Système (occurrence) ; référence son type parent via <see cref="SystemTypeId"/>.</summary>
    public sealed class SystemRecord
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("uniqueId")]
        public string UniqueId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Clé étrangère vers le type de système parent.</summary>
        [JsonPropertyName("systemTypeId")]
        public long SystemTypeId { get; set; }
    }
}
