using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.DaManager.Services;

/// <summary>Versions pointées par les alias <c>test</c> et <c>prod</c> d'une ressource (null : alias absent).</summary>
public sealed record AliasVersions(int? Test, int? Prod);

/// <summary>Versions que l'alias concerné pointait avant la publication, cible du retour arrière (null : aucune).</summary>
public sealed record PreviousVersions(int? AppBundle, int? Activity);

/// <summary>
/// Ligne du journal de publication (docs/PUBLISHING.md section 8) : Date | Commit | Revit | Alias | AppBundle test / prod |
/// Activity test / prod | Version précédente de l'alias (AppBundle / Activity). <see cref="Alias"/> vaut <c>test</c>
/// (publication en test) ou <c>prod</c> (promotion). <see cref="Id"/> identifie la publication pour la fusion.
/// </summary>
public sealed record JournalEntry(string Date, string? Commit, string Revit, string Alias, AliasVersions AppBundle, AliasVersions Activity, PreviousVersions? Previous)
{
    private readonly string? _id;

    /// <summary>
    /// Identifiant stable de la publication, clé de fusion du journal partagé. Par défaut (<see cref="DefaultId"/>) :
    /// date, version Revit, alias et versions que l'alias pointe après la publication, par exemple
    /// <c>2026-10-09/R2026/test/ab4/act4</c>. Un identifiant lu dans le fichier est conservé tel quel.
    /// </summary>
    public string Id
    {
        get => _id ?? DefaultId;
        init => _id = string.IsNullOrWhiteSpace(value) || value.Trim() == DefaultId ? null : value.Trim();
    }

    /// <summary>Identifiant calculé depuis le contenu ; les entrées sans « id » (formats antérieurs, Markdown) l'utilisent.</summary>
    public string DefaultId
    {
        get
        {
            var prod = Alias == DeploymentService.ProdAlias;
            var bundle = prod ? AppBundle.Prod : AppBundle.Test;
            var activity = prod ? Activity.Prod : Activity.Test;
            return $"{Date}/R{Revit}/{Alias}/ab{bundle?.ToString(CultureInfo.InvariantCulture) ?? "-"}/act{activity?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
        }
    }

    /// <summary>Toutes les versions citées par la ligne pour un type de ressource (à conserver pour le retour arrière).</summary>
    public IEnumerable<int> Versions(DaResourceKind kind)
    {
        var pair = kind == DaResourceKind.AppBundle ? AppBundle : Activity;
        var previous = kind == DaResourceKind.AppBundle ? Previous?.AppBundle : Previous?.Activity;
        return new[] { pair.Test, pair.Prod, previous }.OfType<int>();
    }

    public string Text =>
        $"{Date} · {Commit ?? "—"} · Revit {Revit} · {Alias} · AppBundle {PublicationJournal.Pair(AppBundle.Test, AppBundle.Prod)} · " +
        $"Activity {PublicationJournal.Pair(Activity.Test, Activity.Prod)} · {Alias} précédent {(Previous is null ? "—" : PublicationJournal.Pair(Previous.AppBundle, Previous.Activity))}";
}

/// <summary>
/// Journal de publication lisible : JSON indenté (une entrée par version Revit et par publication, en test comme en
/// prod) et table Markdown de docs/PUBLISHING.md section 8, en lecture comme en écriture. Le nettoyage des versions
/// protège automatiquement toute version citée par le journal.
/// Compatibilité : la version 1 (avant le journal des publications en test : pas d'« alias », « previousProd ») et
/// l'ancienne table à 6 colonnes restent lisibles ; leurs lignes sont des promotions (alias <c>prod</c>).
/// Journal partagé (dossier OneDrive) : <see cref="SaveMerged"/> relit le fichier juste avant d'écrire et fusionne par
/// <see cref="JournalEntry.Id"/>, pour ne pas écraser les lignes ajoutées entre-temps par un autre poste.
/// </summary>
public sealed class PublicationJournal
{
    public const string Format = "egsf-publication-journal";
    public const int FormatVersion = 2;

