using Autodesk.Revit.DB;

namespace Egis.Revit.SystemFilter.Core.Abstractions
{
    /// <summary>
    /// Localise le document de la maquette CVC chargée en lien dans la maquette hôte.
    /// </summary>
    public interface ICvcLinkResolver
    {
        /// <summary>
        /// Retourne le document lié dont le nom contient le jeton configuré, ou
        /// <c>null</c> si aucun lien correspondant n'est chargé.
        /// </summary>
        Document? ResolveLinkedDocument(Document host);
    }
}
