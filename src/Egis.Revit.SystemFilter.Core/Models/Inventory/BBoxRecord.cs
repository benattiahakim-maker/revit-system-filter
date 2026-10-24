using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>
    /// Boîte englobante alignée sur les axes, en millimètres, dans le repère de la maquette PRINCIPALE
    /// (<c>min</c> et <c>max</c> : [x, y, z], min ≤ max sur chaque axe).
    /// </summary>
    public sealed class BBoxRecord
    {
        [JsonPropertyName("min")]
        public double[] Min { get; set; } = new double[3];

        [JsonPropertyName("max")]
        public double[] Max { get; set; } = new double[3];
    }
}
