using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>
    /// Extraction sûre du ZIP d'entrée et contrôle d'un dossier déjà extrait (cas où
    /// Design Automation décompresse lui-même l'argument <c>inputZip</c>).
    /// </summary>
    public static class SafeZipExtractor
    {
        /// <summary>
        /// Extrait <paramref name="zipPath"/> dans <paramref name="destination"/> (vide ou absent).
        /// Refuse zip slip, chemins absolus, doublons (insensibles à la casse) et dépassement
        /// des plafonds ; la taille est comptée sur les octets réellement décompressés.
        /// Retourne les chemins relatifs des fichiers extraits ('/' comme séparateur).
        /// </summary>
        public static IReadOnlyList<string> Extract(string zipPath, string destination, ArchiveLimits? limits = null)
        {
            limits ??= ArchiveLimits.Default;
            Directory.CreateDirectory(destination);
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;

            ZipArchive archive;
            try { archive = ZipFile.OpenRead(zipPath); }
            catch (InvalidDataException) { throw new InputArchiveException("inputZip n'est pas un ZIP valide."); }

            using (archive)
            {
                if (archive.Entries.Count > limits.MaxEntries)
                    throw new InputArchiveException($"inputZip contient trop d'entrées (max {limits.MaxEntries}).");

                foreach (var entry in archive.Entries)
                {
                    var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal);
                    var relative = PathRules.NormalizeRelative(entry.FullName, limits.MaxPathLength);
                    if (!seen.Add(relative)) throw new InputArchiveException("inputZip contient deux entrées de même chemin.");
                    var target = PathRules.Contain(destination, relative);

                    if (isDirectory) { Directory.CreateDirectory(target); continue; }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += read;
                            if (total > limits.MaxTotalBytes)
                                throw new InputArchiveException($"inputZip dépasse la taille décompressée maximale ({limits.MaxTotalBytes} octets).");
                            output.Write(buffer, 0, read);
                        }
                    }
                    files.Add(relative);
                }
            }
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        /// <summary>
        /// Contrôle un dossier déjà extrait : mêmes plafonds, et refus de tout lien symbolique
        /// ou point d'analyse (qui pourrait pointer hors du dossier).
        /// </summary>
        public static IReadOnlyList<string> ScanDirectory(string root, ArchiveLimits? limits = null)
        {
            limits ??= ArchiveLimits.Default;
            var fullRoot = Path.GetFullPath(root);
            var files = new List<string>();
            long total = 0;
            int count = 0;
            var pending = new Stack<string>();
            pending.Push(fullRoot);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (++count > limits.MaxEntries)
                        throw new InputArchiveException($"inputZip contient trop d'entrées (max {limits.MaxEntries}).");
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget != null)
                        throw new InputArchiveException("Lien symbolique refusé dans inputZip.");
                    var relative = PathRules.NormalizeRelative(Path.GetRelativePath(fullRoot, info.FullName), limits.MaxPathLength);
                    if (info is DirectoryInfo) { pending.Push(info.FullName); continue; }
                    total += ((FileInfo)info).Length;
                    if (total > limits.MaxTotalBytes)
                        throw new InputArchiveException($"inputZip dépasse la taille décompressée maximale ({limits.MaxTotalBytes} octets).");
                    files.Add(relative);
                }
            }
            files.Sort(StringComparer.Ordinal);
            return files;
        }
    }
}
