using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>
    /// Normalise les avertissements du plugin avant de les écrire dans <c>warnings[]</c> de l'enveloppe.
    /// Le validateur du backend (<c>validateEnvelope</c>) n'impose que « tableau de textes » ; ces limites sont celles du plugin,
    /// pour garder une enveloppe légère : textes sur une ligne (retours à la ligne remplacés par une espace), vides ignorés,
    /// longueur bornée, doublons retirés (ordre conservé) et nombre borné avec une ligne de synthèse.
    /// Les textes sont censés arriver déjà nettoyés (chemins et secrets masqués par <c>RunLog.Sanitize</c>).
    /// </summary>
    public static class EnvelopeWarnings
    {
        /// <summary>Nombre maximal d'avertissements dans l'enveloppe, ligne de synthèse comprise.</summary>
        public const int MaxCount = 20;

        /// <summary>Longueur maximale d'un avertissement (aligné sur <c>RunLog.Sanitize</c>).</summary>
        public const int MaxLength = 400;

        public static List<string> Normalize(IEnumerable<string?>? warnings)
        {
            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in warnings ?? Enumerable.Empty<string?>())
            {
                var text = Clean(raw);
                if (text.Length > 0 && seen.Add(text)) distinct.Add(text);
            }
            if (distinct.Count <= MaxCount) return distinct;

            var kept = distinct.Take(MaxCount - 1).ToList();
            var others = distinct.Count - kept.Count;
            kept.Add(string.Format(CultureInfo.InvariantCulture, "… et {0} autre(s) avertissement(s), voir run.log.", others));
            return kept;
        }

        private static string Clean(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var text = raw!.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= MaxLength) return text;
            var cut = MaxLength - 1;
            if (char.IsHighSurrogate(text[cut - 1])) cut--; // ne jamais couper une paire de substitution
            return text.Substring(0, cut).TrimEnd() + "…";
        }
    }
}
