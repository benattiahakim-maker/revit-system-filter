using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Abstractions;
using Egis.Revit.SystemFilter.Core.Configuration;
using Egis.Revit.SystemFilter.Core.Models;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <inheritdoc cref="IViewFilterService"/>
    public sealed class ViewFilterService : IViewFilterService
    {
        private const string DefaultUserPrefix = "USR";

        private readonly CvcFilterOptions _options;
        private readonly ISystemCatalogService _catalog;

        public ViewFilterService(
            CvcFilterOptions? options = null,
            ISystemCatalogService? catalog = null)
        {
            _options = options ?? CvcFilterOptions.Default;
            _catalog = catalog ?? new SystemCatalogService(_options);
        }

        public FilterApplicationResult ApplyFilters(
            Document host,
            Document? linkedSource,
            View view,
            string userPrefix,
            SystemFilterRequest request)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            if (view is null) throw new ArgumentNullException(nameof(view));
            if (request is null) throw new ArgumentNullException(nameof(request));

            userPrefix = NormalizePrefix(userPrefix);

            var reseauFilterName = $"{userPrefix}_{_options.ReseauFilterSuffix}";
            var equipmentFilterName = $"{userPrefix}_{_options.EquipmentFilterSuffix}";

            int reseauRules;
            int equipmentRules;

            using (var transaction = new Transaction(host, "Filtrer les systèmes CVC"))
            {
                transaction.Start();
                try
                {
                    RemoveUserFilters(host, view, reseauFilterName, equipmentFilterName);
                    reseauRules = ApplyReseauFilter(host, view, reseauFilterName, request);
                    equipmentRules = ApplyEquipmentFilter(host, linkedSource, view, equipmentFilterName, request);
                    transaction.Commit();
                }
                catch (Exception exception)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }

                    return FilterApplicationResult.Failed(exception.Message);
                }
            }

            return FilterApplicationResult.Ok(reseauRules, equipmentRules);
        }

        private int ApplyReseauFilter(
            Document host, View view, string filterName, SystemFilterRequest request)
        {
            var systemTypeParameter = new ElementId(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);

            var hostSystemTypes = new FilteredElementCollector(host)
                .OfCategory(BuiltInCategory.OST_DuctSystem)
                .WhereElementIsElementType()
                .ToList();

            var rules = new List<FilterRule>();
            foreach (var systemName in request.SystemTypeNames)
            {
                var systemType = hostSystemTypes.FirstOrDefault(
                    type => string.Equals(type.Name, systemName, StringComparison.OrdinalIgnoreCase));

                if (systemType is not null)
                {
                    rules.Add(ParameterFilterRuleFactory.CreateNotEqualsRule(systemTypeParameter, systemType.Id));
                }
            }

            if (rules.Count == 0)
            {
                return 0;
            }

            var elementFilter = CombineWithWorksets(host, RevitFilterFactory.CreateAndFilter(rules), request);
            CreateHiddenFilter(host, view, filterName, _options.ReseauCategories, elementFilter);
            return rules.Count;
        }

        private int ApplyEquipmentFilter(
            Document host,
            Document? linkedSource,
            View view,
            string filterName,
            SystemFilterRequest request)
        {
            var systemParameter = FindParameterId(host, _options.SystemParameterName);
            var ctaParameter = FindParameterId(host, _options.EquipmentCtaParameterName);

            var rules = new List<FilterRule>();

            if (systemParameter != ElementId.InvalidElementId && linkedSource is not null)
            {
                var instanceNames = _catalog.GetSystemInstanceNames(linkedSource, request.SystemTypeNames);
                foreach (var instanceName in instanceNames)
                {
                    rules.Add(ParameterFilterRuleFactory.CreateNotContainsRule(systemParameter, instanceName));
                }
            }

            if (ctaParameter != ElementId.InvalidElementId)
            {
                foreach (var materialName in request.MaterialNames)
                {
                    rules.Add(ParameterFilterRuleFactory.CreateNotEqualsRule(ctaParameter, materialName));
                }
            }

            if (rules.Count == 0)
            {
                return 0;
            }

            var elementFilter = CombineWithWorksets(host, RevitFilterFactory.CreateAndFilter(rules), request);
            CreateHiddenFilter(host, view, filterName, _options.EquipmentCategories, elementFilter);
            return rules.Count;
        }

        /// <summary>
        /// Combine le filtre des systèmes avec celui des worksets PSY (ET logique),
        /// lorsque l'utilisateur a choisi de conserver certains worksets visibles.
        /// </summary>
        private ElementFilter CombineWithWorksets(
            Document host, ElementFilter systemFilter, SystemFilterRequest request)
        {
            if (!request.IncludePsyWorksets || request.WorksetNames.Count == 0)
            {
                return systemFilter;
            }

            var worksetRules = BuildWorksetRules(host, request.WorksetNames);
            if (worksetRules.Count == 0)
            {
                return systemFilter;
            }

            var worksetFilter = RevitFilterFactory.CreateAndFilter(worksetRules);
            return new LogicalAndFilter(worksetFilter, systemFilter);
        }

        private static List<FilterRule> BuildWorksetRules(
            Document host, IReadOnlyList<string> worksetNames)
        {
            var partitionParameter = new ElementId(BuiltInParameter.ELEM_PARTITION_PARAM);

            var worksets = new FilteredWorksetCollector(host)
                .OfKind(WorksetKind.UserWorkset)
                .ToList();

            var rules = new List<FilterRule>();
            foreach (var worksetName in worksetNames)
            {
                var workset = worksets.FirstOrDefault(
                    w => string.Equals(w.Name, worksetName, StringComparison.OrdinalIgnoreCase));

                if (workset is not null)
                {
                    rules.Add(ParameterFilterRuleFactory.CreateNotEqualsRule(
                        partitionParameter, workset.Id.IntegerValue));
                }
            }

            return rules;
        }

        private static void CreateHiddenFilter(
            Document host,
            View view,
            string filterName,
            IReadOnlyList<BuiltInCategory> categories,
            ElementFilter elementFilter)
        {
            var categoryIds = categories.Select(category => new ElementId(category)).ToList();
            var filter = ParameterFilterElement.Create(host, filterName, categoryIds, elementFilter);
            view.AddFilter(filter.Id);
            view.SetFilterVisibility(filter.Id, false);
        }

        private static void RemoveUserFilters(Document host, View view, params string[] filterNames)
        {
            var names = new HashSet<string>(filterNames, StringComparer.OrdinalIgnoreCase);

            var toDelete = view.GetFilters()
                .Select(id => host.GetElement(id) as ParameterFilterElement)
                .Where(filter => filter is not null && names.Contains(filter.Name))
                .Select(filter => filter!.Id)
                .ToList();

            foreach (var id in toDelete)
            {
                host.Delete(id);
            }
        }

        private static ElementId FindParameterId(Document document, string parameterName)
        {
            var parameter = new FilteredElementCollector(document)
                .OfClass(typeof(ParameterElement))
                .Cast<ParameterElement>()
                .FirstOrDefault(element => string.Equals(element.Name, parameterName, StringComparison.Ordinal));

            return parameter?.Id ?? ElementId.InvalidElementId;
        }

        private static string NormalizePrefix(string userPrefix) =>
            string.IsNullOrWhiteSpace(userPrefix) ? DefaultUserPrefix : userPrefix.Trim();
    }
}
