using System.Text.Json.Nodes;
using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class ArgumentsAndChecksTests
{
    private static IReadOnlyList<ActivityParameter> Parameters2026()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "repo", "src", "Packaging", "definitions", "2026", "activity.json");
        return ActivityParameter.FromDefinition(JsonNode.Parse(File.ReadAllText(path))!.AsObject());
    }

    [Fact]
    public void Parameters_are_read_from_the_activity_definition()
    {
        var parameters = Parameters2026();

        Assert.Equal(["inputZip", "settings", "result"], parameters.Select(p => p.Name));
        Assert.True(parameters[0] is { Verb: "get", Zip: true, IsInput: true, Required: true });
        Assert.True(parameters[1] is { LocalName: "settings.json", IsInput: true });
        Assert.True(parameters[2] is { Verb: "put", Zip: true, LocalName: "result", IsInput: false });
    }

    [Fact]
    public void Arguments_match_activity_parameters_with_their_verbs()
    {
        var workItem = WorkItemArguments.Build(
            "nick.SystemFilter_R2026+test",
            Parameters2026(),
            new Dictionary<string, string> { ["inputZip"] = "https://s3/in", ["settings"] = "https://s3/settings" },
            new Dictionary<string, string> { ["result"] = "https://s3/out" });

        var expected = JsonNode.Parse("""
            {
              "activityId": "nick.SystemFilter_R2026+test",
              "arguments": {
                "inputZip": { "url": "https://s3/in", "verb": "get" },
                "settings": { "url": "https://s3/settings", "verb": "get" },
                "result": { "url": "https://s3/out", "verb": "put" }
              }
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, workItem), workItem.ToJsonString());
    }

    [Fact]
    public void Arguments_reject_missing_required_and_unknown_parameters()
    {
        var parameters = Parameters2026();
        var inputs = new Dictionary<string, string> { ["inputZip"] = "u", ["settings"] = "u" };

        var missing = Assert.Throws<InvalidOperationException>(() => WorkItemArguments.Build("a", parameters, inputs, new Dictionary<string, string>()));
        Assert.Contains("result", missing.Message);

        var unknown = Assert.Throws<InvalidOperationException>(() => WorkItemArguments.Build(
            "a", parameters, new Dictionary<string, string>(inputs) { ["extra"] = "u" }, new Dictionary<string, string> { ["result"] = "u" }));
        Assert.Contains("extra", unknown.Message);
    }

    [Fact]
    public void Zip_checks_report_errors_and_warnings()
    {
        using var env = new TestEnvironment();

        Assert.True(InputChecks.CheckZip(env.ZipPath) is { IsValid: true, Warnings.Count: 0 });
        Assert.False(InputChecks.CheckZip(Path.Combine(env.Root, "absent.zip")).IsValid);
        Assert.False(InputChecks.CheckZip(env.WriteZip("empty.zip", ("notes.txt", "x"))).IsValid);

        var several = InputChecks.CheckZip(env.WriteZip("two.zip", ("A.rvt", "x"), ("B.rvt", "y")));
        Assert.True(several.IsValid);
        Assert.NotEmpty(several.Warnings);

        var longName = InputChecks.CheckZip(env.WriteZip("long.zip", (new string('d', 160) + "/A.rvt", "x")));
        Assert.Contains(longName.Warnings, w => w.Contains("166 caractères", StringComparison.Ordinal));

        var accents = InputChecks.CheckZip(env.WriteZip("accents.zip", ("Bâtiment/Étage.rvt", "x")));
        Assert.True(accents.IsValid);
        Assert.NotEmpty(accents.Warnings);

        File.WriteAllText(Path.Combine(env.Root, "broken.zip"), "pas un zip");
        Assert.False(InputChecks.CheckZip(Path.Combine(env.Root, "broken.zip")).IsValid);
    }

    [Fact]
    public void Settings_checks_block_empty_input_and_only_warn_on_contract_errors()
    {
        using var env = new TestEnvironment();

        Assert.True(InputChecks.CheckSettings(env.ViewModel.SettingsText) is { IsValid: true, Warnings.Count: 0 });
        Assert.False(InputChecks.CheckSettings("  ").IsValid);

        // Cas D6 : un settings.json volontairement invalide doit pouvoir partir (contrôle côté moteur).
        var d6 = InputChecks.CheckSettings("""{ "schemaVersion": "2.0" }""");
        Assert.True(d6.IsValid);
        Assert.NotEmpty(d6.Warnings);
    }
}
