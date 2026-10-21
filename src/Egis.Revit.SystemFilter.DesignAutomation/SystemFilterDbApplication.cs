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
    /// S'abonne à l'événement du moteur, lit <c>params.json</c>, applique les filtres
    /// via les services Core et enregistre le document résultat.
    /// </summary>
    public sealed class SystemFilterDbApplication : IExternalDBApplication
    {
        private const string InputFileName = "params.json";
        private const string OutputFileName = "result.rvt";

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
            var workingDirectory = Directory.GetCurrentDirectory();
            ExecuteWorkitem(e.DesignAutomationData.RevitDoc, workingDirectory);
            e.Succeeded = true;
        }

        /// <summary>
        /// Exécute un workitem : lit <c>params.json</c> dans le dossier de travail,
        /// applique les filtres puis enregistre le document résultat.
        /// </summary>
        public void ExecuteWorkitem(Document document, string workingDirectory)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var input = DesignAutomationInput.Load(Path.Combine(workingDirectory, InputFileName));

            new SystemFilterAutomation().Run(document, input);

            var outputPath = Path.Combine(workingDirectory, OutputFileName);
            document.SaveAs(outputPath, new SaveAsOptions { OverwriteExistingFile = true });
        }
    }
}
