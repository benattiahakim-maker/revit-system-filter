using System;
using System.Collections.Generic;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>
    /// Attribue les ids de <c>rooms[]</c> (entiers uniques dans le fichier) : les ElementId Revit de deux maquettes
    /// liées peuvent être identiques, donc l'id exporté est un compteur 1, 2, 3… par couple (modelKey, revitId).
    /// Redemander le même couple renvoie le même id. Sans dépendance Revit.
    /// </summary>
    public sealed class RoomIdAllocator
    {
        private readonly Dictionary<(string, long), long> _ids = new Dictionary<(string, long), long>();

        public long Allocate(string modelKey, long revitId)
        {
            if (string.IsNullOrEmpty(modelKey)) throw new ArgumentException("modelKey requis.", nameof(modelKey));
            if (!_ids.TryGetValue((modelKey, revitId), out var id))
            {
                id = _ids.Count + 1;
                _ids[(modelKey, revitId)] = id;
            }
            return id;
        }

        public int Count => _ids.Count;
    }

    /// <summary>Clés de <c>models[]</c> : "main" pour l'hôte, "link-&lt;ElementId de l'instance&gt;" pour un lien (une clé par instance, unique).</summary>
    public static class ModelKeys
    {
        public const string Host = "main";

        public static string ForLinkInstance(long instanceElementId) => "link-" + instanceElementId;
    }
}
