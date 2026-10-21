using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.DesignAutomation.Models;

namespace Egis.Revit.SystemFilter.DesignAutomation
{
    /// <summary>
    /// Application de base de données headless pour APS Design Automation.
    ///
    /// Phase 2 : dans <see cref="OnStartup"/>, s'abonner à
    /// <c>DesignAutomationBridge.DesignAutomationReadyEvent</c> (fourni par le
    /// SDK Autodesk DA) et router l'événement vers <see cref="ExecuteWorkitem"/>.
    /// La logique métier, elle, est déjà complète et partagée via
    /// <see cref="SystemFilterAutomation"/>.
    /// </summary>
    public sealed class SystemFilterDbApplication : IExternalDBApplication
    {
        private const string InputFileName = "params.json";
        private const string OutputFileName = "result.rvt";

        public ExternalDBApplicationResult OnStartup(ControlledApplication application)
        {
            // Phase 2 :
            // DesignAutomationBridge.DesignAutomationReadyEvent += OnDesignAutomationReady;
            return ExternalDBApplicationResult.Succeeded;
        }

        public ExternalDBApplicationResult OnShutdown(ControlledApplication application)
        {
            return ExternalDBApplicationResult.Succeeded;
        }

        /// <summary>
        /// Exécute un workitem : lit <c>params.json</c> dans le dossier de travail,
        /// applique les filtres puis enregistre le document résultat.
        /// Appelé par le handler Design Automation (phase 2).
        /// </summary>
        public void ExecuteWorkitem(Document document, string workingDirectory)
        {
            var input = DesignAutomationInput.Load(Path.Combine(workingDirectory, InputFileName));

            new SystemFilterAutomation().Run(document, input);

            var outputPath = Path.Combine(workingDirectory, OutputFileName);
            document.SaveAs(outputPath, new SaveAsOptions { OverwriteExistingFile = true });
        }
    }
}
