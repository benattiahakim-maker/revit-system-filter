using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Abstractions
{
    /// <summary>
    /// Parcourt tous les systèmes MEP d'un document et produit un inventaire
    /// normalisé (types, systèmes, éléments reliés par clés). Lecture seule.
    /// </summary>
    public interface ISystemInventoryService
    {
        SystemInventory BuildInventory(Document document);
    }
}