    /// <summary>Format initial : promotions seulement, sans alias, prod précédente dans « previousProd ».</summary>
    public const int LegacyFormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] Header = ["Date", "Commit", "Revit", "Alias", "AppBundle test / prod", "Activity test / prod", "Version précédente de l'alias (AppBundle / Activity)"];

    private const int LegacyColumnCount = 6;

    public PublicationJournal(IEnumerable<JournalEntry>? entries = null)
    {
        // Première occurrence d'un identifiant conservée : les lignes déjà au journal priment sur les nouvelles.
        Entries = [.. (entries ?? []).DistinctBy(e => e.Id, StringComparer.Ordinal).OrderBy(e => e.Date, StringComparer.Ordinal).ThenBy(e => e.Revit, StringComparer.Ordinal)];
    }

    public IReadOnlyList<JournalEntry> Entries { get; }

    /// <summary>Versions à protéger pour une ressource : toutes celles que le journal cite pour cette version Revit.</summary>
    public IReadOnlyList<int> ProtectedVersions(string revitVersion, DaResourceKind kind) =>
        [.. Entries.Where(e => e.Revit == revitVersion).SelectMany(e => e.Versions(kind)).Distinct().Order()];

    /// <summary>
    /// Fusion sans doublon par <see cref="JournalEntry.Id"/> : une ligne dont l'identifiant figure déjà au journal est
    /// ignorée (la ligne existante est gardée).
    /// </summary>
    public PublicationJournal Merge(IEnumerable<JournalEntry> entries) => new(Entries.Concat(entries));

    public PublicationJournal Add(JournalEntry entry) => Merge([entry]);

    // ---- Fichiers ------------------------------------------------------------------------------------------------

    /// <summary>Journal du fichier, ou journal vide si le fichier n'existe pas. Un fichier illisible lève une erreur.</summary>
    public static PublicationJournal Load(string path) =>
        File.Exists(path) ? Import(path, File.ReadAllText(path)) : new PublicationJournal();

    /// <summary>Lecture selon l'extension : <c>.md</c> (table de PUBLISHING section 8), sinon JSON.</summary>
    public static PublicationJournal Import(string path, string text) =>
        IsMarkdown(path) ? ParseMarkdown(text) : ParseJson(text);

    /// <summary>
    /// Écriture atomique (fichier temporaire unique dans le même dossier, puis remplacement) ; <c>.md</c> produit la
    /// table Markdown, toute autre extension le JSON. Remplace le fichier tel quel : pour le journal partagé, passer par
    /// <see cref="SaveMerged"/>.
    /// </summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = $"{full}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, IsMarkdown(full) ? ToMarkdown() : ToJson(), new UTF8Encoding(false));
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// Écriture dans un journal partagé : relit le fichier juste avant d'écrire, ajoute les lignes de ce journal dont
    /// l'identifiant n'y figure pas encore, puis écrit (<see cref="Save"/>). Les lignes ajoutées par un autre poste
    /// depuis le chargement sont donc conservées. Reste une fenêtre très courte (relecture → remplacement) où deux
    /// écritures simultanées peuvent se perdre : dernier écrit gagnant. Renvoie le journal écrit.
    /// </summary>
    public PublicationJournal SaveMerged(string path)
    {
        var merged = Load(path).Merge(Entries);
        merged.Save(path);
        return merged;
    }

    // ---- JSON ----------------------------------------------------------------------------------------------------

    public string ToJson()
    {
        var entries = new JsonArray();
        foreach (var entry in Entries)
        {
            entries.Add(new JsonObject
            {
                ["id"] = entry.Id,
                ["date"] = entry.Date,
                ["commit"] = entry.Commit,
                ["revit"] = entry.Revit,
                ["alias"] = entry.Alias,
                ["appBundle"] = new JsonObject { ["test"] = entry.AppBundle.Test, ["prod"] = entry.AppBundle.Prod },
                ["activity"] = new JsonObject { ["test"] = entry.Activity.Test, ["prod"] = entry.Activity.Prod },
                ["previous"] = entry.Previous is null ? null : new JsonObject { ["appBundle"] = entry.Previous.AppBundle, ["activity"] = entry.Previous.Activity },
            });
        }

        var root = new JsonObject { ["format"] = Format, ["version"] = FormatVersion, ["entries"] = entries };
        return root.ToJsonString(WriteOptions) + "\n";
    }

    public static PublicationJournal ParseJson(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Journal JSON illisible : {exception.Message}");
        }

        if (root is not JsonObject obj || obj["format"]?.ToString() != Format)
        {
            throw new InvalidOperationException($"Journal JSON : « format » doit valoir « {Format} ».");
        }

        var version = Number(obj["version"], "version");
        if (version is not (FormatVersion or LegacyFormatVersion))
        {
            throw new InvalidOperationException($"Journal JSON : version {version} non prise en charge (attendues : {LegacyFormatVersion} ou {FormatVersion}).");
        }

        var legacy = version == LegacyFormatVersion;

        var entries = new List<JournalEntry>();
        var index = 0;
        foreach (var node in obj["entries"] as JsonArray ?? throw new InvalidOperationException("Journal JSON : tableau « entries » manquant."))
        {
            index++;
            try
            {
                var e = node as JsonObject ?? throw new InvalidOperationException("objet attendu");
                var previous = e[legacy ? "previousProd" : "previous"] is JsonObject p ? new PreviousVersions(Version(p["appBundle"]), Version(p["activity"])) : null;
                entries.Add(Validate(new JournalEntry(
                    Text(e["date"]) ?? throw new InvalidOperationException("« date » manquante"),
                    Text(e["commit"]),
                    Text(e["revit"]) ?? throw new InvalidOperationException("« revit » manquant"),
                    legacy ? DeploymentService.ProdAlias : Text(e["alias"]) ?? throw new InvalidOperationException("« alias » manquant"),
                    ReadPair(e["appBundle"], "appBundle"),
                    ReadPair(e["activity"], "activity"),
                    previous)) with { Id = Text(e["id"]) ?? string.Empty });
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                throw new InvalidOperationException($"Journal JSON, entrée {index} : {exception.Message}");
            }
        }

        return new PublicationJournal(entries);
    }

    // ---- Markdown ------------------------------------------------------------------------------------------------

    /// <summary>Table de docs/PUBLISHING.md section 8 (à coller dans une description de PR, une release ou un ticket).</summary>
    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.Append("| ").AppendJoin(" | ", Header).Append(" |\n");
        builder.Append(string.Concat(Enumerable.Repeat("| --- ", Header.Length))).Append("|\n");
        foreach (var e in Entries)
        {
            builder.Append($"| {e.Date} | {e.Commit ?? "—"} | {e.Revit} | {e.Alias} | {Pair(e.AppBundle.Test, e.AppBundle.Prod)} | {Pair(e.Activity.Test, e.Activity.Prod)} | ")
                .Append(e.Previous is null ? "—" : Pair(e.Previous.AppBundle, e.Previous.Activity))
                .Append(" |\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Lit les lignes de la table (en-tête et séparateur ignorés ; le texte autour de la table aussi). Une ligne à
    /// 6 colonnes (ancienne table, sans « Alias ») est une promotion.
    /// </summary>
    public static PublicationJournal ParseMarkdown(string markdown)
    {
        var entries = new List<JournalEntry>();
        var lineNumber = 0;
        foreach (var raw in markdown.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim().Trim('`')).ToArray();
            if (cells[0] == Header[0] || cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ':')))
            {
                continue;
            }

            try
            {
                if (cells.Length != Header.Length && cells.Length != LegacyColumnCount)
                {
                    throw new InvalidOperationException($"{Header.Length} colonnes attendues ({LegacyColumnCount} pour l'ancienne table), {cells.Length} trouvée(s)");
                }

                var legacy = cells.Length == LegacyColumnCount;
                var offset = legacy ? 0 : 1;
                var (bundleTest, bundleProd) = ParsePair(cells[3 + offset]);
                var (activityTest, activityProd) = ParsePair(cells[4 + offset]);
                PreviousVersions? previous = null;
                if (!IsEmpty(cells[5 + offset]))
                {
                    var (previousBundle, previousActivity) = ParsePair(cells[5 + offset]);
                    previous = new PreviousVersions(previousBundle, previousActivity);
                }

                entries.Add(Validate(new JournalEntry(
                    cells[0],
                    IsEmpty(cells[1]) ? null : cells[1],
                    cells[2],
                    legacy ? DeploymentService.ProdAlias : cells[3],
                    new AliasVersions(bundleTest, bundleProd),
                    new AliasVersions(activityTest, activityProd),
                    previous)));
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                throw new InvalidOperationException($"Journal Markdown, ligne {lineNumber} : {exception.Message}");
            }
        }

        return new PublicationJournal(entries);
    }

    internal static string Pair(int? first, int? second) => $"{Show(first)} / {Show(second)}";

    // ---- Interne -------------------------------------------------------------------------------------------------

    private static JournalEntry Validate(JournalEntry entry)
    {
        if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException($"date « {entry.Date} » invalide (AAAA-MM-JJ attendu)");
        }

        if (!DeploymentService.SupportedVersions.Contains(entry.Revit))
        {
            throw new InvalidOperationException($"version Revit « {entry.Revit} » inconnue ({string.Join(", ", DeploymentService.SupportedVersions)})");
        }

        if (entry.Alias is not (DeploymentService.TestAlias or DeploymentService.ProdAlias))
        {
            throw new InvalidOperationException($"alias « {entry.Alias} » inconnu ({DeploymentService.TestAlias} ou {DeploymentService.ProdAlias})");
        }

        return entry;
    }

    private static (int?, int?) ParsePair(string cell)
    {
        var parts = cell.Split('/');
        return parts.Length == 2
            ? (ParseVersion(parts[0]), ParseVersion(parts[1]))
            : throw new InvalidOperationException($"« {cell} » : deux versions « test / prod » attendues");
    }

    private static int? ParseVersion(string text)
    {
        var value = text.Trim();
        if (IsEmpty(value))
        {
            return null;
        }

        return int.TryParse(value.TrimStart('v', 'V'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            ? version
            : throw new InvalidOperationException($"version « {value} » invalide");
    }

    private static bool IsEmpty(string text) => text is "" or "-" or "—" or "–" || text.StartsWith("aucun", StringComparison.OrdinalIgnoreCase);

    private static AliasVersions ReadPair(JsonNode? node, string name) =>
        node is JsonObject o ? new AliasVersions(Version(o["test"]), Version(o["prod"])) : throw new InvalidOperationException($"objet « {name} » manquant");

    private static int? Version(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var version = Number(node, "version");
        return version >= 1 ? version : throw new InvalidOperationException($"version {version} invalide");
    }

    private static int Number(JsonNode? node, string name) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new InvalidOperationException($"« {name} » : entier attendu");

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static string Show(int? version) => version is null ? "—" : $"v{version}";

    private static bool IsMarkdown(string path) => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase);
}
