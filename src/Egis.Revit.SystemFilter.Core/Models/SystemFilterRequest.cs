using System;
using System.Collections.Generic;
using System.Linq;

namespace Egis.Revit.SystemFilter.Core.Models
{
    /// <summary>
    /// Décrit ce que l'utilisateur veut afficher dans la vue : les types de
    /// systèmes, les noms de matériel et, optionnellement, les worksets PSY à
    /// conserver visibles. Objet d'entrée partagé par le mode interactif (WPF)
    /// et le mode headless (Design Automation).
    /// </summary>
    public sealed class SystemFilterRequest
    {
        public SystemFilterRequest(
            IEnumerable<string>? systemTypeNames = null,
            IEnumerable<string>? materialNames = null,
            IEnumerable<string>? worksetNames = null,
            bool includePsyWorksets = false)
        {
            SystemTypeNames = Clean(systemTypeNames);
            MaterialNames = Clean(materialNames);
            WorksetNames = Clean(worksetNames);
            IncludePsyWorksets = includePsyWorksets;
        }

        /// <summary>Types de systèmes de gaines sélectionnés.</summary>
        public IReadOnlyList<string> SystemTypeNames { get; }

        /// <summary>Noms de matériel d'équipement sélectionnés.</summary>
        public IReadOnlyList<string> MaterialNames { get; }

        /// <summary>Worksets PSY à conserver visibles (combinés en ET logique).</summary>
        public IReadOnlyList<string> WorksetNames { get; }

        /// <summary>
        /// Lorsque vrai, les éléments des worksets PSY sélectionnés restent visibles
        /// en plus des systèmes choisis (comportement « Afficher Éléments PSY »).
        /// </summary>
        public bool IncludePsyWorksets { get; }

        /// <summary>Vrai si aucun critère n'a été fourni.</summary>
        public bool IsEmpty =>
            SystemTypeNames.Count == 0 && MaterialNames.Count == 0;

        private static IReadOnlyList<string> Clean(IEnumerable<string>? values) =>
            (values ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
