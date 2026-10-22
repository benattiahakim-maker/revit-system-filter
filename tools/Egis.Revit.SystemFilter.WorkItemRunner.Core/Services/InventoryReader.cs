using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>
/// Vue de l'inventaire des systèmes (<c>systems-inventory.json</c>) : bilan, colonnes et arbre
/// type de système → système → éléments (gaines, raccords, accessoires, terminaux, équipements).
/// </summary>
public sealed record InventoryView(
    string? Status,
    IReadOnlyList<KeyValuePair<string, string>> Summary,
    IReadOnlyList<string> Columns,
    IReadOnlyList<TableRow> Tree,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Lecture de <c>systems-inventory.json</c> produit par l'export Design Automation. Les trois collections à plat
/// (<c>systemTypes</c>, <c>systems</c>, <c>elements</c>) reliées par clés sont remontées en arbre pour l'affichage.
/// </summary>
public sealed class InventoryReader
{
    /// <summary>Colonnes de l'arbre ; la première porte le nom de l'objet (avec retrait et flèche de dépliage).</summary>
    public static readonly IReadOnlyList<string> TreeColumns =
        ["Objet", "Nature", "Domaine", "Détail", "Niveau", "Dimension", "Longueur (mm)"];

    public InventoryView Read(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            return Failed($"JSON illisible : {exception.Message}");
        }

        if (root is not JsonObject model)
        {
            return Failed("JSON inattendu : objet attendu à la racine.");
        }

        var systemTypes = model["systemTypes"]?.AsArray() ?? [];
        var systems = model["systems"]?.AsArray() ?? [];
        var elements = model["elements"]?.AsArray() ?? [];

        var typeDomain = systemTypes
            .OfType<JsonObject>()
            .Where(t => t["id"] is not null)
            .GroupBy(t => Id(t["id"]))
            .ToDictionary(g => g.Key, g => Text(g.First()["domain"]));

        var systemsByType = systems.OfType<JsonObject>()
            .GroupBy(s => Id(s["systemTypeId"]))
            .ToDictionary(g => g.Key, g => g.OrderBy(s => Text(s["name"]), StringComparer.CurrentCulture).ToList());

        var elementsBySystem = elements.OfType<JsonObject>()
            .GroupBy(e => Id(e["systemId"]))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => Text(e["category"]), StringComparer.Ordinal)
                      .ThenBy(e => Text(e["name"]), StringComparer.CurrentCulture).ToList());

        var tree = new List<TableRow>();
        foreach (var type in systemTypes.OfType<JsonObject>().OrderBy(t => Text(t["name"]), StringComparer.CurrentCulture))
        {
            var typeId = Id(type["id"]);
            var domain = Domain(Text(type["domain"]));
            var typeSystems = systemsByType.GetValueOrDefault(typeId) ?? [];
            var systemRows = typeSystems.Select(system => SystemRow(system, domain, elementsBySystem)).ToList();
            var label = Text(type["name"]);
            var classification = type["classification"]?.GetValue<string>();
            tree.Add(new TableRow(
                [string.IsNullOrEmpty(classification) ? label : $"{label}  ·  {classification}", "Type de système", domain, $"{typeSystems.Count} système(s)", null, null, null],
                systemRows));
        }

        AppendOrphans(tree, systems, systemsByType, elementsBySystem, typeDomain);

        var summary = new List<KeyValuePair<string, string>>
        {
            new("Schéma", Text(model["schemaVersion"])),
            new("Maquette", Text(model["model"]?["title"])),
            new("Revit", Text(model["model"]?["revitVersion"])),
            new("Généré (UTC)", Text(model["generatedAtUtc"])),
            new("Types / systèmes / éléments",
                $"{Count(model, "systemTypeCount", systemTypes.Count)} / {Count(model, "systemCount", systems.Count)} / {Count(model, "elementCount", elements.Count)}"),
        };

        return new InventoryView(Text(model["model"]?["title"]), summary, TreeColumns, tree, []);
    }

    private static TableRow SystemRow(JsonObject system, string domain, IReadOnlyDictionary<long, List<JsonObject>> elementsBySystem)
    {
        var elements = elementsBySystem.GetValueOrDefault(Id(system["id"])) ?? [];
        var elementRows = elements.Select(e => ElementRow(e, domain)).ToList();
        return new TableRow(
            [Text(system["name"]), "Système", domain, $"{elements.Count} élément(s)", null, null, null],
            elementRows,
            depth: 1);
    }

    private static TableRow ElementRow(JsonObject element, string parentDomain)
    {
        var family = element["family"]?.GetValue<string>();
        var type = element["type"]?.GetValue<string>();
        var detail = string.Join("  ·  ", new[] { family, type }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var materiel = element["materiel"]?.GetValue<string>();
        var numCta = element["numCta"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(materiel))
        {
            detail = string.IsNullOrEmpty(detail) ? materiel! : $"{detail}  ·  {materiel}";
        }

        if (!string.IsNullOrWhiteSpace(numCta))
        {
            detail += $"  ·  CTA {numCta}";
        }

        var name = element["name"]?.GetValue<string>();
        var category = Text(element["category"]);
        return new TableRow(
            [
                string.IsNullOrWhiteSpace(name) ? category : name!,
                category,
                Domain(Text(element["domain"])) is { Length: > 0 } d and not "—" ? d : parentDomain,
                detail.Length == 0 ? null : detail,
                element["level"]?.GetValue<string>(),
                element["size"]?.GetValue<string>(),
                Length(element["lengthMm"]),
            ],
            depth: 2);
    }

    private static void AppendOrphans(
        List<TableRow> tree,
        JsonArray systems,
        IReadOnlyDictionary<long, List<JsonObject>> systemsByType,
        IReadOnlyDictionary<long, List<JsonObject>> elementsBySystem,
        IReadOnlyDictionary<long, string> typeDomain)
    {
        var orphanSystems = systems.OfType<JsonObject>()
            .Where(s => !typeDomain.ContainsKey(Id(s["systemTypeId"])))
            .OrderBy(s => Text(s["name"]), StringComparer.CurrentCulture)
            .ToList();
        if (orphanSystems.Count == 0)
        {
            return;
        }

        var rows = orphanSystems.Select(s => SystemRow(s, "—", elementsBySystem)).ToList();
        tree.Add(new TableRow(["(systèmes sans type)", "Type de système", "—", $"{orphanSystems.Count} système(s)", null, null, null], rows));
    }

    private static string Domain(string raw) => raw switch
    {
        "Duct" => "Gaines (air)",
        "Pipe" => "Tuyaux (eau)",
        _ => "—",
    };

    private static long Id(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var id) ? id : long.MinValue;

    private static string Text(JsonNode? node) => node?.ToString() ?? "—";

    private static string? Length(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var mm)
            ? mm.ToString("0.#", CultureInfo.InvariantCulture)
            : null;

    private static string Count(JsonObject model, string key, int fallback)
    {
        var value = model["summary"]?[key];
        return value is JsonValue v && v.TryGetValue<int>(out var n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : fallback.ToString(CultureInfo.InvariantCulture);
    }

    private static InventoryView Failed(string error) =>
        new(null, [], TreeColumns, [], [error]);
}
