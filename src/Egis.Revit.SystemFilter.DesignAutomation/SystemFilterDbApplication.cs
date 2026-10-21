using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using Egis.Revit.SystemFilter.DesignAutomation.Models;

namespace Egis.Revit.SystemFilter.DesignAutomation
{
    /// <summary>
    /// Application de base de données headless pour APS Design Automation for Revit.
    /// Au démarrage du workitem, exporte l'inventaire des systèmes du modèle en JSON
    /// via les services Core — aucune sélection n'est requise en entrée.
    /// </summary>
    public sealed class SystemFilterDbApplication : IExternalDBApplication
    {
        private const string OptionsFileName = "options.json";

        public ExternalDBApplicationResult OnStartup(ControlledApplication application)
        {
            DesignAutomationBridge.DesignAutomationReadyEvent += OnDesignAutomationReady;
            return ExternalDBApplicationResult.Succeeded;
        }

        public ExternalDBApplicationResult OnShutdown(ControlledApplication application)
        {
            DesignAutomationBridge.DesignAutomationReadyEvent -= OnDesignAutomationReady;
            return ExternalDBApplicationResult.Succeeded;
        }

        private void OnDesignAutomationReady(object? sender, DesignAutomationReadyEventArgs e)
        {
            ExportInventory(e.DesignAutomationData.RevitDoc, Directory.GetCurrentDirectory());
            e.Succeeded = true;
        }

        /// <summary>
        /// Lit les options facultatives (<c>options.json</c>) puis écrit l'inventaire
        /// des systèmes dans le dossier de travail.
        /// </summary>
        public string ExportInventory(Document document, string workingDirectory)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var options = DesignAutomationOptions.Load(Path.Combine(workingDirectory, OptionsFileName));
            return new SystemInventoryExport().Export(document, workingDirectory, options);
        }
    }
}
