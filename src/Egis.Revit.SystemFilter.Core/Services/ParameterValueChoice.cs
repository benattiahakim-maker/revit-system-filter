using System.Collections.Generic;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>Lecture brute d'un paramètre Revit (sans API Revit) : AsString() et AsValueString().</summary>
    public readonly struct ParameterReading
    {
        public ParameterReading(bool found, string? asString, string? asValueString)
        {
            Found = found;
            AsString = asString;
            AsValueString = asValueString;
        }

        public static ParameterReading Missing { get; } = new ParameterReading(false, null, null);

        public bool Found { get; }
        public string? AsString { get; }
        public string? AsValueString { get; }
    }

    /// <summary>
    /// Choix sans Revit de la valeur d'un paramètre : l'instance d'abord, puis le type ;
    /// pour chacun AsString() puis, si null/vide, AsValueString() (paramètres non texte) ; trim, vide => null.
    /// </summary>
    public static class ParameterValueChoice
    {
        public static string? Normalize(ParameterReading reading)
        {
            if (!reading.Found) return null;
            var text = Clean(reading.AsString);
            return text ?? Clean(reading.AsValueString);
        }

        public static string? Choose(ParameterReading instance, ParameterReading type) =>
            Normalize(instance) ?? Normalize(type);

        private static string? Clean(string? value)
        {
            if (value is null) return null;
            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        /// <summary>Lignes de synthèse pour run.log : une INFO, et un WARN par paramètre jamais rempli.</summary>
        public static (string Info, List<string> Warnings) Summarize(
            int equipmentCount, int numCtaCount, int materielCount, int markCount,
            string numCtaParameterName, string materielParameterName)
        {
            var info = $"équipements mécaniques : {equipmentCount} ; numCta rempli : {numCtaCount} ; materiel rempli : {materielCount} ; mark rempli : {markCount}";
            var warnings = new List<string>();
            if (equipmentCount > 0 && numCtaCount == 0)
                warnings.Add($"paramètre « {numCtaParameterName} » absent ou vide sur les {equipmentCount} équipements mécaniques (instance et type) : numCta non exporté");
            if (equipmentCount > 0 && materielCount == 0)
                warnings.Add($"paramètre « {materielParameterName} » absent ou vide sur les {equipmentCount} équipements mécaniques (instance et type) : materiel non exporté");
            return (info, warnings);
        }
    }
}
