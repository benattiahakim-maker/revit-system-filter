using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Services;
using Egis.Revit.SystemFilter.DesignAutomation.Models;

namespace Egis.Revit.SystemFilter.DesignAutomation
{
    /// <summary>
    /// Façade headless : parcourt tous les systèmes du modèle (service Core) et
    /// écrit l'inventaire normalisé en JSON. Opération en lecture seule.
    /// </summary>
    public sealed class SystemInventoryExport
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public string Export(Document document, string workingDirectory, DesignAutomationOptions? options = null)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));

            options ??= DesignAutomationOptions.Default;

            var inventory = new SystemInventoryService(options.ToInventoryOptions()).BuildInventory(document);

            var outputPath = Path.Combine(workingDirectory, options.OutputFileName);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(inventory, JsonOptions));

            return outputPath;
        }
    }
}
