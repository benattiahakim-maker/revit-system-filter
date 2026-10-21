using System.Collections.Generic;

namespace Egis.Revit.SystemFilter.Core.Models
{
    /// <summary>
    /// Inventaire des éléments sélectionnables présenté à l'utilisateur :
    /// types de systèmes et noms de matériel (issus du lien CVC) et worksets
    /// (issus de la maquette hôte).
    /// </summary>
    public sealed class SystemCatalog
    {
        public SystemCatalog(
            IReadOnlyList<string> systemTypeNames,
            IReadOnlyList<string> worksetNames,
            IReadOnlyList<string> materialNames)
        {
            SystemTypeNames = systemTypeNames;
            WorksetNames = worksetNames;
            MaterialNames = materialNames;
        }

        public IReadOnlyList<string> SystemTypeNames { get; }

        public IReadOnlyList<string> WorksetNames { get; }

        public IReadOnlyList<string> MaterialNames { get; }

        public static SystemCatalog Empty { get; } =
            new SystemCatalog(new string[0], new string[0], new string[0]);
    }
}
