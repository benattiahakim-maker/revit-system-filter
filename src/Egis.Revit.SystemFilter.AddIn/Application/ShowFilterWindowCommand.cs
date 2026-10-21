using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Egis.Revit.SystemFilter.AddIn.Infrastructure;
using Egis.Revit.SystemFilter.AddIn.ViewModels;
using Egis.Revit.SystemFilter.AddIn.Views;
using Egis.Revit.SystemFilter.Core.Configuration;
using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.AddIn.Application
{
    /// <summary>
    /// Commande du bouton ruban : prépare le contexte (lien CVC, inventaire,
    /// préfixe utilisateur) et ouvre la fenêtre de vérification non modale.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ShowFilterWindowCommand : IExternalCommand
    {
        private const string DialogTitle = "Egis System Filter";

        private static SystemFilterWindow? _window;

        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var uiApplication = commandData.Application;
            var uiDocument = uiApplication.ActiveUIDocument;
            if (uiDocument is null)
            {
                message = "Aucun document actif.";
                return Result.Cancelled;
            }

            if (_window is { IsLoaded: true })
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var hostDocument = uiDocument.Document;
            var options = CvcFilterOptions.Default;

            var linkedDocument = new CvcLinkResolver(options).ResolveLinkedDocument(hostDocument);
            if (linkedDocument is null)
            {
                TaskDialog.Show(
                    DialogTitle,
                    $"Aucune maquette CVC liée (nom contenant « {options.LinkNameToken} ») " +
                    "n'a été trouvée dans le document actif.");
                return Result.Cancelled;
            }

            var catalogService = new SystemCatalogService(options);
            var catalog = catalogService.BuildCatalog(hostDocument, linkedDocument);

            var context = new RevitSessionContext(
                uiDocument,
                hostDocument,
                linkedDocument,
                BuildUserPrefix(uiApplication.Application.Username));

            var viewModel = new SystemFilterViewModel(
                context,
                catalog,
                new RevitActionDispatcher(),
                new ViewFilterService(options, catalogService),
                new VerificationViewService(options));

            _window = new SystemFilterWindow { DataContext = viewModel };
            new WindowInteropHelper(_window) { Owner = uiApplication.MainWindowHandle };
            _window.Closed += (_, _) => _window = null;
            _window.Show();

            return Result.Succeeded;
        }

        private static string BuildUserPrefix(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return "USR";
            }

            var trimmed = username.Trim();
            var prefix = trimmed.Length >= 3 ? trimmed.Substring(0, 3) : trimmed;
            return prefix.ToUpperInvariant();
        }
    }
}
