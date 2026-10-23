using System.Collections.Generic;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>
    /// Fusion des parents d'un élément (sans dépendance Revit, testée sous Linux) :
    /// <c>parentIds</c> contient TOUS les systèmes de l'élément, sans doublon, dans
    /// l'ordre de première rencontre.
    /// </summary>
    public static class ParentIdMerge
    {
        /// <summary>Ajoute <paramref name="parentId"/> s'il est absent. Retourne vrai si ajouté.</summary>
        public static bool Add(List<long> parentIds, long parentId)
        {
            if (parentIds.Contains(parentId)) return false;
            parentIds.Add(parentId);
            return true;
        }
    }
}
