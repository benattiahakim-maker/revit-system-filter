using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Egis.Revit.SystemFilter.AddIn.Application
{
    /// <summary>
    /// Contexte Revit capturé au lancement de la commande et transmis au ViewModel :
    /// maquette hôte (PSY), maquette CVC liée, document UI et préfixe utilisateur.
    /// </summary>
    public sealed class RevitSessionContext
    {
        public RevitSessionContext(
            UIDocument uiDocument,
            Document hostDocument,
            Document linkedDocument,
            string userPrefix)
        {
            UiDocument = uiDocument;
            HostDocument = hostDocument;
            LinkedDocument = linkedDocument;
            UserPrefix = userPrefix;
        }

        public UIDocument UiDocument { get; }

        public Document HostDocument { get; }

        public Document LinkedDocument { get; }

        public string UserPrefix { get; }
    }
}
