using System;
using System.Collections.Generic;
using System.Linq;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>Niveau du modèle (nom + élévation, même repère que les coordonnées des éléments).</summary>
    public readonly struct LevelInfo
    {
        public LevelInfo(string name, double elevation)
        {
            Name = name;
            Elevation = elevation;
        }

        public string Name { get; }
        public double Elevation { get; }
    }

    /// <summary>Choix sans Revit du niveau d'un élément, et liste des niveaux d'un système.</summary>
    public static class LevelChoice
    {
        /// <summary>Tolérance (pieds) : un point à peine sous un niveau est rattaché à ce niveau.</summary>
        public const double Tolerance = 0.01;

        /// <summary>
        /// Premier nom non vide de <paramref name="orderedCandidates"/> (LevelId, paramètres de niveau, hôte) ;
        /// sinon niveau le plus proche sous <paramref name="z"/> (ou le plus bas si tout est au-dessus) ; sinon null.
        /// </summary>
        public static string? Choose(IEnumerable<string?> orderedCandidates, IReadOnlyList<LevelInfo> levels, double? z)
        {
            foreach (var candidate in orderedCandidates ?? Enumerable.Empty<string?>())
            {
                if (!string.IsNullOrWhiteSpace(candidate)) return candidate!.Trim();
            }
            return NearestBelow(levels, z);
        }

        public static string? NearestBelow(IReadOnlyList<LevelInfo> levels, double? z)
        {
            if (z is null || levels is null || levels.Count == 0) return null;
            var below = levels.Where(l => l.Elevation <= z.Value + Tolerance)
                .OrderByDescending(l => l.Elevation).ThenBy(l => l.Name, StringComparer.Ordinal).ToList();
            if (below.Count > 0) return below[0].Name;
            return levels.OrderBy(l => l.Elevation).ThenBy(l => l.Name, StringComparer.Ordinal).First().Name;
        }

        /// <summary>Noms distincts, triés par élévation (niveau inconnu en fin), puis par nom.</summary>
        public static List<string> SortDistinct(IEnumerable<string?> names, IReadOnlyList<LevelInfo> levels)
        {
            var elevation = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var l in levels ?? Array.Empty<LevelInfo>())
            {
                if (!elevation.ContainsKey(l.Name)) elevation[l.Name] = l.Elevation;
            }
            return (names ?? Enumerable.Empty<string?>())
                .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => elevation.TryGetValue(n, out var e) ? e : double.MaxValue)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();
        }

        public static string Summary(int elementCount, int missingBefore, int missingAfter) =>
            $"niveaux : {elementCount} éléments ; sans niveau avant repli : {missingBefore} ; après repli (hôte / altitude) : {missingAfter}";
    }
}
