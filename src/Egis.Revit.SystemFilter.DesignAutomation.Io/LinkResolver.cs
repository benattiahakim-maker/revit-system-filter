using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>
    /// Résout le chemin enregistré d'un lien Revit vers un fichier DU DOSSIER EXTRAIT uniquement.
    /// Ordre : même chemin relatif au modèle principal (s'il reste dans le dossier), puis nom de
    /// fichier unique dans le ZIP. Jamais de chemin hors du dossier extrait.
    /// </summary>
    public sealed class LinkResolver
    {
        private readonly string _root;
        private readonly IReadOnlyCollection<string> _entries;
        private readonly string _mainModel;

        public LinkResolver(string extractedRoot, IReadOnlyCollection<string> entries, string mainModelRelative)
        {
            _root = Path.GetFullPath(extractedRoot);
            _entries = entries;
            _mainModel = mainModelRelative;
        }

        /// <summary>Chemin relatif ('/') du fichier retenu, ou null si le lien est introuvable dans le ZIP.</summary>
        public string? Resolve(string? savedPath) => Resolve(savedPath, out _);

        /// <summary>Comme <see cref="Resolve(string?)"/> ; <paramref name="byName"/> vaut vrai si résolu à la 2e étape (nom de fichier).</summary>
        public string? Resolve(string? savedPath, out bool byName)
        {
            byName = false;
            if (string.IsNullOrWhiteSpace(savedPath)) return null;
            var saved = savedPath.Replace('\\', '/');

            // 1. Chemin relatif au dossier du modèle principal, s'il ne sort pas du dossier extrait.
            if (!saved.StartsWith("/", StringComparison.Ordinal) && !(saved.Length >= 2 && saved[1] == ':'))
            {
                var mainDir = Path.GetDirectoryName(_mainModel.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
                var candidate = Path.GetFullPath(Path.Combine(_root, mainDir, saved.Replace('/', Path.DirectorySeparatorChar)));
                if (PathRules.IsInside(_root, candidate))
                {
                    var rel = Path.GetRelativePath(_root, candidate).Replace('\\', '/');
                    var hit = _entries.FirstOrDefault(e => string.Equals(e, rel, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) return hit;
                }
            }

            // 2. Nom de fichier unique dans le ZIP (hors modèle principal).
            var fileName = saved.Split('/').Last();
            if (fileName.Length == 0) return null;
            var matches = _entries.Where(e => !string.Equals(e, _mainModel, StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(e.Split('/').Last(), fileName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) return null;
            byName = true;
            return matches[0];
        }

        /// <summary>Ligne run.log d'un lien résolu (« résolu par nom » pour la 2e étape).</summary>
        public static string ResolvedMessage(string relative, bool byName) =>
            byName ? $"lien chargé : {relative} (résolu par nom)" : $"lien chargé : {relative}";

        /// <summary>Chemin complet (dans le dossier extrait) d'une entrée résolue.</summary>
        public string FullPath(string relative) => PathRules.Contain(_root, relative);

        /// <summary>Nom affichable d'un lien : nom de fichier seul, jamais le chemin enregistré complet.</summary>
        public static string DisplayName(string? savedPath) =>
            string.IsNullOrWhiteSpace(savedPath) ? "(sans nom)" : savedPath.Replace('\\', '/').Split('/').Last();
    }
}
