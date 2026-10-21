using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>
    /// Élément d'un réseau (gaine, raccord, accessoire, terminal, équipement…).
    /// Référence son système parent via <see cref="SystemId"/>.
    /// </summary>
    public sealed class NetworkElementRecord
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("uniqueId")]
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>Clé stable de catégorie (ex. DuctCurves, DuctFitting, PipeFitting…).</summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("domain")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public NetworkDomain Domain { get; set; }

        [JsonPropertyName("family")]
        public string? Family { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("level")]
        public string? Level { get; set; }

        /// <summary>Dimension lisible (gaines : « 300x200 » ou « Ø200 » ; tuyaux : « DN50 »).</summary>
        [JsonPropertyName("size")]
        public string? Size { get; set; }

        /// <summary>Longueur en millimètres (gaines, tuyaux, flexibles).</summary>
        [JsonPropertyName("lengthMm")]
        public double? LengthMm { get; set; }

        /// <summary>NOM MATERIEL (équipements uniquement).</summary>
        [JsonPropertyName("materiel")]
        public string? Materiel { get; set; }

        /// <summary>EG-CVC_Num CTA (équipements uniquement).</summary>
        [JsonPropertyName("numCta")]
        public string? NumCta { get; set; }

        /// <summary>Clé étrangère vers le système parent.</summary>
        [JsonPropertyName("systemId")]
        public long SystemId { get; set; }
    }
}
