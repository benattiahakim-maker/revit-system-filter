using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Egis.Revit.SystemFilter.Core.Models.Inventory;
using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public class BaseEquipmentTests
{
    // Mêmes options que SystemInventoryExport (nulls omis par défaut).
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void Serialise_la_valeur_en_entier()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(new SystemRecord { Id = 201, BaseEquipmentId = 9001 }, Options))!;
        Assert.Equal(JsonValueKind.Number, node["baseEquipmentId"]!.GetValueKind());
        Assert.Equal(9001L, node["baseEquipmentId"]!.GetValue<long>());
    }

    [Fact]
    public void Serialise_null_toujours_present()
    {
        var obj = JsonNode.Parse(JsonSerializer.Serialize(new SystemRecord { Id = 201 }, Options))!.AsObject();
        Assert.True(obj.ContainsKey("baseEquipmentId"));
        Assert.Null(obj["baseEquipmentId"]);
    }

    [Fact]
    public void Equipement_absent_de_elements_remis_a_null_avec_avertissement()
    {
        var systems = new List<SystemRecord>
        {
            new() { Id = 1, Name = "Soufflage", BaseEquipmentId = 9001 },  // CTA exportée
            new() { Id = 2, Name = "Reprise", BaseEquipmentId = 7777 },    // absente (lien, autre catégorie)
            new() { Id = 3, Name = "EF", BaseEquipmentId = null },         // pas d'équipement
        };
        var warnings = BaseEquipmentCheck.Apply(systems, new long[] { 9001, 42 });

        Assert.Equal(9001L, systems[0].BaseEquipmentId);
        Assert.Null(systems[1].BaseEquipmentId);
        Assert.Null(systems[2].BaseEquipmentId);
        var warning = Assert.Single(warnings);
        Assert.Contains("7777", warning);
        Assert.Contains("Reprise", warning);

        var log = new RunLog();
        foreach (var w in warnings) log.Warn(w);
        Assert.StartsWith("WARN équipement de départ 7777", log.ToString());
        Assert.DoesNotContain(":\\", log.ToString());
    }
}
