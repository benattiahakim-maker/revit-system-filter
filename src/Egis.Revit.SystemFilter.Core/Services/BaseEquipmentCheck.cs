using System.Collections.Generic;
using System.Linq;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>
    /// Contrôle sans Revit, appliqué APRÈS la collecte de tous les éléments : un
    /// <c>baseEquipmentId</c> qui ne désigne aucun élément de <c>elements[]</c> (élément filtré,
    /// autre catégorie ou situé dans un lien) est remis à null, avec un avertissement.
    /// </summary>
    public static class BaseEquipmentCheck
    {
        /// <summary>Au-delà de ce nombre de systèmes corrigés, une seule ligne de synthèse.</summary>
        public const int MaxDetailedWarnings = 20;

        /// <summary>Nombre d'exemples cités dans la ligne de synthèse.</summary>
        public const int SummaryExamples = 5;

        /// <summary>
        /// Retourne les avertissements (sans chemin) : un par système corrigé, ou une seule ligne
        /// de synthèse (nombre + 5 exemples) au-delà de <see cref="MaxDetailedWarnings"/>.
        /// </summary>
        public static List<string> Apply(IEnumerable<SystemRecord> systems, IEnumerable<long> elementIds)
        {
            var known = new HashSet<long>(elementIds);
            var warnings = new List<string>();
            foreach (var system in systems)
            {
                if (system.BaseEquipmentId is long id && !known.Contains(id))
                {
                    warnings.Add($"équipement de départ {id} du système {system.Id} ({system.Name}) absent de elements[] : baseEquipmentId = null");
                    system.BaseEquipmentId = null;
                }
            }
            if (warnings.Count > MaxDetailedWarnings)
            {
                var examples = string.Join(" ; ", warnings.Take(SummaryExamples));
                return new List<string>
                {
                    $"{warnings.Count} systèmes avec un équipement de départ absent de elements[] (baseEquipmentId = null) ; exemples : {examples}",
                };
            }
            return warnings;
        }
    }
}
