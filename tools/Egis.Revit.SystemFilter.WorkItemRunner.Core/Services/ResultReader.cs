using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>Ligne de pièce affichée : tous les champs de <c>systems[]</c>, les paramètres partagés réunis en un texte.</summary>
public sealed record SystemRow(
    string? ElementId,
    string? Number,
    string? Name,
    string? Level,
    string? Occupancy,
    double? AreaM2,
    double? UnboundedHeightM,
    double? VolumeM3,
    string? VolumeStatus,
    string? FinishFloor,
    string? FinishWall,
    string? FinishCeiling,
    string? FinishBase,
    string? Phase,
    string? DesignOption,
    string? SharedParameters,
    string QualityFlags);

/// <summary>Ligne d'anomalie affichée.</summary>
public sealed record AnomalyRow(string Code, string Severity, string Scope, string? ElementId, string Message);

/// <summary>Lecture de <c>result.json</c> : bilan, pièces, anomalies et écarts au schéma.</summary>
public sealed record ResultView(
    string? Status,
    IReadOnlyList<KeyValuePair<string, string>> Summary,
    IReadOnlyList<SystemRow> Systems,
    IReadOnlyList<AnomalyRow> Anomalies,
    IReadOnlyList<string> SchemaErrors)
{
    public bool IsSchemaValid => SchemaErrors.Count == 0;

    /// <summary>
    /// Colonnes du tableau des pièces, déduites des clés de <c>systems[]</c> (union dans l'ordre d'apparition) : les objets
    /// imbriqués sont aplatis (<c>finitions.sol</c>), une référence d'élément (niveau, phase, variante) montre son nom,
    /// un paramètre partagé sa valeur. Un nouveau champ du contrat apparaît sans modifier l'outil.
    /// </summary>
    public IReadOnlyList<string> SystemColumns { get; init; } = [];

    /// <summary>Valeurs du tableau des pièces : une ligne par pièce, une cellule par colonne de <see cref="SystemColumns"/>.</summary>
    public IReadOnlyList<IReadOnlyList<string?>> SystemValues { get; init; } = [];
}

