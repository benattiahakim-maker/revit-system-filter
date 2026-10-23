using System;
using System.IO;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>Règles de chemins relatifs (zip slip, chemins absolus, confinement dans un dossier).</summary>
    public static class PathRules
    {
        /// <summary>
        /// Normalise un chemin d'entrée ('\' → '/') et le refuse s'il est vide, absolu,
        /// avec lettre de lecteur, UNC, segment "..", "." ou vide, ou caractère de contrôle.
        /// </summary>
        public static string NormalizeRelative(string? path, int maxLength = 259)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InputArchiveException("Chemin vide.");
            var p = path.Replace('\\', '/');
            if (p.EndsWith("/", StringComparison.Ordinal)) p = p.TrimEnd('/');
            if (p.Length == 0 || p.Length > maxLength) throw new InputArchiveException("Chemin vide ou trop long.");
            if (p.StartsWith("/", StringComparison.Ordinal) || (p.Length >= 2 && p[1] == ':'))
                throw new InputArchiveException("Chemin absolu refusé.");
            foreach (var c in p)
            {
                if (char.IsControl(c) || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    throw new InputArchiveException("Caractère interdit dans un chemin.");
            }
            foreach (var segment in p.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new InputArchiveException("Chemin relatif invalide (segment vide, '.' ou '..').");
                if (segment.EndsWith(" ", StringComparison.Ordinal) || segment.EndsWith(".", StringComparison.Ordinal))
                    throw new InputArchiveException("Segment de chemin terminé par un espace ou un point.");
            }
            return p;
        }

        public static bool TryNormalizeRelative(string? path, out string normalized)
        {
            try { normalized = NormalizeRelative(path); return true; }
            catch (InputArchiveException) { normalized = string.Empty; return false; }
        }

        /// <summary>Chemin complet sous <paramref name="root"/> ; refuse toute sortie du dossier.</summary>
        public static string Contain(string root, string relative)
        {
            var fullRoot = Path.GetFullPath(root);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar)) fullRoot += Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InputArchiveException("Chemin hors du dossier extrait refusé.");
            return full;
        }

        /// <summary>Vrai si <paramref name="fullPath"/> est strictement à l'intérieur de <paramref name="root"/>.</summary>
        public static bool IsInside(string root, string fullPath)
        {
            var fullRoot = Path.GetFullPath(root);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar)) fullRoot += Path.DirectorySeparatorChar;
            return Path.GetFullPath(fullPath).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
