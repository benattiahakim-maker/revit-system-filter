using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>
    /// Pièce placée (Area &gt; 0) de l'hôte ou d'un lien chargé, pour <c>rooms[]</c> (format 1.1).
    /// Ne pas confondre avec <see cref="RoomRecord"/> (pièce ou espace d'UN élément, <c>elements[].room</c>).
    /// Les valeurs absentes sont écrites <c>null</c>. Unités : mm, m², m³.
    /// </summary>
    public sealed class PlacedRoomRecord
    {
        /// <summary>Unique dans le fichier, attribué par le plugin (les ids Revit peuvent collisionner entre maquettes liées).</summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("revitId")]
        public long RevitId { get; set; }

        [JsonPropertyName("uniqueId")]
        public string UniqueId { get; set; } = string.Empty;

        [JsonPropertyName("modelKey")]
        public string ModelKey { get; set; } = string.Empty;

        [JsonPropertyName("number")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Number { get; set; }

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Name { get; set; }

        [JsonPropertyName("level")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Level { get; set; }

        /// <summary>m².</summary>
        [JsonPropertyName("area")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double? Area { get; set; }

        /// <summary>m³ ; null si Revit ne l'a pas calculé.</summary>
        [JsonPropertyName("volume")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double? Volume { get; set; }

        [JsonPropertyName("phase")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Phase { get; set; }

        [JsonPropertyName("department")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Department { get; set; }

        /// <summary>Hauteur non contrainte, mm.</summary>
        [JsonPropertyName("height")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double? Height { get; set; }

        [JsonPropertyName("perimeter")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double? Perimeter { get; set; }

        [JsonPropertyName("baseOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double? BaseOffset { get; set; }

        [JsonPropertyName("limitOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double? LimitOffset { get; set; }

        [JsonPropertyName("upperLimit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? UpperLimit { get; set; }

        [JsonPropertyName("occupancy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Occupancy { get; set; }

        [JsonPropertyName("comments")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Comments { get; set; }

        [JsonPropertyName("baseFinish")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? BaseFinish { get; set; }

        [JsonPropertyName("ceilingFinish")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? CeilingFinish { get; set; }

        [JsonPropertyName("floorFinish")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? FloorFinish { get; set; }

        [JsonPropertyName("wallFinish")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? WallFinish { get; set; }

        /// <summary>Boîte alignée sur les axes (mm, repère de l'hôte) ; null si la pièce n'a pas de géométrie.</summary>
        [JsonPropertyName("bbox")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public BBoxRecord? BBox { get; set; }
    }
}
