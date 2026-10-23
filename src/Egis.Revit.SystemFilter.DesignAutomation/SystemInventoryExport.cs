using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Configuration;
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

        public byte[] Serialize(Document document, SystemInventoryOptions options)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            var inventory = new SystemInventoryService(options).BuildInventory(document);
            return JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions);
        }
    }
}
