using System;
using System.Linq;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Configuration;
using Egis.Revit.SystemFilter.Core.Models;
using Egis.Revit.SystemFilter.Core.Services;
using Egis.Revit.SystemFilter.DesignAutomation.Models;

namespace Egis.Revit.SystemFilter.DesignAutomation
{
    /// <summary>
    /// Façade headless : crée une vue 3D de vérification et y applique les filtres
    /// de systèmes à partir de paramètres JSON. Réutilise exactement les mêmes
    /// services Core que l'add-in interactif, sans aucune dépendance UI.
    /// </summary>
    public sealed class SystemFilterAutomation
    {
        private readonly CvcFilterOptions _options;

        public SystemFilterAutomation(CvcFilterOptions? options = null)
        {
            _options = options ?? CvcFilterOptions.Default;
        }

        public FilterApplicationResult Run(Document document, DesignAutomationInput input)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (input is null) throw new ArgumentNullException(nameof(input));

            var linkedDocument = new CvcLinkResolver(_options).ResolveLinkedDocument(document);

            var viewName = $"{input.UserPrefix}_{_options.SavedViewPrefix}_{input.ViewName}";
            var view = CreateIsometricView(document, viewName);

            var request = new SystemFilterRequest(
                input.SystemTypeNames,
                input.MaterialNames,
                input.WorksetNames,
                input.IncludePsyWorksets);

            var filterService = new ViewFilterService(_options);
            return filterService.ApplyFilters(document, linkedDocument, view, input.UserPrefix, request);
        }

        private static View3D CreateIsometricView(Document document, string requestedName)
        {
            var viewFamilyType = new FilteredElementCollector(document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(type => type.ViewFamily == ViewFamily.ThreeDimensional)
                ?? throw new InvalidOperationException("Aucun type de vue 3D n'est disponible dans le document.");

            using var transaction = new Transaction(document, "Créer la vue de vérification");
            transaction.Start();

            var view = View3D.CreateIsometric(document, viewFamilyType.Id);
            view.Name = EnsureUniqueViewName(document, requestedName);

            transaction.Commit();
            return view;
        }

        private static string EnsureUniqueViewName(Document document, string requestedName)
        {
            var existing = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(view => view.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(requestedName))
            {
                return requestedName;
            }

            var index = 1;
            string candidate;
            do
            {
                candidate = $"{requestedName}_{index++}";
            }
            while (existing.Contains(candidate));

            return candidate;
        }
    }
}
