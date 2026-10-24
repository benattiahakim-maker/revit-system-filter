using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Inventory
{
    /// <summary>
    /// Maquette lue : l'hôte (<c>isHost</c>, transformation identité) ou une instance de lien chargée.
    /// <see cref="Key"/> est l'identifiant unique de la maquette dans le fichier (<c>rooms[].modelKey</c>).
    /// </summary>
    public sealed class ModelRecord
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Title { get; set; }

        [JsonPropertyName("isHost")]
        public bool IsHost { get; set; }

        /// <summary>RevitLinkInstance.GetTotalTransform() (identité pour l'hôte) ; informatif : les boîtes sont déjà ramenées dans le repère de l'hôte.</summary>
        [JsonPropertyName("transform")]
        public TransformRecord Transform { get; set; } = new TransformRecord();
    }

    /// <summary>Transformation rigide : origine en millimètres, bases = vecteurs unitaires.</summary>
    public sealed class TransformRecord
    {
        [JsonPropertyName("origin")]
        public double[] Origin { get; set; } = new double[] { 0, 0, 0 };

        [JsonPropertyName("basisX")]
        public double[] BasisX { get; set; } = new double[] { 1, 0, 0 };

        [JsonPropertyName("basisY")]
        public double[] BasisY { get; set; } = new double[] { 0, 1, 0 };

        [JsonPropertyName("basisZ")]
        public double[] BasisZ { get; set; } = new double[] { 0, 0, 1 };
    }
}
