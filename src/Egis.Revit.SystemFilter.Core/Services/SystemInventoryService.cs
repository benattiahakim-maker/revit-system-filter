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

            var knownTypeIds = new HashSet<long>();
            var knownElementIds = new HashSet<long>();

            if (_options.IncludeDuctSystems)
            {
                CollectDuctSystems(document, inventory, knownTypeIds, knownElementIds);
            }

            if (_options.IncludePipingSystems)
            {
                CollectPipingSystems(document, inventory, knownTypeIds, knownElementIds);
            }

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
            HashSet<long> knownTypeIds, HashSet<long> knownElementIds)
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
            }
        }

        private void CollectPipingSystems(
            Document document, SystemInventory inventory,
            HashSet<long> knownTypeIds, HashSet<long> knownElementIds)
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
            });
        }

        private void AddElement(
            Document document, SystemInventory inventory, HashSet<long> knownElementIds,
            Element element, NetworkDomain domain, long systemId)
        {
            if (element is null || !knownElementIds.Add(element.Id.Value))
            {
                // Élément déjà rattaché à un système : premier système rencontré = parent.
                return;
            }

            var builtInCategory = element.Category?.BuiltInCategory ?? BuiltInCategory.INVALID;
            if (!CategoryKeys.TryGetValue(builtInCategory, out var categoryKey))
            {
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
                Level = GetLevelName(document, element),
                Size = GetSize(element),
                LengthMm = GetLengthMm(element),
                SystemId = systemId,
            };

            if (builtInCategory == BuiltInCategory.OST_MechanicalEquipment)
            {
                record.Materiel = element.LookupParameter(_cvcOptions.MaterialNameParameterName)?.AsString();
                record.NumCta = element.LookupParameter(_cvcOptions.EquipmentCtaParameterName)?.AsString();
            }

            inventory.Elements.Add(record);
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
