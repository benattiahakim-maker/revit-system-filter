namespace Egis.Revit.SystemFilter.Core.Models
{
    /// <summary>
    /// Résultat de l'application des filtres sur une vue : nombre de règles
    /// générées et message éventuel. Permet au mode interactif comme au mode
    /// headless de rendre compte sans dépendre d'une quelconque UI.
    /// </summary>
    public sealed class FilterApplicationResult
    {
        private FilterApplicationResult(
            bool success,
            int reseauRuleCount,
            int equipmentRuleCount,
            string? message)
        {
            Success = success;
            ReseauRuleCount = reseauRuleCount;
            EquipmentRuleCount = equipmentRuleCount;
            Message = message;
        }

        public bool Success { get; }

        public int ReseauRuleCount { get; }

        public int EquipmentRuleCount { get; }

        public string? Message { get; }

        public static FilterApplicationResult Ok(int reseauRuleCount, int equipmentRuleCount) =>
            new FilterApplicationResult(true, reseauRuleCount, equipmentRuleCount, null);

        public static FilterApplicationResult Failed(string message) =>
            new FilterApplicationResult(false, 0, 0, message);
    }
}
