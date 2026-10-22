using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class InventoryReaderTests
{
    [Fact]
    public void Reads_summary_and_builds_type_system_element_tree()
    {
        var view = new InventoryReader().Read(TestEnvironment.ExampleResult);

        Assert.True(view.IsValid);
        Assert.Equal(InventoryReader.TreeColumns, view.Columns);
        Assert.Contains(view.Summary, kv => kv.Key.StartsWith("Types") && kv.Value == "2 / 2 / 3");

        // Deux types de système à la racine.
        Assert.Equal(2, view.Tree.Count);
        var cta = view.Tree.Single(r => r.Cells[0]!.StartsWith("CTA BUREAUX ASF", StringComparison.Ordinal));
        Assert.Equal("Type de système", cta.Cells[1]);
        Assert.Equal("Gaines (air)", cta.Cells[2]);

        // Un système sous le type, deux éléments sous le système.
        var system = Assert.Single(cta.Children);
        Assert.Equal("ARJ CTA LT 35", system.Cells[0]);
        Assert.Equal("Système", system.Cells[1]);
        Assert.Equal(2, system.Children.Count);
        Assert.Contains(system.Children, e => e.Cells[1] == "MechanicalEquipment");
    }

    [Fact]
    public void Invalid_json_is_reported_without_throwing()
    {
        var view = new InventoryReader().Read("{ pas du json");

        Assert.False(view.IsValid);
        Assert.NotEmpty(view.Errors);
        Assert.Empty(view.Tree);
    }
}
