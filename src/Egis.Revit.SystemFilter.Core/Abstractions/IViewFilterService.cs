using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Models;

namespace Egis.Revit.SystemFilter.Core.Abstractions
{
    /// <summary>
    /// Crée et applique sur une vue les filtres « réseaux » et « équipements »
    /// correspondant à une sélection de systèmes. Ouvre sa propre transaction.
    /// </summary>
    public interface IViewFilterService
    {
        /// <summary>
        /// Remplace les filtres précédents de l'utilisateur sur la vue puis
        /// applique les filtres correspondant à <paramref name="request"/>.
        /// </summary>
        /// <param name="host">Maquette hôte (PSY) contenant la vue.</param>
        /// <param name="linkedSource">Maquette CVC liée (occurrences de systèmes), ou <c>null</c> si absente.</param>
        /// <param name="view">Vue cible des filtres.</param>
        /// <param name="userPrefix">Préfixe identifiant l'utilisateur (ex. « ABE »).</param>
        /// <param name="request">Critères de sélection.</param>
        FilterApplicationResult ApplyFilters(
            Document host,
            Document? linkedSource,
            View view,
            string userPrefix,
            SystemFilterRequest request);
    }
}