/// <summary>
/// Lecture et validation de <c>result.json</c> contre <c>docs/schema/result.schema.json</c> (draft 2020-12, formats
/// vérifiés, comme <c>src/Tests/Support/ResultSchema.cs</c>).
/// </summary>
public sealed class ResultReader(string schemaPath)
{
    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List, RequireFormatValidation = true };
    private readonly Lazy<JsonSchema> _schema = new(() => JsonSchema.FromText(
        File.ReadAllText(schemaPath),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() })); // registre propre : le $id peut être chargé plusieurs fois

    public static ResultReader ForRepository(string repositoryRoot) =>
        new(Path.Combine(repositoryRoot, "docs", "schema", "result.schema.json"));

    public ResultView Read(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            return new ResultView(null, [], [], [], [$"JSON illisible : {exception.Message}"]);
        }

        var summary = root?["summary"];
        var counts = summary?["counts"];
        var rows = new List<KeyValuePair<string, string>>
        {
            new("Statut", Text(summary?["status"])),
            new("Schéma du résultat", Text(root?["schemaVersion"])),
            new("Extracteur", Text(summary?["extractorVersion"])),
            new("Revit", $"{Text(summary?["revit"]?["version"])} ({Text(summary?["revit"]?["build"])})"),
            new("Engine", Text(summary?["apsEngine"])),
            new("Maquette", Text(summary?["source"]?["relativePath"])),
            new("Début / fin (UTC)", $"{Text(summary?["startedAtUtc"])} / {Text(summary?["completedAtUtc"])}"),
            new("Pièces collectées / exportées / avec anomalies", $"{Text(counts?["systemsCollected"])} / {Text(counts?["systemsExported"])} / {Text(counts?["systemsWithAnomalies"])}"),
            new("Corrélation", Text(summary?["correlationId"])),
        };
        if (summary?["images"] is JsonObject images)
        {
            rows.Add(new("Images (demandées / exportées / en échec)", $"{Text(images["requested"])} / {Text(images["exported"])} / {Text(images["failed"])}"));
        }

        foreach (var error in summary?["errors"]?.AsArray() ?? [])
        {
            rows.Add(new("Erreur", $"{Text(error?["code"])} ({Text(error?["stage"])}) : {Text(error?["message"])}"));
        }

        var systems = (root?["systems"]?.AsArray() ?? []).Select(r => new SystemRow(
            r?["id_revit"]?.ToString(),
            r?["numero"]?.ToString(),
            r?["nom"]?.ToString(),
            NamedRef(r?["niveau"]),
            r?["occupation"]?.ToString(),
            Number(r?["surface_m2"]),
            Number(r?["hauteur_non_limitee_m"]),
            Number(r?["volume_m3"]),
            r?["statut_volume"]?.ToString(),
            r?["finitions"]?["sol"]?.ToString(),
            r?["finitions"]?["murs"]?.ToString(),
            r?["finitions"]?["plafond"]?.ToString(),
            r?["finitions"]?["plinthe"]?.ToString(),
            NamedRef(r?["phase"]),
            NamedRef(r?["variante"]),
            SharedParameters(r?["parametres_partages"]),
            string.Join(", ", (r?["indicateurs_qualite"]?.AsArray() ?? []).Select(f => f?.ToString())))).ToList();
        var anomalies = (root?["anomalies"]?.AsArray() ?? []).Select(a => new AnomalyRow(
            Text(a?["code"]),
            Text(a?["severity"]),
            Text(a?["scope"]),
            a?["elementId"]?.ToString(),
            Text(a?["message"]))).ToList();
        var (columns, values) = BuildSystemTable(root?["systems"]?.AsArray());
        return new ResultView(summary?["status"]?.ToString(), rows, systems, anomalies, Validate(json))
        {
            SystemColumns = columns,
            SystemValues = values,
        };
    }

    private static (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows) BuildSystemTable(JsonArray? systems)
    {
        var columns = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var flattened = new List<Dictionary<string, string?>>();
        foreach (var system in systems ?? [])
        {
            var cells = new Dictionary<string, string?>(StringComparer.Ordinal);
            Flatten(system, string.Empty, cells);
            foreach (var key in cells.Keys.Where(known.Add))
            {
                columns.Add(key);
            }

            flattened.Add(cells);
        }

        IReadOnlyList<IReadOnlyList<string?>> rows = flattened
            .Select(cells => (IReadOnlyList<string?>)columns.Select(column => cells.GetValueOrDefault(column)).ToArray())
            .ToList();
        return (columns, rows);
    }

    private static void Flatten(JsonNode? node, string path, Dictionary<string, string?> cells)
    {
        switch (node)
        {
            case JsonObject reference when path.Length > 0 && reference["nom"] is JsonValue name:
                cells[path] = name.ToString();
                break;
            case JsonObject parameter when path.Length > 0 && parameter.ContainsKey("guid") && parameter.ContainsKey("valeur"):
                cells[path] = parameter["valeur"] is JsonValue value ? Scalar(value) : null;
                break;
            case JsonObject obj:
                foreach (var property in obj)
                {
                    Flatten(property.Value, path.Length == 0 ? property.Key : $"{path}.{property.Key}", cells);
                }

                break;
            case JsonArray array when array.Any(item => item is JsonObject { } o && o.ContainsKey("discipline")):
                // Équipements et terminaux : une colonne par discipline (équipements.cvc, …), même vide, pour garder le détail.
                foreach (var discipline in Disciplines)
                {
                    cells[$"{path}.{discipline}"] = Summarize(new JsonArray(array
                        .OfType<JsonObject>()
                        .Where(item => item["discipline"]?.ToString() == discipline)
                        .Select(item => (JsonNode)item.DeepClone())
                        .ToArray()));
                }

                break;
            case JsonArray array when array.Any(item => item is JsonObject):
                cells[path] = Summarize(array);
                break;
            case JsonArray array:
                cells[path] = string.Join(", ", array.Select(item => item is JsonValue v ? Scalar(v) : item?.ToJsonString()));
                break;
            case JsonValue scalar:
                cells[path] = Scalar(scalar);
                break;
            default:
                cells[path] = null;
                break;
        }
    }

    private static readonly string[] Disciplines = ["cvc", "plomberie", "electricite", "autres"];

    /// <summary>Nombre maximal de repères cités dans la cellule d'un tableau d'éléments.</summary>
    private const int MaxLabels = 5;

    /// <summary>
    /// Cellule d'un tableau d'objets : le nombre d'éléments, et pour des éléments rattachés à la pièce (portes, équipements,
    /// terminaux) leurs repères (<c>repere</c>, sinon <c>type</c>, sinon <c>id_revit</c>). Le détail est dans le JSON exporté.
    /// </summary>
    private static string Summarize(JsonArray array)
    {
        var labels = array
            .OfType<JsonObject>()
            .Where(item => item.ContainsKey("id_revit"))
            .Select(item => new[] { item["repere"], item["type"], item["id_revit"] }
                .Select(node => node?.ToString())
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "?")
            .ToList();
        return labels.Count == 0
            ? array.Count == 0 ? "0" : $"{array.Count} élément(s)"
            : $"{array.Count} : {string.Join(", ", labels.Take(MaxLabels))}{(labels.Count > MaxLabels ? ", …" : string.Empty)}";
    }

    /// <summary>Texte d'une valeur JSON ; les nombres sont écrits en notation invariante, sans zéros inutiles.</summary>
    private static string Scalar(JsonValue value) =>
        value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var number)
            ? number.ToString("0.####", CultureInfo.InvariantCulture)
            : value.ToString();

    /// <summary>
    /// Lecture de <c>TRCValidationDict.json</c> (fiches de l'application TRC, données seules) : une ligne par objet BIM, une colonne par
    /// code de propriété (valeurs multiples réunies par « | »). Pas de schéma, de statut ni d'anomalies : il n'y en a pas dans ce format.
    /// </summary>
    public ResultView ReadTrc(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            return new ResultView(null, [], [], [], [$"JSON illisible : {exception.Message}"]);
        }

        var fiches = root?.AsArray().OfType<JsonObject>().ToList() ?? [];
        var objects = fiches.Where(f => f["model"]?.ToString() == "trc.bimobject").ToList();
        var bimIdByPk = objects.ToDictionary(o => o["pk"]!.ToString(), o => o["fields"]?["bim_id"]?.ToString() ?? string.Empty, StringComparer.Ordinal);

        var columns = new List<string> { "bim_id" };
        var known = new HashSet<string>(StringComparer.Ordinal) { "bim_id" };
        var cells = bimIdByPk.Keys.ToDictionary(pk => pk, pk => new Dictionary<string, List<string>>(StringComparer.Ordinal), StringComparer.Ordinal);
        var valueCount = 0;
        foreach (var fiche in fiches.Where(f => f["model"]?.ToString() == "trc.bimobjectpropertyvalue"))
        {
            var fields = fiche["fields"];
            var pk = fields?["bim_object_id"]?.ToString();
            var code = fields?["bim_property_code_property"]?.ToString();
            if (pk is null || code is null || !cells.TryGetValue(pk, out var row))
            {
                continue;
            }

            if (known.Add(code))
            {
                columns.Add(code);
            }

            if (!row.TryGetValue(code, out var list))
            {
                row[code] = list = [];
            }

            list.Add(fields?["latest_value"]?.ToString() ?? string.Empty);
            valueCount++;
        }

        IReadOnlyList<IReadOnlyList<string?>> values = bimIdByPk
            .Select(pair => (IReadOnlyList<string?>)columns
                .Select(column => column == "bim_id" ? pair.Value : cells[pair.Key].TryGetValue(column, out var list) ? string.Join(" | ", list) : null)
                .ToArray())
            .ToList();

        var codeFile = fiches.FirstOrDefault(f => f["model"]?.ToString() == "special.setcodefile")?["fields"]?["code_file"]?.ToString();
        var rows = new List<KeyValuePair<string, string>>
        {
            new("Format", "TRC (TRCValidationDict.json), données seules"),
            new("Fichier (code_file)", codeFile ?? "—"),
            new("Objets BIM / valeurs", $"{objects.Count} / {valueCount}"),
        };
        return new ResultView(null, rows, [], [], [])
        {
            SystemColumns = columns,
            SystemValues = values,
        };
    }

    public IReadOnlyList<string> Validate(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var results = _schema.Value.Evaluate(document.RootElement, Options);
            if (results.IsValid)
            {
                return [];
            }

            var errors = (results.Details ?? [])
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} : {e.Key} : {e.Value}"))
                .ToList();
            return errors.Count > 0 ? errors : ["<racine> : instance non conforme"];
        }
        catch (JsonException exception)
        {
            return [$"JSON illisible : {exception.Message}"];
        }
    }

    private static string Text(JsonNode? node) => node?.ToString() ?? "—";

    /// <summary>Référence d'élément (niveau, phase, variante) : son nom si c'est un objet, sinon sa valeur.</summary>
    private static string? NamedRef(JsonNode? node) =>
        node is JsonObject reference ? reference["nom"]?.ToString() : node?.ToString();

    /// <summary>Paramètres partagés d'une pièce : <c>clé=valeur</c>, séparés par « ; » (valeur absente : <c>—</c>).</summary>
    private static string? SharedParameters(JsonNode? node) =>
        node is JsonObject parameters && parameters.Count > 0
            ? string.Join("; ", parameters.Select(p => $"{p.Key}={p.Value?["valeur"]?.ToString() ?? "—"}"))
            : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number
        : node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;
}
