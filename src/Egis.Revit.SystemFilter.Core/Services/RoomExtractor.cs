using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>Maquettes et pièces extraites (format 1.1) ; <see cref="Warnings"/> sans chemin ni secret.</summary>
    public sealed class RoomExtraction
    {
        public List<ModelRecord> Models { get; } = new List<ModelRecord>();
        public List<PlacedRoomRecord> Rooms { get; } = new List<PlacedRoomRecord>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Extrait les pièces placées (Area &gt; 0) du document principal et des liens chargés, chacune avec sa boîte
    /// englobante alignée sur les axes, en mm, dans le repère de la maquette principale (8 coins transformés par
    /// <c>GetTotalTransform()</c> du lien puis boîte alignée). N'effectue AUCUN calcul de conflit : il est fait par le backend.
    /// Limites : liens de premier niveau seulement (pas de lien dans un lien) ; toutes les phases (aucun filtre de phase
    /// dans les réglages « systems »).
    /// </summary>
    public static class RoomExtractor
    {
        public static RoomExtraction Extract(Document host)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            var result = new RoomExtraction();
            var ids = new RoomIdAllocator();

            result.Models.Add(new ModelRecord { Key = ModelKeys.Host, Title = host.Title, IsHost = true, Transform = ToRecord(Transform.Identity) });
            ExtractFrom(host, ModelKeys.Host, Transform.Identity, ids, result);

            var links = new FilteredElementCollector(host).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .OrderBy(l => l.Id.Value).ToList();
            foreach (var link in links)
            {
                Document? linkDoc;
                try { linkDoc = link.GetLinkDocument(); }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException) { linkDoc = null; }
                if (linkDoc is null) continue; // lien non chargé : déjà signalé dans run.log

                var key = ModelKeys.ForLinkInstance(link.Id.Value);
                var transform = link.GetTotalTransform();
                result.Models.Add(new ModelRecord { Key = key, Title = linkDoc.Title, IsHost = false, Transform = ToRecord(transform) });
                try
                {
                    ExtractFrom(linkDoc, key, transform, ids, result);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
                {
                    result.Warnings.Add($"pièces du lien {key} non extraites : {ex.GetType().Name}");
                }
            }
            return result;
        }

        private static void ExtractFrom(Document doc, string modelKey, Transform modelToHost, RoomIdAllocator ids, RoomExtraction result)
        {
            var toHost = ToAffine(modelToHost);
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .OfType<Room>().OrderBy(r => r.Id.Value);
            foreach (var room in rooms)
            {
                if (room.Area <= 0) continue; // non placée ou non délimitée
                result.Rooms.Add(ToRecord(doc, room, modelKey, ids.Allocate(modelKey, room.Id.Value), toHost));
            }
        }

        private static PlacedRoomRecord ToRecord(Document doc, Room room, string modelKey, long id, Affine toHost)
        {
            var volume = Safe(() => room.Volume);
            return new PlacedRoomRecord
            {
                Id = id,
                RevitId = room.Id.Value,
                UniqueId = room.UniqueId,
                ModelKey = modelKey,
                Number = Blank(room.Number),
                Name = Blank(Text(doc, room.get_Parameter(BuiltInParameter.ROOM_NAME)) ?? room.Name),
                Level = room.Level?.Name,
                Area = Math.Round(UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters), 3),
                Volume = volume is double v && v > 0 ? Math.Round(UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.CubicMeters), 3) : (double?)null,
                Phase = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_PHASE)),
                Department = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)),
                Height = LengthMm(room.get_Parameter(BuiltInParameter.ROOM_HEIGHT)),
                Perimeter = LengthMm(room.get_Parameter(BuiltInParameter.ROOM_PERIMETER)),
                BaseOffset = LengthMm(room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET)),
                LimitOffset = LengthMm(room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET)),
                UpperLimit = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL)),
                Occupancy = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_OCCUPANCY)),
                Comments = Text(doc, room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)),
                BaseFinish = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_FINISH_BASE)),
                CeilingFinish = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING)),
                FloorFinish = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)),
                WallFinish = Text(doc, room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL)),
                BBox = BBoxOf(room, toHost),
            };
        }

        /// <summary>Boîte englobante de l'élément (vue nulle) en mm dans le repère de l'hôte ; null sans géométrie.</summary>
        public static BBoxRecord? BBoxOf(Element element, Transform modelToHost) => BBoxOf(element, ToAffine(modelToHost));

        private static BBoxRecord? BBoxOf(Element element, Affine modelToHost)
        {
            var box = Safe(() => element.get_BoundingBox(null));
            if (box is null) return null;
            return BoxMath.ToHostMm(ToVec(box.Min), ToVec(box.Max), ToAffine(box.Transform), modelToHost);
        }

        public static Affine ToAffine(Transform t) =>
            new Affine(ToVec(t.Origin), ToVec(t.BasisX), ToVec(t.BasisY), ToVec(t.BasisZ));

        private static Vec3 ToVec(XYZ p) => new Vec3(p.X, p.Y, p.Z);

        private static TransformRecord ToRecord(Transform t) => new TransformRecord
        {
            Origin = new[] { BoxMath.Mm(t.Origin.X), BoxMath.Mm(t.Origin.Y), BoxMath.Mm(t.Origin.Z) },
            BasisX = new[] { t.BasisX.X, t.BasisX.Y, t.BasisX.Z },
            BasisY = new[] { t.BasisY.X, t.BasisY.Y, t.BasisY.Z },
            BasisZ = new[] { t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z },
        };

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        /// <summary>Valeur lisible d'un paramètre : texte, nom de l'élément référencé, ou valeur affichée ; null si vide.</summary>
        private static string? Text(Document doc, Parameter? p)
        {
            if (p is null || !p.HasValue) return null;
            switch (p.StorageType)
            {
                case StorageType.String: return Blank(p.AsString());
                case StorageType.ElementId:
                    var id = p.AsElementId();
                    return id is null || id == ElementId.InvalidElementId ? null : Blank(doc.GetElement(id)?.Name);
                default: return Blank(p.AsValueString());
            }
        }

        private static double? LengthMm(Parameter? p) =>
            p is not null && p.HasValue && p.StorageType == StorageType.Double
                ? BoxMath.Mm(p.AsDouble())
                : (double?)null;

        private static T? Safe<T>(Func<T?> read)
        {
            try { return read(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return default; }
            catch (InvalidOperationException) { return default; }
        }
    }
}
