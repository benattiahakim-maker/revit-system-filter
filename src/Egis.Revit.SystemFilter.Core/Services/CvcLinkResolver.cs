using System;
using System.Linq;
using Autodesk.Revit.DB;
using Egis.Revit.SystemFilter.Core.Abstractions;
using Egis.Revit.SystemFilter.Core.Configuration;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <inheritdoc cref="ICvcLinkResolver"/>
    public sealed class CvcLinkResolver : ICvcLinkResolver
    {
        private readonly CvcFilterOptions _options;

        public CvcLinkResolver(CvcFilterOptions? options = null)
        {
            _options = options ?? CvcFilterOptions.Default;
        }

        public Document? ResolveLinkedDocument(Document host)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            var links = new FilteredElementCollector(host)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>();

            foreach (var link in links)
            {
                if (link.Name.IndexOf(_options.LinkNameToken, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var linkedDocument = link.GetLinkDocument();
                if (linkedDocument is not null)
                {
                    return linkedDocument;
                }
            }

            return null;
        }
    }
}
