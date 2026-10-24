using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Configuration;
using Egis.Revit.SystemFilter.Core.Models.Inventory;
using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation
{
    /// <summary>
    /// Façade headless : parcourt tous les systèmes du modèle (service Core) et
    /// sérialise l'inventaire normalisé (format 1.0 inchangé). Opération en lecture seule.
    /// </summary>
    public sealed class SystemInventoryExport
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public byte[] Serialize(Document document, SystemInventoryOptions options, Action<string>? warn = null, Action<string>? info = null)
            => Serialize(Build(document, options, warn, info));

        /// <summary>Lit la maquette (lecture seule) et renvoie l'inventaire normalisé.</summary>
        public SystemInventory Build(Document document, SystemInventoryOptions options, Action<string>? warn = null, Action<string>? info = null)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            var service = new SystemInventoryService(options);
            var inventory = service.BuildInventory(document);
            foreach (var line in service.Infos) info?.Invoke(line);
            foreach (var warning in service.Warnings) warn?.Invoke(warning);
            return inventory;
        }

        public static byte[] Serialize(SystemInventory inventory) => JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions);

        /// <summary>Résultat natif (objets, propriétés, références), calculé hors Revit à partir de l'inventaire.</summary>
        public static byte[] SerializeEnvelope(SystemInventory inventory, Action<string>? info = null)
        {
            var analysis = RoomAnalyzer.Analyze(inventory);
            info?.Invoke($"pièces : {analysis.RoomsWithGeometry} avec géométrie, {analysis.ElementsInRooms}/{analysis.AnalyzedElements} élément(s) dans une pièce (tolérance {analysis.ToleranceMm} mm)");
            return JsonSerializer.SerializeToUtf8Bytes(ResultEnvelopeBuilder.Build(inventory, analysis), JsonOptions);
        }
    }
}
