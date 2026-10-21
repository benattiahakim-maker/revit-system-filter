using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Egis.Revit.SystemFilter.Core.Configuration
{
    /// <summary>
    /// Regroupe tous les paramètres configurables de l'outil (noms de paramètres
    /// partagés, conventions de nommage des filtres et des vues, catégories ciblées).
    /// Remplace les valeurs qui étaient codées en dur dans l'ancien plugin.
    /// </summary>
    public sealed class CvcFilterOptions
    {
        /// <summary>
        /// Fragment de nom permettant d'identifier le lien de la maquette CVC
        /// parmi les liens Revit chargés dans la maquette hôte (PSY).
        /// </summary>
        public string LinkNameToken { get; init; } = "0100-CVC";

        /// <summary>Nom du paramètre partagé portant le système CVC sur les équipements.</summary>
        public string SystemParameterName { get; init; } = "EG_CVC_SYSTEME";

        /// <summary>Nom du paramètre partagé portant le numéro de CTA sur les équipements.</summary>
        public string EquipmentCtaParameterName { get; init; } = "EG-CVC_Num CTA";

        /// <summary>Nom du paramètre portant le nom de matériel sur les équipements.</summary>
        public string MaterialNameParameterName { get; init; } = "NOM MATERIEL";

        /// <summary>Suffixe du filtre « réseaux » (gaines, raccords, etc.).</summary>
        public string ReseauFilterSuffix { get; init; } = "CVC_RSX";

        /// <summary>Suffixe du filtre « équipements ».</summary>
        public string EquipmentFilterSuffix { get; init; } = "CVC_EQPT";

        /// <summary>Préfixe des vues de vérification sauvegardées.</summary>
        public string SavedViewPrefix { get; init; } = "SYS_CVC";

        /// <summary>Suffixe des filtres « réseaux » recréés lors de la sauvegarde d'une vue.</summary>
        public string SavedReseauFilterSuffix { get; init; } = "FIL_RSX";

        /// <summary>Suffixe des filtres « équipements » recréés lors de la sauvegarde d'une vue.</summary>
        public string SavedEquipmentFilterSuffix { get; init; } = "FIL_EQPT";

        /// <summary>Catégories de réseaux ciblées par le filtre « réseaux ».</summary>
        public IReadOnlyList<BuiltInCategory> ReseauCategories { get; init; } = new[]
        {
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctTerminal,
        };

        /// <summary>Catégories ciblées par le filtre « équipements ».</summary>
        public IReadOnlyList<BuiltInCategory> EquipmentCategories { get; init; } = new[]
        {
            BuiltInCategory.OST_MechanicalEquipment,
        };

        /// <summary>Options par défaut (projet Tour Triangle historique).</summary>
        public static CvcFilterOptions Default { get; } = new CvcFilterOptions();
    }
}
