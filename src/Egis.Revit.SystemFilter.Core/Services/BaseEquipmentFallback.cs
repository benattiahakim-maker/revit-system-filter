using System.Collections.Generic;
using System.Linq;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>Équipement mécanique raccordé à un système, avec le nombre de réseaux MEP distincts auxquels il est raccordé.</summary>
    public readonly struct EquipmentCandidate
    {
        public EquipmentCandidate(long id, int networkCount)
        {
            Id = id;
            NetworkCount = networkCount;
        }

        public long Id { get; }
        public int NetworkCount { get; }
    }

    /// <summary>
    /// Repli sans Revit quand <c>BaseEquipment</c> est null : on prend l'équipement mécanique du réseau
    /// raccordé au plus grand nombre de réseaux ; à égalité, le plus petit id (déterministe).
    /// </summary>
    public static class BaseEquipmentFallback
    {
        /// <summary>Retourne l'id choisi, ou null s'il n'y a aucun candidat.</summary>
        public static long? Choose(IEnumerable<EquipmentCandidate> candidates)
        {
            var list = candidates?.ToList() ?? new List<EquipmentCandidate>();
            if (list.Count == 0) return null;
            return list
                .GroupBy(c => c.Id)
                .Select(g => new EquipmentCandidate(g.Key, g.Max(c => c.NetworkCount)))
                .OrderByDescending(c => c.NetworkCount)
                .ThenBy(c => c.Id)
                .First().Id;
        }

        /// <summary>Ligne de run.log pour un repli utilisé.</summary>
        public static string Message(long systemId, string? systemName, long equipmentId, int candidateCount) =>
            $"repli équipement de départ : système {systemId} ({systemName}) sans BaseEquipment, équipement mécanique {equipmentId} retenu parmi {candidateCount} candidat(s)";

        /// <summary>Au-delà de <see cref="BaseEquipmentCheck.MaxDetailedWarnings"/> lignes, une seule ligne de synthèse.</summary>
        public static List<string> Summarize(List<string> lines)
        {
            if (lines.Count <= BaseEquipmentCheck.MaxDetailedWarnings) return lines;
            var examples = string.Join(" ; ", lines.Take(BaseEquipmentCheck.SummaryExamples));
            return new List<string>
            {
                $"{lines.Count} systèmes sans BaseEquipment avec repli sur un équipement mécanique du réseau ; exemples : {examples}",
            };
        }
    }
}
