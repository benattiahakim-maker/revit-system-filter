using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using Egis.Revit.SystemFilter.DesignAutomation.Io;
using Egis.Revit.SystemFilter.DesignAutomation.Models;

namespace Egis.Revit.SystemFilter.DesignAutomation
{
    /// <summary>
    /// Application de base de données headless pour APS Design Automation for Revit.
    /// Contrat (aligné sur system-filter-backend) :
    /// <c>inputZip</c> (dossier <c>input/</c>) + <c>settings.json</c> facultatif →
    /// <c>result/</c> (zippé par Design Automation : systems-inventory.json + run.log).
    /// Aucun document n'est passé par <c>/i</c> : le modèle principal est ouvert ici,
    /// après redirection de ses liens vers le dossier extrait.
    /// </summary>
    public sealed class SystemFilterDbApplication : IExternalDBApplication
    {
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
            e.Succeeded = Run(e.DesignAutomationData.RevitApp, System.IO.Directory.GetCurrentDirectory());
        }

        /// <summary>Exécute le workitem ; run.log est toujours écrit dans result/, même en cas d'échec.</summary>
        public bool Run(Application app, string workingDirectory)
        {
            var ws = new JobWorkspace(workingDirectory);
            Document? document = null;
            try
            {
                ws.Prepare();
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(ws.MainModelFullPath);
                RetargetLinks(modelPath, ws);

                var open = new OpenOptions();
                if (BasicFileInfo.Extract(ws.MainModelFullPath).IsWorkshared)
                {
                    open.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                }
                document = app.OpenDocumentFile(modelPath, open);
                ReportUnloadedLinks(document, ws.Log);

                var json = new SystemInventoryExport().Serialize(document, ws.Settings.ToInventoryOptions(), ws.Log.Warn, ws.Log.Info);
                ws.WriteInventory(json);
                ws.Log.Info($"{ws.Settings.OutputFileName} écrit");
                ws.Log.Info($"terminé : {ws.Log.Warnings} avertissement(s)");
                return true;
            }
            catch (InputArchiveException ex)
            {
                ws.Log.Error($"entrée refusée : {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                // Message nettoyé par RunLog (chemins absolus et secrets masqués).
                ws.Log.Error($"échec : {ex.GetType().Name} : {ex.Message}");
                return false;
            }
            finally
            {
                try { document?.Close(false); } catch (Exception) { /* fermeture best effort */ }
                ws.WriteRunLog();
            }
        }

        /// <summary>
        /// Redirige chaque lien Revit vers un fichier du dossier extrait. Un lien introuvable
        /// dans le ZIP n'est PAS chargé (jamais de chemin hors du dossier) et est signalé dans run.log.
        /// </summary>
        private static void RetargetLinks(ModelPath modelPath, JobWorkspace ws)
        {
            var data = TransmissionData.ReadTransmissionData(modelPath);
            if (data is null) { ws.Log.Info("liens : aucun"); return; }

            var resolver = ws.CreateLinkResolver();
            int found = 0, missing = 0;
            foreach (var id in data.GetAllExternalFileReferenceIds())
            {
                var reference = data.GetLastSavedReferenceData(id);
                if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink) continue;

                var saved = ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetPath());
                var resolved = resolver.Resolve(saved, out var byName);
                if (resolved != null)
                {
                    var target = ModelPathUtils.ConvertUserVisiblePathToModelPath(resolver.FullPath(resolved));
                    data.SetDesiredReferenceData(id, target, PathType.Absolute, true);
                    ws.Log.Info(LinkResolver.ResolvedMessage(resolved, byName));
                    found++;
                }
                else
                {
                    data.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, false);
                    ws.Log.Warn($"lien manquant : {LinkResolver.DisplayName(saved)} (absent du ZIP, non chargé)");
                    missing++;
                }
            }
            data.IsTransmitted = true;
            TransmissionData.WriteTransmissionData(modelPath, data);
            ws.Log.Info($"liens : {found} chargé(s), {missing} manquant(s)");
        }

        private static void ReportUnloadedLinks(Document document, RunLog log)
        {
            foreach (var type in new FilteredElementCollector(document).OfClass(typeof(RevitLinkType)))
            {
                if (!RevitLinkType.IsLoaded(document, type.Id))
                {
                    log.Warn($"lien non chargé après ouverture : {type.Name}");
                }
            }
        }
    }
}
