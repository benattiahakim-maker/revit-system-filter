using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>Espace MEP ou pièce contenant un élément ; <see cref="Source"/> = space | room | linkedRoom.</summary>
    public sealed class RoomRecord
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("number")]
        public string? Number { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
    }
}
