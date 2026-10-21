using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Abstractions;
using Egis.Revit.SystemFilter.Core.Configuration;
using Egis.Revit.SystemFilter.Core.Models;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <inheritdoc cref="ISystemCatalogService"/>
    public sealed class SystemCatalogService : ISystemCatalogService
    {
        private readonly CvcFilterOptions _options;

        public SystemCatalogService(CvcFilterOptions? options = null)
        {
            _options = options ?? CvcFilterOptions.Default;
        }

        public IReadOnlyList<string> GetDuctSystemTypeNames(Document source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new FilteredElementCollector(source)
                .OfCategory(BuiltInCategory.OST_DuctSystem)
                .WhereElementIsElementType()
                .Select(element => element.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCulture)
                .ToList();
        }

        public IReadOnlyList<string> GetUserWorksetNames(Document host)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (!host.IsWorkshared)
            {
                return new string[0];
            }

            return new FilteredWorksetCollector(host)
                .OfKind(WorksetKind.UserWorkset)
                .Select(workset => workset.Name)
                .OrderBy(name => name, StringComparer.CurrentCulture)
                .ToList();
        }

        public IReadOnlyList<string> GetMaterialNames(Document source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var names = new List<string>();

            var equipment = new FilteredElementCollector(source)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType();

            foreach (var element in equipment)
            {
                var value = element.LookupParameter(_options.MaterialNameParameterName)?.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    names.Add(value!);
                }
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCulture)
                .ToList();
        }

        public IReadOnlyList<string> GetSystemInstanceNames(
            Document source, IEnumerable<string> systemTypeNames)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var wantedTypes = new HashSet<string>(
                systemTypeNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (wantedTypes.Count == 0)
            {
                return new string[0];
            }

            var instanceNames = new List<string>();

            var systems = new FilteredElementCollector(source)
                .OfCategory(BuiltInCategory.OST_DuctSystem)
                .WhereElementIsNotElementType();

            foreach (var system in systems)
            {
                var typeName = source.GetElement(system.GetTypeId())?.Name;
                if (typeName is not null && wantedTypes.Contains(typeName))
                {
                    instanceNames.Add(system.Name);
                }
            }

            return instanceNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCulture)
                .ToList();
        }

        public SystemCatalog BuildCatalog(Document host, Document linkedSource)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (linkedSource is null)
            {
                throw new ArgumentNullException(nameof(linkedSource));
            }

            return new SystemCatalog(
                GetDuctSystemTypeNames(linkedSource),
                GetUserWorksetNames(host),
                GetMaterialNames(linkedSource));
        }
    }
}
