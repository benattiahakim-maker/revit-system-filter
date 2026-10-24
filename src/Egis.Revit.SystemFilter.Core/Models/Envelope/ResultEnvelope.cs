using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.Core.Models.Envelope
{
    /// <summary>
    /// Résultat natif du plugin (fichier <c>envelope.json</c>) : objets et propriétés prêts à afficher, au format de
    /// l'enveloppe commune du backend (contractVersion 1.0). Sans <c>serviceId</c> ni <c>urn</c> : ce sont des
    /// informations du backend (le service, la conversion de chaque maquette pour le viewer).
    /// </summary>
    public sealed class ResultEnvelope
    {
        public const string ContractVersion = "1.0";
        public const string FileName = "envelope.json";

        [JsonPropertyName("contractVersion")]
        public string Version { get; set; } = ContractVersion;

        /// <summary><c>done</c>, ou <c>partial</c> si <see cref="Warnings"/> n'est pas vide.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = "done";

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Mêmes clés et titres que <c>models[]</c> de l'inventaire ; l'<c>urn</c> est ajouté par le backend.</summary>
        [JsonPropertyName("models")]
        public List<EnvelopeModel> Models { get; set; } = new List<EnvelopeModel>();

        [JsonPropertyName("objects")]
        public List<EnvelopeObject> Objects { get; set; } = new List<EnvelopeObject>();
    }

    public sealed class EnvelopeModel
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
    }

    public sealed class EnvelopeObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("children")]
        public List<EnvelopeObject> Children { get; set; } = new List<EnvelopeObject>();

        [JsonPropertyName("properties")]
        public List<EnvelopeProperty> Properties { get; set; } = new List<EnvelopeProperty>();
    }

    /// <summary>Propriété typée : <c>text | number | boolean | date | box | ref | file</c> (voir docs/frontend-contract.md du backend).</summary>
    public sealed class EnvelopeProperty
    {
        public const string Text = "text";
        public const string Number = "number";
        public const string Box = "box";
        public const string Ref = "ref";

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = Text;

        [JsonPropertyName("value")]
        public object? Value { get; set; }

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        [JsonPropertyName("group")]
        public string? Group { get; set; }
    }
}
