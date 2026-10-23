using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Plumbing;
using Egis.Revit.SystemFilter.Core.Abstractions;
using Egis.Revit.SystemFilter.Core.Configuration;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <inheritdoc cref="ISystemInventoryService"/>
    public sealed class SystemInventoryService : ISystemInventoryService
    {
        private static readonly Dictionary<BuiltInCategory, string> CategoryKeys =
            new Dictionary<BuiltInCategory, string>
            {
                // Gaines
                { BuiltInCategory.OST_DuctCurves, "DuctCurves" },
                { BuiltInCategory.OST_DuctFitting, "DuctFitting" },
                { BuiltInCategory.OST_DuctAccessory, "DuctAccessory" },
                { BuiltInCategory.OST_FlexDuctCurves, "FlexDuctCurves" },
                { BuiltInCategory.OST_DuctTerminal, "DuctTerminal" },
                // Tuyauterie
                { BuiltInCategory.OST_PipeCurves, "PipeCurves" },
                { BuiltInCategory.OST_PipeFitting, "PipeFitting" },
                { BuiltInCategory.OST_PipeAccessory, "PipeAccessory" },
                { BuiltInCategory.OST_FlexPipeCurves, "FlexPipeCurves" },
                { BuiltInCategory.OST_PlumbingFixtures, "PlumbingFixtures" },
                { BuiltInCategory.OST_Sprinklers, "Sprinklers" },
                // Commun
                { BuiltInCategory.OST_MechanicalEquipment, "MechanicalEquipment" },
            };

        private readonly SystemInventoryOptions _options;
        private readonly CvcFilterOptions _cvcOptions;
        private List<string> _fallbackLines = new List<string>();
        private List<LevelInfo> _levels = new List<LevelInfo>();
        private int _missingLevelBefore;

        /// <summary>Avertissements du dernier <see cref="BuildInventory"/> (sans chemin), à écrire dans run.log.</summary>
        public IReadOnlyList<string> Warnings { get; private set; } = new List<string>();

        /// <summary>Lignes d'information du dernier <see cref="BuildInventory"/> (synthèses pièces et niveaux), pour run.log.</summary>
        public IReadOnlyList<string> Infos { get; private set; } = new List<string>();

        public SystemInventoryService(
            SystemInventoryOptions? options = null,
            CvcFilterOptions? cvcOptions = null)
        {
            _options = options ?? SystemInventoryOptions.Default;
            _cvcOptions = cvcOptions ?? CvcFilterOptions.Default;
        }

        public SystemInventory BuildInventory(Document document)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var inventory = new SystemInventory
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Model = new ModelInfo
                {
                    Title = document.Title,
                    RevitVersion = document.Application.VersionNumber,
                },
            };

            _fallbackLines = new List<string>();
            _levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .Select(l => new LevelInfo(l.Name, l.ProjectElevation)).ToList();
            _missingLevelBefore = 0;
            var knownTypeIds = new HashSet<long>();
            // id -> record (null : élément vu mais de catégorie non couverte).
            var knownElementIds = new Dictionary<long, NetworkElementRecord?>();

            if (_options.IncludeDuctSystems)
            {
                CollectDuctSystems(document, inventory, knownTypeIds, knownElementIds);
            }

            if (_options.IncludePipingSystems)
            {
                CollectPipingSystems(document, inventory, knownTypeIds, knownElementIds);
            }

            // Après la collecte de TOUS les éléments : baseEquipmentId doit désigner un élément exporté.
            var warnings = BaseEquipmentFallback.Summarize(_fallbackLines);
            warnings.AddRange(BaseEquipmentCheck.Apply(inventory.Systems, inventory.Elements.Select(e => e.Id)));
            var equipments = inventory.Elements.Where(e => e.Category == "MechanicalEquipment").ToList();
            var equipments = inventory.Elements.Where(e => e.Category == "MechanicalEquipment").ToList();
            var bySystem = new Dictionary<long, List<string?>>();
            foreach (var e in inventory.Elements)
            {
                foreach (var pid in e.ParentIds)
                {
                    if (!bySystem.TryGetValue(pid, out var list)) bySystem[pid] = list = new List<string?>();
                    list.Add(e.Level);
                }
            }
            foreach (var system in inventory.Systems)
            {
                system.Levels = LevelChoice.SortDistinct(
                    bySystem.TryGetValue(system.Id, out var names) ? names : new List<string?>(), _levels);
            }

            Infos = new List<string>
            {
                RoomChoice.Summary(equipments),
                LevelChoice.Summary(inventory.Elements.Count, _missingLevelBefore, inventory.Elements.Count(e => e.Level == null)),
            };
            Warnings = warnings;

            inventory.Summary = new InventorySummary
            {
                SystemTypeCount = inventory.SystemTypes.Count,
                SystemCount = inventory.Systems.Count,
                ElementCount = inventory.Elements.Count,
            };

            return inventory;
        }

        private void CollectDuctSystems(
            Document document, SystemInventory inventory,
            HashSet<long> knownTypeIds, Dictionary<long, NetworkElementRecord?> knownElementIds)
        {
            var systems = new FilteredElementCollector(document)
                .OfClass(typeof(MechanicalSystem))
                .Cast<MechanicalSystem>();

            foreach (var system in systems)
            {
                AddSystem(document, inventory, knownTypeIds, system,
                    system.GetTypeId(), NetworkDomain.Duct, system.SystemType.ToString());

                foreach (Element element in system.DuctNetwork)
                {
                    AddElement(document, inventory, knownElementIds, element,
                        NetworkDomain.Duct, system.Id.Value);
                }

                // Équipement de départ (ex. CTA) : ajouté à elements[] avec ce système dans parentIds (sans doublon).
                if (system.BaseEquipment is Element baseEquipment)
                {
                    AddElement(document, inventory, knownElementIds, baseEquipment,
                        NetworkDomain.Duct, system.Id.Value);
                }
                else if (FallbackEquipment(document, system, system.DuctNetwork) is Element fallback)
                {
                    inventory.Systems[inventory.Systems.Count - 1].BaseEquipmentId = fallback.Id.Value;
                    AddElement(document, inventory, knownElementIds, fallback,
                        NetworkDomain.Duct, system.Id.Value);
                }
            }
        }

        private void CollectPipingSystems(
            Document document, SystemInventory inventory,
            HashSet<long> knownTypeIds, Dictionary<long, NetworkElementRecord?> knownElementIds)
        {
            var systems = new FilteredElementCollector(document)
                .OfClass(typeof(PipingSystem))
                .Cast<PipingSystem>();

            foreach (var system in systems)
            {
                AddSystem(document, inventory, knownTypeIds, system,
                    system.GetTypeId(), NetworkDomain.Pipe, system.SystemType.ToString());

                foreach (Element element in system.PipingNetwork)
                {
                    AddElement(document, inventory, knownElementIds, element,
                        NetworkDomain.Pipe, system.Id.Value);
                }

                // Équipement de départ : ajouté à elements[] avec ce système dans parentIds (sans doublon).
                if (system.BaseEquipment is Element baseEquipment)
                {
                    AddElement(document, inventory, knownElementIds, baseEquipment,
                        NetworkDomain.Pipe, system.Id.Value);
                }
                else if (FallbackEquipment(document, system, system.PipingNetwork) is Element fallback)
                {
                    inventory.Systems[inventory.Systems.Count - 1].BaseEquipmentId = fallback.Id.Value;
                    AddElement(document, inventory, knownElementIds, fallback,
                        NetworkDomain.Pipe, system.Id.Value);
                }
            }
        }

        private static void AddSystem(
            Document document, SystemInventory inventory, HashSet<long> knownTypeIds,
            MEPSystem system, ElementId typeId, NetworkDomain domain, string classification)
        {
            if (typeId != ElementId.InvalidElementId && knownTypeIds.Add(typeId.Value))
            {
                inventory.SystemTypes.Add(new SystemTypeRecord
                {
                    Id = typeId.Value,
                    Name = document.GetElement(typeId)?.Name ?? "(sans type)",
                    Domain = domain,
                    Classification = classification,
                });
            }

            inventory.Systems.Add(new SystemRecord
            {
                Id = system.Id.Value,
                UniqueId = system.UniqueId,
                Name = system.Name,
                SystemTypeId = typeId.Value,
                BaseEquipmentId = BaseEquipmentOf(system),
            });
        }

        /// <summary>
        /// Repli si BaseEquipment est null : équipements mécaniques du système (éléments du système,
        /// éléments du réseau et leurs voisins via les connecteurs), choix par <see cref="BaseEquipmentFallback"/>.
        /// </summary>
        private Element? FallbackEquipment(Document document, MEPSystem system, ElementSet network)
        {
            var equipments = new Dictionary<long, Element>();
            void Consider(Element? e)
            {
                if (e is FamilyInstance && e.Category?.Id.Value == (long)BuiltInCategory.OST_MechanicalEquipment)
                    equipments[e.Id.Value] = e;
            }

            foreach (Element e in system.Elements) Consider(e);
            foreach (Element e in network)
            {
                Consider(e);
                foreach (Connector c in ConnectorsOf(e))
                {
                    if (!c.IsConnected) continue;
                    foreach (Connector r in c.AllRefs) Consider(r.Owner);
                }
            }
            if (equipments.Count == 0) return null;

            var candidates = equipments.Values
                .Select(e => new EquipmentCandidate(e.Id.Value, NetworkCount(e)))
                .ToList();
            var chosen = BaseEquipmentFallback.Choose(candidates);
            if (chosen is not long id) return null;
            _fallbackLines.Add(BaseEquipmentFallback.Message(system.Id.Value, system.Name, id, candidates.Count));
            return equipments[id];
        }

        private static IEnumerable<Connector> ConnectorsOf(Element element)
        {
            ConnectorSet? set = element switch
            {
                FamilyInstance fi => fi.MEPModel?.ConnectorManager?.Connectors,
                MEPCurve curve => curve.ConnectorManager?.Connectors,
                _ => null,
            };
            if (set is null) yield break;
            foreach (Connector c in set) yield return c;
        }

        /// <summary>Nombre de systèmes MEP distincts auxquels l'équipement est raccordé (via ses connecteurs).</summary>
        private static int NetworkCount(Element equipment)
        {
            var ids = new HashSet<long>();
            foreach (var c in ConnectorsOf(equipment))
            {
                try
                {
                    if (c.MEPSystem is MEPSystem s) ids.Add(s.Id.Value);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // connecteur sans système (ex. électrique non géré) : ignoré
                }
            }
            return ids.Count;
        }

        private static long? BaseEquipmentOf(MEPSystem system) =>
            (system as MechanicalSystem)?.BaseEquipment?.Id.Value
            ?? (system as PipingSystem)?.BaseEquipment?.Id.Value;

        private void AddElement(
            Document document, SystemInventory inventory, Dictionary<long, NetworkElementRecord?> knownElementIds,
            Element element, NetworkDomain domain, long systemId)
        {
            if (element is null) return;

            if (knownElementIds.TryGetValue(element.Id.Value, out var known))
            {
                // Élément déjà rencontré : parentIds contient TOUS ses systèmes (sans doublon).
                if (known != null) ParentIdMerge.Add(known.ParentIds, systemId);
                return;
            }

            var builtInCategory = element.Category?.BuiltInCategory ?? BuiltInCategory.INVALID;
            if (!CategoryKeys.TryGetValue(builtInCategory, out var categoryKey))
            {
                knownElementIds[element.Id.Value] = null;
                return;
            }

            var elementType = document.GetElement(element.GetTypeId()) as ElementType;

            var record = new NetworkElementRecord
            {
                Id = element.Id.Value,
                UniqueId = element.UniqueId,
                Category = categoryKey,
                Domain = domain,
                Family = elementType?.FamilyName,
                Type = elementType?.Name,
                Name = element.Name,
                Level = GetLevelName(document, element, ref _missingLevelBefore, _levels),
                Size = GetSize(element),
                LengthMm = GetLengthMm(element),
                ParentIds = new List<long> { systemId },
            };
            record.Room = GetRoom(document, element);

            if (builtInCategory == BuiltInCategory.OST_MechanicalEquipment)
            {
                record.Materiel = element.LookupParameter(_cvcOptions.MaterialNameParameterName)?.AsString();
                record.NumCta = element.LookupParameter(_cvcOptions.EquipmentCtaParameterName)?.AsString();
            }

            knownElementIds[element.Id.Value] = record;
            inventory.Elements.Add(record);
        }

        private static readonly BuiltInParameter[] LevelParameters =
        {
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.RBS_START_LEVEL_PARAM,
            BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
        };

        /// <summary>LevelId, paramètres de niveau, niveau de l'hôte (lien compris), puis altitude ; choix dans <see cref="LevelChoice"/>.</summary>
        private static string? GetLevelName(Document document, Element element, ref int missingBefore, IReadOnlyList<LevelInfo> levels)
        {
            var direct = new List<string?> { LevelNameOf(document, element.LevelId) };
            direct.AddRange(LevelParameters.Select(p => LevelNameOf(document, LevelIdParam(element, p))));
            if (direct.All(string.IsNullOrWhiteSpace)) missingBefore++;

            var candidates = direct.Concat(HostLevel(document, element));
            return LevelChoice.Choose(candidates, levels, InsertionZ(element));
        }

        private static IEnumerable<string?> HostLevel(Document document, Element element)
        {
            if (!(element is FamilyInstance fi) || fi.Host is null) yield break;
            var host = fi.Host;
            if (host is RevitLinkInstance link)
            {
                // Hôte dans un lien : on prend l'élément hôte du document lié s'il est accessible.
                var linkDoc = link.GetLinkDocument();
                var reference = fi.HostFace;
                if (linkDoc != null && reference != null && reference.LinkedElementId != ElementId.InvalidElementId)
                {
                    var linked = linkDoc.GetElement(reference.LinkedElementId);
                    if (linked != null) yield return LevelNameOf(linkDoc, linked.LevelId);
                }
                yield break;
            }
            yield return LevelNameOf(document, host.LevelId);
            foreach (var p in LevelParameters) yield return LevelNameOf(document, LevelIdParam(host, p));
        }

        private static ElementId? LevelIdParam(Element element, BuiltInParameter parameter)
        {
            var p = element.get_Parameter(parameter);
            return p != null && p.StorageType == StorageType.ElementId ? p.AsElementId() : null;
        }

        private static string? LevelNameOf(Document document, ElementId? id) =>
            id is not null && id != ElementId.InvalidElementId ? (document.GetElement(id) as Level)?.Name : null;

        private static double? InsertionZ(Element element)
        {
            if (element.Location is LocationPoint lp) return lp.Point.Z;
            if (element.Location is LocationCurve lc) return Math.Min(lc.Curve.GetEndPoint(0).Z, lc.Curve.GetEndPoint(1).Z);
            var box = element.get_BoundingBox(null);
            return box is null ? (double?)null : (box.Min.Z + box.Max.Z) / 2;
        }

        /// <summary>Espace MEP, puis pièce de la maquette, puis pièce d'un lien chargé ; choix dans <see cref="RoomChoice"/>.</summary>
        private static RoomRecord? GetRoom(Document document, Element element)
        {
            var fi = element as FamilyInstance;
            var point = InsertionPoint(element);

            SpatialElement? space = Safe(() => fi?.Space) ?? (point is null ? null : Safe(() => document.GetSpaceAtPoint(point)));
            if (space != null) return RoomChoice.Choose(ToRecord(space), null, null);

            SpatialElement? room = Safe(() => fi?.Room) ?? (point is null ? null : Safe(() => document.GetRoomAtPoint(point)));
            if (room != null) return RoomChoice.Choose(null, ToRecord(room), null);

            if (point is null) return null;
            foreach (var link in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var linkDoc = Safe(() => link.GetLinkDocument());
                if (linkDoc is null) continue;
                var local = link.GetTotalTransform().Inverse.OfPoint(point);
                var linked = Safe(() => linkDoc.GetRoomAtPoint(local));
                if (linked != null) return RoomChoice.Choose(null, null, ToRecord(linked));
            }
            return null;
        }

        private static RoomRecord ToRecord(SpatialElement s) =>
            new RoomRecord { Id = s.Id.Value, Number = s.Number, Name = s.Name };

        private static XYZ? InsertionPoint(Element element)
        {
            if (element.Location is LocationPoint lp) return lp.Point;
            var box = element.get_BoundingBox(null);
            return box is null ? null : (box.Min + box.Max) / 2;
        }

        private static T? Safe<T>(Func<T?> read) where T : class
        {
            try { return read(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static string? GetSize(Element element)
        {
            var width = element.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            var height = element.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
            if (HasDouble(width) && HasDouble(height))
            {
                return $"{ToMm(width!.AsDouble())}x{ToMm(height!.AsDouble())}";
            }

            var diameter = element.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
            if (HasDouble(diameter))
            {
                return $"Ø{ToMm(diameter!.AsDouble())}";
            }

            // Repli : paramètre « Dimension » calculé par Revit (ducts et pipes).
            return element.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString();
        }

        private static double? GetLengthMm(Element element)
        {
            var length = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (length is null || length.StorageType != StorageType.Double || !length.HasValue)
            {
                return null;
            }

            return Math.Round(
                UnitUtils.ConvertFromInternalUnits(length.AsDouble(), UnitTypeId.Millimeters), 1);
        }

        private static bool HasDouble(Parameter? parameter) =>
            parameter is not null && parameter.StorageType == StorageType.Double && parameter.HasValue;

        private static long ToMm(double internalValue) =>
            (long)Math.Round(UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters));
    }
}
