using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>
    /// Orchestration sans Revit du workitem : préparation de l'entrée et écriture de la sortie.
    /// <list type="bullet">
    /// <item><c>input</c> : dossier (inputZip décompressé par Design Automation, <c>zip: true</c>) ou fichier ZIP (extrait ici, de façon sûre).</item>
    /// <item><c>settings.json</c> : facultatif.</item>
    /// <item><c>result/</c> : dossier zippé par Design Automation (<c>zip: true</c>) → output.zip.</item>
    /// </list>
    /// </summary>
    public sealed class JobWorkspace
    {
        public const string InputName = "input";
        public const string SettingsName = "settings.json";
        public const string ResultName = "result";
        private const string ExtractedName = "input-extracted";

        public string WorkingDirectory { get; }
        public RunLog Log { get; } = new RunLog();
        public JobSettings Settings { get; private set; } = new JobSettings();
        public string ExtractedRoot { get; private set; } = string.Empty;
        public IReadOnlyList<string> Entries { get; private set; } = Array.Empty<string>();
        public string MainModelRelative { get; private set; } = string.Empty;
        public string MainModelFullPath => PathRules.Contain(ExtractedRoot, MainModelRelative);
        public string ResultDirectory => Path.Combine(WorkingDirectory, ResultName);

        public JobWorkspace(string workingDirectory) { WorkingDirectory = Path.GetFullPath(workingDirectory); }

        /// <summary>Lit settings.json, contrôle/extrait l'entrée et désigne le modèle principal.</summary>
        public void Prepare(ArchiveLimits? limits = null)
        {
            Directory.CreateDirectory(ResultDirectory);
            Settings = JobSettings.Load(Path.Combine(WorkingDirectory, SettingsName));
            Log.Info($"settings.json {(File.Exists(Path.Combine(WorkingDirectory, SettingsName)) ? "lu" : "absent : réglages par défaut")}");

            var input = Path.Combine(WorkingDirectory, InputName);
            if (Directory.Exists(input))
            {
                ExtractedRoot = input;
                Entries = SafeZipExtractor.ScanDirectory(input, limits);
            }
            else if (File.Exists(input) || File.Exists(input + ".zip"))
            {
                var zip = File.Exists(input) ? input : input + ".zip";
                ExtractedRoot = Path.Combine(WorkingDirectory, ExtractedName);
                Entries = SafeZipExtractor.Extract(zip, ExtractedRoot, limits);
            }
            else
            {
                throw new InputArchiveException("Argument inputZip absent.");
            }

            Log.Info($"inputZip : {Entries.Count} fichier(s)");
            MainModelRelative = Settings.ResolveMainModel(Entries);
            Log.Info($"modèle principal : {MainModelRelative}");
        }

        public LinkResolver CreateLinkResolver() => new LinkResolver(ExtractedRoot, Entries, MainModelRelative);

        /// <summary>Écrit l'inventaire (octets JSON produits par l'export) dans result/.</summary>
        public string WriteInventory(byte[] json)
        {
            Directory.CreateDirectory(ResultDirectory);
            var path = Path.Combine(ResultDirectory, Settings.OutputFileName);
            File.WriteAllBytes(path, json);
            return path;
        }

        /// <summary>Écrit run.log dans result/ (toujours, y compris en cas d'échec).</summary>
        public void WriteRunLog()
        {
            Directory.CreateDirectory(ResultDirectory);
            File.WriteAllText(Path.Combine(ResultDirectory, RunLog.FileName), Log.ToString());
        }

        /// <summary>
        /// Zippe result/ à plat (usage local et tests ; sur APS, c'est Design Automation qui zippe).
        /// </summary>
        public static void ZipResult(string resultDirectory, string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(resultDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
    }
}
