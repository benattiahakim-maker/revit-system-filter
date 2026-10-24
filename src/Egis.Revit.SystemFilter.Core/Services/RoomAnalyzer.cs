using System;
using System.Collections.Generic;
using System.Linq;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>Pièces d'un système, ids triés (ceux de <c>rooms[].id</c>).</summary>
    public sealed class SystemRooms
    {
        /// <summary>Traversées : en conflit avec au moins un élément du système (contient les desservies et la pièce de départ).</summary>
        public List<long> Crossed { get; } = new List<long>();

        /// <summary>Desservies : en conflit avec un terminal (DuctTerminal, Sprinklers, PlumbingFixtures).</summary>
        public List<long> Served { get; } = new List<long>();

        /// <summary>Pièces de départ : en conflit avec l'équipement de départ (<c>baseEquipmentId</c>).</summary>
        public List<long> Start { get; } = new List<long>();
    }

    /// <summary>Résultat de <see cref="RoomAnalyzer.Analyze"/>.</summary>
    public sealed class RoomAnalysis
    {
        /// <summary>Pièces (ids triés) de chaque élément ayant une boîte ; absent = hors de toute pièce ou sans géométrie.</summary>
        public Dictionary<long, List<long>> ElementRoomIds { get; } = new Dictionary<long, List<long>>();

        public Dictionary<long, SystemRooms> Systems { get; } = new Dictionary<long, SystemRooms>();

        public double ToleranceMm { get; set; }
        public int RoomsWithGeometry { get; set; }
        public int AnalyzedElements { get; set; }
        public int ElementsInRooms { get; set; }
    }

    /// <summary>
    /// Calcul des pièces traversées, desservies et de départ, sans API Revit (testé sous Linux). Même règle que l'ancien
    /// calcul du backend : conflit = recouvrement de boîtes alignées sur les axes STRICTEMENT supérieur à la tolérance
    /// (1 mm) sur X, Y et Z, toutes boîtes en mm dans le repère de l'hôte. Éléments en diagonale : faux positifs possibles.
    /// </summary>
    public static class RoomAnalyzer
    {
        public const double DefaultToleranceMm = 1;

        /// <summary>Catégories Revit (avec ou sans préfixe OST_) dont les pièces sont « desservies ».</summary>
        public static readonly IReadOnlyList<string> TerminalCategories = new[] { "DuctTerminal", "Sprinklers", "PlumbingFixtures" };

        public static string NormalizeCategory(string? category)
            => (category ?? string.Empty).StartsWith("OST_", StringComparison.Ordinal) ? category!.Substring(4) : category ?? string.Empty;

        public static bool IsTerminal(string? category) => TerminalCategories.Contains(NormalizeCategory(category));

        public static RoomAnalysis Analyze(SystemInventory inventory, double toleranceMm = DefaultToleranceMm)
        {
            if (inventory is null) throw new ArgumentNullException(nameof(inventory));
            if (double.IsNaN(toleranceMm) || toleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            var result = new RoomAnalysis { ToleranceMm = toleranceMm };

            // Pièces avec géométrie, triées par X minimal : la recherche saute tout ce qui commence après l'élément.
            var rooms = (inventory.Rooms ?? new List<PlacedRoomRecord>())
                .Where(r => r.BBox != null && Valid(r.BBox))
                .OrderBy(r => r.BBox!.Min[0]).ToList();
            result.RoomsWithGeometry = rooms.Count;

            foreach (var e in inventory.Elements)
            {
                if (e.BBox == null || !Valid(e.BBox)) continue;
                result.AnalyzedElements++;
                var found = new List<long>();
                var b = e.BBox;
                foreach (var r in rooms)
                {
                    var rb = r.BBox!;
                    if (rb.Min[0] >= b.Max[0]) break; // pièces suivantes : X minimal encore plus grand
                    if (Overlap(b, rb, toleranceMm)) found.Add(r.Id);
                }
                found.Sort();
                if (found.Count > 0) result.ElementsInRooms++;
                result.ElementRoomIds[e.Id] = found;
            }

            var members = new Dictionary<long, List<NetworkElementRecord>>();
            foreach (var e in inventory.Elements)
                foreach (var p in e.ParentIds)
                {
                    if (!members.TryGetValue(p, out var list)) members[p] = list = new List<NetworkElementRecord>();
                    list.Add(e);
                }
            foreach (var s in inventory.Systems)
            {
                var crossed = new SortedSet<long>();
                var served = new SortedSet<long>();
                var start = new SortedSet<long>();
                if (members.TryGetValue(s.Id, out var els))
                    foreach (var e in els)
                    {
                        if (!result.ElementRoomIds.TryGetValue(e.Id, out var ids)) continue;
                        foreach (var r in ids) crossed.Add(r);
                        if (IsTerminal(e.Category)) foreach (var r in ids) served.Add(r);
                        if (s.BaseEquipmentId == e.Id) foreach (var r in ids) start.Add(r);
                    }
                var rooms3 = new SystemRooms();
                rooms3.Crossed.AddRange(crossed);
                rooms3.Served.AddRange(served);
                rooms3.Start.AddRange(start);
                result.Systems[s.Id] = rooms3;
            }
            return result;
        }

        private static bool Valid(BBoxRecord b)
            => b.Min.Length == 3 && b.Max.Length == 3
               && b.Min.All(double.IsFinite) && b.Max.All(double.IsFinite)
               && b.Min[0] <= b.Max[0] && b.Min[1] <= b.Max[1] && b.Min[2] <= b.Max[2];

        private static bool Overlap(BBoxRecord a, BBoxRecord b, double tol)
            => Math.Min(a.Max[0], b.Max[0]) - Math.Max(a.Min[0], b.Min[0]) > tol
               && Math.Min(a.Max[1], b.Max[1]) - Math.Max(a.Min[1], b.Min[1]) > tol
               && Math.Min(a.Max[2], b.Max[2]) - Math.Max(a.Min[2], b.Min[2]) > tol;
    }
}
