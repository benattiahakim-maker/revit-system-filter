using System.Collections.Generic;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Models;

namespace Egis.Revit.SystemFilter.Core.Abstractions
{
    /// <summary>
    /// Fournit, en lecture seule, les listes d'éléments sélectionnables
    /// (types de systèmes, worksets, noms de matériel, occurrences de systèmes).
    /// </summary>
    public interface ISystemCatalogService
    {
        /// <summary>Noms des types de systèmes de gaines du document source.</summary>
        IReadOnlyList<string> GetDuctSystemTypeNames(Document source);

        /// <summary>Noms des worksets utilisateur de la maquette hôte.</summary>
        IReadOnlyList<string> GetUserWorksetNames(Document host);

        /// <summary>Noms de matériel distincts des équipements du document source.</summary>
        IReadOnlyList<string> GetMaterialNames(Document source);

        /// <summary>
        /// Noms des occurrences de systèmes rattachées aux types de systèmes fournis.
        /// </summary>
        IReadOnlyList<string> GetSystemInstanceNames(
            Document source, IEnumerable<string> systemTypeNames);

        /// <summary>
        /// Construit l'inventaire complet : systèmes et matériels depuis le lien
        /// CVC, worksets depuis la maquette hôte.
        /// </summary>
        SystemCatalog BuildCatalog(Document host, Document linkedSource);
    }
}
