using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>
    /// Fabrique les <see cref="ElementFilter"/> à partir de règles de filtrage.
    /// Remplace l'ancienne méthode <c>CreateElementFilterFromFilterRules</c>.
    /// </summary>
    internal static class RevitFilterFactory
    {
        /// <summary>
        /// Combine les règles en un ET logique. Chaque règle devient un
        /// <see cref="ElementParameterFilter"/>. La liste ne doit pas être vide.
        /// </summary>
        public static ElementFilter CreateAndFilter(IReadOnlyCollection<FilterRule> rules)
        {
            var filters = rules
                .Select(rule => (ElementFilter)new ElementParameterFilter(rule))
                .ToList();

            return new LogicalAndFilter(filters);
        }

        /// <summary>
        /// Aplatit récursivement un <see cref="ElementFilter"/> en la liste des
        /// règles qu'il contient (les ET imbriqués sont équivalents à un ET plat).
        /// </summary>
        public static List<FilterRule> ExtractRules(ElementFilter filter)
        {
            var rules = new List<FilterRule>();
            Collect(filter, rules);
            return rules;
        }

        private static void Collect(ElementFilter filter, List<FilterRule> rules)
        {
            switch (filter)
            {
                case ElementParameterFilter parameterFilter:
                    rules.AddRange(parameterFilter.GetRules());
                    break;
                case ElementLogicalFilter logicalFilter:
                    foreach (var child in logicalFilter.GetFilters())
                    {
                        Collect(child, rules);
                    }
                    break;
            }
        }
    }
}
