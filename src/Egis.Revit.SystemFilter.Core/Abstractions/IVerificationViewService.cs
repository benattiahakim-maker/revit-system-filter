using Autodesk.Revit.DB;

namespace Egis.Revit.SystemFilter.Core.Abstractions
{
    /// <summary>
    /// Gère la vue 3D de vérification : création et sauvegarde (duplication) de
    /// la vue filtrée sous un nom choisi, avec ses filtres.
    /// </summary>
    public interface IVerificationViewService
    {
        /// <summary>
        /// Duplique <paramref name="source"/>, nomme la copie
        /// « <c>{userPrefix}_{SavedViewPrefix}_{viewName}</c> » et y recrée les
        /// filtres de la vue source sous des noms dédiés. Retourne la nouvelle vue.
        /// </summary>
        View DuplicateAndSaveView(
            Document host,
            View source,
            string userPrefix,
            string viewName);
    }
}
