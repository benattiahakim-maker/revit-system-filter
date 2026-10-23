using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
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

        /// <summary>Avertissements du dernier <see cref="BuildInventory"/> (sans chemin), à écrire dans run.log.</summary>
        public IReadOnlyList<string> Warnings { get; private set; } = new List<string>();

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
                Mark = GetMark(element),
                Level = GetLevelName(document, element),
                Size = GetSize(element),
                LengthMm = GetLengthMm(element),
                ParentIds = new List<long> { systemId },
            };

            if (builtInCategory == BuiltInCategory.OST_MechanicalEquipment)
            {
                record.Materiel = element.LookupParameter(_cvcOptions.MaterialNameParameterName)?.AsString();
                record.NumCta = element.LookupParameter(_cvcOptions.EquipmentCtaParameterName)?.AsString();
            }

            knownElementIds[element.Id.Value] = record;
            inventory.Elements.Add(record);
        }

        private static string? GetMark(Element element)
        {
            var mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            return string.IsNullOrWhiteSpace(mark) ? null : mark!.Trim();
        }

        private static string? GetLevelName(Document document, Element element)
        {
            if (element.LevelId != ElementId.InvalidElementId)
            {
                return document.GetElement(element.LevelId)?.Name;
            }

            var levelParameter = element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            var levelId = levelParameter?.AsElementId();
            return levelId is not null && levelId != ElementId.InvalidElementId
                ? document.GetElement(levelId)?.Name
                : null;
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
