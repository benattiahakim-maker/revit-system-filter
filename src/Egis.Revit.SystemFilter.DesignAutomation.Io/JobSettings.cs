using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>
    /// Contenu de <c>settings.json</c> (argument <c>settings</c>, facultatif).
    /// Clés acceptées : schemaVersion ("1.x"), mainModelPath, outputFileName,
    /// includeDuctSystems, includePipingSystems. Toute autre clé est refusée.
    /// </summary>
    public sealed class JobSettings
    {
        public const string DefaultOutputFileName = "systems-inventory.json";
        public const int MaxSettingsBytes = 64 * 1024;
        private static readonly string[] Allowed = { "schemaVersion", "mainModelPath", "hostModel", "outputFileName", "includeDuctSystems", "includePipingSystems" };

        public string SchemaVersion { get; private set; } = "1.0";
        public string? MainModelPath { get; private set; }
        public string OutputFileName { get; private set; } = DefaultOutputFileName;
        public bool IncludeDuctSystems { get; private set; } = true;
        public bool IncludePipingSystems { get; private set; } = true;

        /// <summary>Lit settings.json ; fichier absent → réglages par défaut.</summary>
        public static JobSettings Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new JobSettings();
            if (new FileInfo(path).Length > MaxSettingsBytes) throw new InputArchiveException("settings.json dépasse 64 Kio.");
            return Parse(File.ReadAllText(path));
        }

        public static JobSettings Parse(string json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException) { throw new InputArchiveException("settings.json n'est pas un JSON valide."); }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new InputArchiveException("settings.json : objet JSON attendu.");
                var s = new JobSettings();
                foreach (var p in root.EnumerateObject())
                {
                    var key = Allowed.FirstOrDefault(a => string.Equals(a, p.Name, StringComparison.OrdinalIgnoreCase));
                    if (key is null) throw new InputArchiveException($"settings.{Trunc(p.Name)} non pris en charge.");
                    if (p.Value.ValueKind == JsonValueKind.Null) continue;
                    switch (key)
                    {
                        case "schemaVersion":
                            var v = String(p, key);
                            if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^1\\.[0-9]+$")) throw new InputArchiveException("settings.schemaVersion invalide (1.x attendu).");
                            s.SchemaVersion = v; break;
                        case "mainModelPath":
                        case "hostModel": // alias de mainModelPath
                            s.MainModelPath = PathRules.NormalizeRelative(String(p, key));
                            if (!s.MainModelPath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) throw new InputArchiveException("settings.mainModelPath doit désigner un .rvt.");
                            break;
                        case "outputFileName":
                            var name = String(p, key);
                            if (name.Length > 100 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                || !PathRules.TryNormalizeRelative(name, out _) || string.Equals(name, RunLog.FileName, StringComparison.OrdinalIgnoreCase))
                                throw new InputArchiveException("settings.outputFileName invalide (nom de fichier .json simple).");
                            s.OutputFileName = name; break;
                        case "includeDuctSystems": s.IncludeDuctSystems = Bool(p, key); break;
                        case "includePipingSystems": s.IncludePipingSystems = Bool(p, key); break;
                    }
                }
                return s;
            }
        }

        /// <summary>
        /// Détermine le modèle principal parmi les fichiers du ZIP : mainModelPath s'il est fourni
        /// (doit exister, comparaison insensible à la casse), sinon l'unique .rvt à la racine.
        /// </summary>
        public string ResolveMainModel(IReadOnlyCollection<string> entries)
        {
            if (MainModelPath != null)
            {
                var match = entries.FirstOrDefault(e => string.Equals(e, MainModelPath, StringComparison.OrdinalIgnoreCase));
                return match ?? throw new InputArchiveException("settings.mainModelPath ne désigne aucune entrée du ZIP.");
            }
            var rootRvt = entries.Where(e => !e.Contains('/') && e.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)).ToList();
            if (rootRvt.Count == 1) return rootRvt[0];
            if (rootRvt.Count == 0) throw new InputArchiveException("Aucun .rvt à la racine du ZIP : renseigner settings.mainModelPath.");
            throw new InputArchiveException("Plusieurs .rvt à la racine du ZIP : renseigner settings.mainModelPath.");
        }

        private static string String(JsonProperty p, string key) =>
            p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : throw new InputArchiveException($"settings.{key} doit être une chaîne.");

        private static bool Bool(JsonProperty p, string key) =>
            p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.Value.GetBoolean() : throw new InputArchiveException($"settings.{key} doit être un booléen.");

        private static string Trunc(string s) => s.Length > 32 ? s.Substring(0, 32) : s;
    }
}
