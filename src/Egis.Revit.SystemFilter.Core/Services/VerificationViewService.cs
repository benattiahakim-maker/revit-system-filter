using System;
using System.Linq;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Abstractions;
using Egis.Revit.SystemFilter.Core.Configuration;
using RvtArgumentException = Autodesk.Revit.Exceptions.ArgumentException;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <inheritdoc cref="IVerificationViewService"/>
    public sealed class VerificationViewService : IVerificationViewService
    {
        private const string DefaultUserPrefix = "USR";

        private readonly CvcFilterOptions _options;

        public VerificationViewService(CvcFilterOptions? options = null)
        {
            _options = options ?? CvcFilterOptions.Default;
        }

        public View DuplicateAndSaveView(
            Document host, View source, string userPrefix, string viewName)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(viewName))
            {
                throw new ArgumentException("Le nom de la vue est obligatoire.", nameof(viewName));
            }

            userPrefix = string.IsNullOrWhiteSpace(userPrefix) ? DefaultUserPrefix : userPrefix.Trim();
            var targetName = $"{userPrefix}_{_options.SavedViewPrefix}_{viewName.Trim()}";

            View duplicated;
            using (var transaction = new Transaction(host, "Enregistrer la vue de vérification"))
            {
                transaction.Start();
                try
                {
                    var duplicatedId = source.Duplicate(ViewDuplicateOption.WithDetailing);
                    duplicated = (View)host.GetElement(duplicatedId);

                    try
                    {
                        duplicated.Name = targetName;
                    }
                    catch (RvtArgumentException)
                    {
                        throw new InvalidOperationException(
                            $"Une vue nommée « {targetName} » existe déjà. Choisissez un autre nom.");
                    }

                    FreezeFilters(host, duplicated, targetName);
                    transaction.Commit();
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }

                    throw;
                }
            }

            return duplicated;
        }

        /// <summary>
        /// Recrée les filtres hérités de la vue source sous des noms propres à la
        /// vue sauvegardée, afin qu'ils ne soient pas écrasés par un filtrage ultérieur.
        /// </summary>
        private void FreezeFilters(Document host, View view, string viewName)
        {
            var inheritedFilters = view.GetFilters()
                .Select(id => host.GetElement(id) as ParameterFilterElement)
                .Where(filter => filter is not null)
                .Cast<ParameterFilterElement>()
                .ToList();

            foreach (var filter in inheritedFilters)
            {
                var rules = RevitFilterFactory.ExtractRules(filter.GetElementFilter());
                if (rules.Count == 0)
                {
                    continue;
                }

                var isReseau = filter.Name.IndexOf("RSX", StringComparison.OrdinalIgnoreCase) >= 0;
                var suffix = isReseau ? _options.SavedReseauFilterSuffix : _options.SavedEquipmentFilterSuffix;
                var frozenName = $"{viewName}_{suffix}";

                var elementFilter = RevitFilterFactory.CreateAndFilter(rules);
                var frozenFilter = ParameterFilterElement.Create(
                    host, frozenName, filter.GetCategories(), elementFilter);

                view.RemoveFilter(filter.Id);
                view.AddFilter(frozenFilter.Id);
                view.SetFilterVisibility(frozenFilter.Id, false);
            }
        }
    }
}
