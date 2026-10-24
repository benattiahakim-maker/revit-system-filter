using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Egis.Revit.SystemFilter.Core.Models.Envelope;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>
    /// Construit le résultat natif (objets, propriétés, références) à partir de l'inventaire et de l'analyse des pièces.
    /// Sans API Revit (testé sous Linux). Même structure que celle que le backend déduisait de systems-inventory :
    /// systèmes à la racine, leurs éléments puis leurs pièces en enfants, pièces sans système en racines (sans <c>type</c>).
    /// Ids : <c>system-N</c>, <c>element-N</c>, <c>room-N</c>. Un élément partagé entre systèmes est un objet identique.
    /// </summary>
    public static class ResultEnvelopeBuilder
    {
        public const string ViewerIdKey = "viewerId";

        private enum Relation { Crossed, Served, Start }

        public static ResultEnvelope Build(SystemInventory inventory, RoomAnalysis analysis)
        {
            if (inventory is null) throw new ArgumentNullException(nameof(inventory));
            if (analysis is null) throw new ArgumentNullException(nameof(analysis));
            var envelope = new ResultEnvelope();
            var models = inventory.Models ?? new List<ModelRecord>();
            if (inventory.Rooms is null)
                envelope.Warnings.Add("Pièces non disponibles : l'inventaire n'en contient pas (format 1.0).");
            if (models.Count == 0)
                envelope.Models.Add(new EnvelopeModel { Key = ModelKeys.Host, Title = inventory.Model.Title ?? ModelKeys.Host });
            else
                envelope.Models.AddRange(models.Select(m => new EnvelopeModel { Key = m.Key, Title = m.Title ?? m.Key }));
            // Systèmes et éléments appartiennent toujours à la maquette hôte (leurs viewerId portent son préfixe).
            var hostKey = models.FirstOrDefault(m => m.IsHost)?.Key ?? ModelKeys.Host;

            var rooms = (inventory.Rooms ?? new List<PlacedRoomRecord>()).ToDictionary(r => r.Id);
            var types = inventory.SystemTypes.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());

            var relations = new Dictionary<long, HashSet<Relation>>();
            var systemsOf = new Dictionary<Relation, Dictionary<long, List<long>>>
            {
                [Relation.Crossed] = new Dictionary<long, List<long>>(),
                [Relation.Served] = new Dictionary<long, List<long>>(),
                [Relation.Start] = new Dictionary<long, List<long>>(),
            };
            void Relate(long roomId, Relation relation, long systemId)
            {
                if (!relations.TryGetValue(roomId, out var set)) relations[roomId] = set = new HashSet<Relation>();
                set.Add(relation);
                var map = systemsOf[relation];
                if (!map.TryGetValue(roomId, out var list)) map[roomId] = list = new List<long>();
                if (!list.Contains(systemId)) list.Add(systemId);
            }
            foreach (var s in inventory.Systems)
            {
                if (!analysis.Systems.TryGetValue(s.Id, out var sr)) continue;
                foreach (var r in sr.Crossed) Relate(r, Relation.Crossed, s.Id);
                foreach (var r in sr.Served) { Relate(r, Relation.Served, s.Id); Relate(r, Relation.Crossed, s.Id); }
                foreach (var r in sr.Start) { Relate(r, Relation.Start, s.Id); Relate(r, Relation.Crossed, s.Id); }
            }

            EnvelopeObject ElementObject(NetworkElementRecord e)
            {
                var name = !string.IsNullOrEmpty(e.Name) ? e.Name! : !string.IsNullOrEmpty(e.Category) ? e.Category : "Élément " + e.Id;
                var props = new List<EnvelopeProperty>();
                props.AddRange(Text(ViewerIdKey, "Identifiant du viewer", e.UniqueId));
                props.AddRange(Text("modelKey", "Maquette", hostKey));
                // Mêmes membres scalaires que le JSON de l'inventaire (clé = libellé).
                props.AddRange(Text("category", "category", e.Category));
                props.AddRange(Text("domain", "domain", e.Domain.ToString()));
                props.AddRange(Text("family", "family", e.Family));
                props.AddRange(Text("type", "type", e.Type));
                props.AddRange(Text("level", "level", e.Level));
                props.AddRange(Text("size", "size", e.Size));
                props.AddRange(Num("lengthMm", "lengthMm", e.LengthMm, null));
                props.AddRange(Text("materiel", "materiel", e.Materiel));
                props.AddRange(Text("numCta", "numCta", e.NumCta));
                if (analysis.ElementRoomIds.TryGetValue(e.Id, out var roomIds))
                    props.AddRange(Refs("room", "Pièce", roomIds.Where(rooms.ContainsKey), "room"));
                return new EnvelopeObject { Id = "element-" + e.Id, Name = name, Properties = props };
            }

            var elementsOf = new Dictionary<long, List<EnvelopeObject>>();
            foreach (var e in inventory.Elements)
                foreach (var parent in e.ParentIds)
                {
                    if (!elementsOf.TryGetValue(parent, out var list)) elementsOf[parent] = list = new List<EnvelopeObject>();
                    list.Add(ElementObject(e));
                }

            EnvelopeObject? RoomObject(long roomId)
            {
                if (!rooms.TryGetValue(roomId, out var r)) return null;
                var label = string.Join(" ", new[] { r.Number, r.Name }.Where(x => !string.IsNullOrEmpty(x)));
                var props = new List<EnvelopeProperty>();
                props.AddRange(Text(ViewerIdKey, "Identifiant du viewer", r.UniqueId));
                if (relations.TryGetValue(roomId, out var rel))
                {
                    var strongest = rel.Contains(Relation.Start) ? "départ" : rel.Contains(Relation.Served) ? "desservie" : "traversée";
                    props.Add(new EnvelopeProperty { Key = "type", Label = "Type", Value = strongest });
                }
                props.AddRange(Text("number", "Numéro", r.Number));
                props.AddRange(Text("level", "Niveau", r.Level));
                props.AddRange(Num("area", "Surface", r.Area, "m²"));
                props.AddRange(Num("volume", "Volume", r.Volume, "m³"));
                props.AddRange(Num("height", "Hauteur", r.Height, "mm"));
                props.AddRange(Num("perimeter", "Périmètre", r.Perimeter, "mm"));
                props.AddRange(Num("baseOffset", "Décalage de base", r.BaseOffset, "mm"));
                props.AddRange(Num("limitOffset", "Décalage limite", r.LimitOffset, "mm"));
                props.AddRange(Text("upperLimit", "Limite supérieure", r.UpperLimit));
                props.AddRange(Text("department", "Service", r.Department));
                props.AddRange(Text("phase", "Phase", r.Phase));
                props.AddRange(Text("occupancy", "Occupation", r.Occupancy));
                props.AddRange(Text("baseFinish", "Finition de base", r.BaseFinish));
                props.AddRange(Text("ceilingFinish", "Finition du plafond", r.CeilingFinish));
                props.AddRange(Text("floorFinish", "Finition du sol", r.FloorFinish));
                props.AddRange(Text("wallFinish", "Finition des murs", r.WallFinish));
                props.AddRange(Text("comments", "Commentaires", r.Comments));
                props.AddRange(Text("modelKey", "Maquette", r.ModelKey));
                if (r.BBox != null)
                    props.Add(new EnvelopeProperty { Key = "bbox", Label = "Boîte englobante (mm)", Type = EnvelopeProperty.Box, Value = r.BBox });
                // Rôle PAR système : un ref par système et par rôle.
                props.AddRange(Refs("system", "Traversée par le système", Get(systemsOf[Relation.Crossed], roomId), "system"));
                props.AddRange(Refs("servedBy", "Desservie par le système", Get(systemsOf[Relation.Served], roomId), "system"));
                props.AddRange(Refs("startOf", "Pièce de départ du système", Get(systemsOf[Relation.Start], roomId), "system"));
                return new EnvelopeObject { Id = "room-" + roomId, Name = label.Length > 0 ? label : "Pièce " + roomId, Properties = props };
            }

            foreach (var s in inventory.Systems)
            {
                analysis.Systems.TryGetValue(s.Id, out var sr);
                var roomIds = (sr?.Crossed ?? new List<long>()).Concat(sr?.Served ?? new List<long>()).Concat(sr?.Start ?? new List<long>()).Distinct().OrderBy(x => x);
                types.TryGetValue(s.SystemTypeId, out var type);
                elementsOf.TryGetValue(s.Id, out var own);
                var children = new List<EnvelopeObject>(own ?? new List<EnvelopeObject>());
                foreach (var id in roomIds) { var o = RoomObject(id); if (o != null) children.Add(o); }
                var props = new List<EnvelopeProperty>();
                props.AddRange(Text(ViewerIdKey, "Identifiant du viewer", s.UniqueId));
                props.AddRange(Text("modelKey", "Maquette", hostKey));
                props.AddRange(Text("levels", "Niveaux", string.Join(", ", s.Levels.Where(l => !string.IsNullOrEmpty(l)))));
                if (type != null)
                {
                    props.AddRange(Text("systemType", "Type de système", type.Name));
                    props.AddRange(Text("domain", "Domaine", type.Domain.ToString()));
                    props.AddRange(Text("classification", "Classification", type.Classification));
                }
                if (s.BaseEquipmentId is long baseId && (own ?? new List<EnvelopeObject>()).Any(o => o.Id == "element-" + baseId))
                    props.Add(new EnvelopeProperty { Key = "baseEquipment", Label = "Équipement de départ", Type = EnvelopeProperty.Ref, Value = "element-" + baseId });
                props.AddRange(Num("systemTypeId", "systemTypeId", s.SystemTypeId, null));
                envelope.Objects.Add(new EnvelopeObject
                {
                    Id = "system-" + s.Id,
                    Name = !string.IsNullOrEmpty(s.Name) ? s.Name : "Système " + s.Id,
                    Children = children,
                    Properties = props,
                });
            }
            // Pièces sans aucun système : racines, pour qu'aucune pièce de l'inventaire ne disparaisse.
            foreach (var id in rooms.Keys.Where(id => !relations.ContainsKey(id)).OrderBy(id => id))
            {
                var o = RoomObject(id);
                if (o != null) envelope.Objects.Add(o);
            }
            if (envelope.Warnings.Count > 0) envelope.Status = "partial";
            return envelope;
        }

        private static List<long>? Get(Dictionary<long, List<long>> map, long key) => map.TryGetValue(key, out var v) ? v : null;

        private static IEnumerable<EnvelopeProperty> Text(string key, string label, string? value)
            => string.IsNullOrEmpty(value) ? Array.Empty<EnvelopeProperty>() : new[] { new EnvelopeProperty { Key = key, Label = label, Value = value } };

        private static IEnumerable<EnvelopeProperty> Num(string key, string label, double? value, string? unit)
            => value is double d && double.IsFinite(d)
                ? new[] { new EnvelopeProperty { Key = key, Label = label, Type = EnvelopeProperty.Number, Value = d, Unit = unit } }
                : Array.Empty<EnvelopeProperty>();

        private static IEnumerable<EnvelopeProperty> Refs(string key, string label, IEnumerable<long>? ids, string prefix)
            => (ids ?? Array.Empty<long>()).OrderBy(i => i).Select(i => new EnvelopeProperty
            {
                Key = key, Label = label, Type = EnvelopeProperty.Ref, Value = prefix + "-" + i.ToString(CultureInfo.InvariantCulture),
            });
    }
}
