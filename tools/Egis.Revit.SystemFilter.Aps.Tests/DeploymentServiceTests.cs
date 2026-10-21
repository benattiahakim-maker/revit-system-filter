using System.Net;
using System.Text.Json.Nodes;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.Aps.Tests;

/// <summary>Port de <c>deploy.py</c> : même séquence d'appels, sur les définitions réelles du dépôt.</summary>
public sealed class DeploymentServiceTests
{
    private const string Upload = """{"version":VERSION,"uploadParameters":{"endpointURL":"https://s3.test/up","formData":{"key":"k"}}}""";
    private static readonly DefinitionLoader Definitions = new(Support.DefinitionsDirectory);

    [Fact]
    public void Definitions_keep_commandLine_verbatim_and_replace_placeholders()
    {
        var raw = JsonNode.Parse(File.ReadAllText(Definitions.PathOf("2026", DaResourceKind.Activity)))!;
        var activity = Definitions.Load("2026", DaResourceKind.Activity, "nick", "test");

        Assert.True(JsonNode.DeepEquals(raw["commandLine"], activity["commandLine"]));
        Assert.Equal("nick.Egis.Revit.SystemFilter_R2026+test", Assert.Single(activity["appbundles"]!.AsArray())!.GetValue<string>());
        Assert.Equal("ExtractRooms_R2026", Definitions.IdOf("2026", DaResourceKind.Activity));
        Assert.Equal("Egis.Revit.SystemFilter_R2026", Definitions.IdOf("2026", DaResourceKind.AppBundle));
    }

    [Fact]
    public void Unreplaced_placeholder_is_rejected()
    {
        var error = Assert.Throws<ApsException>(() => Definitions.Load("2026", DaResourceKind.Activity, "nick"));
        Assert.Equal("definitions/2026/activity.json : paramètre de gabarit non remplacé", error.Message);
    }

    [Fact]
    public async Task First_test_publication_creates_uploads_and_aliases()
    {
        using var temp = new TempDirectory();
        Support.WriteValidBundle(Path.Combine(temp.Path, "Egis.Revit.SystemFilter_R2026.zip"), "2026");
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/engines", HttpStatusCode.OK, """{"data":["Autodesk.Revit+2026"]}""");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2026/aliases", HttpStatusCode.NotFound);
        handler.Expect(HttpMethod.Post, $"{Support.Da}/appbundles", HttpStatusCode.OK, Upload.Replace("VERSION", "1"));
        handler.Expect(HttpMethod.Post, "https://s3.test/up", HttpStatusCode.NoContent);
        handler.Expect(HttpMethod.Post, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2026/aliases", HttpStatusCode.OK, "{}");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/activities/ExtractRooms_R2026/aliases", HttpStatusCode.NotFound);
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities", HttpStatusCode.OK, """{"version":1}""");
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities/ExtractRooms_R2026/aliases", HttpStatusCode.OK, "{}");
        var log = new List<string>();

        await Service(http, log).PublishTestAsync("nick", ["2026"], temp.Path);

        Assert.Equal(0, handler.Pending);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Definitions.PathOf("2026", DaResourceKind.AppBundle))), JsonNode.Parse(handler.Requests[2].BodyText)));
        Assert.Contains("extraction des pièces", handler.Requests[2].BodyText); // UTF-8, sans échappement
        var activity = JsonNode.Parse(handler.Requests[6].BodyText)!;
        Assert.Equal("ExtractRooms_R2026", activity["id"]!.GetValue<string>());
        Assert.Equal("nick.Egis.Revit.SystemFilter_R2026+test", activity["appbundles"]![0]!.GetValue<string>());
        Assert.Equal("""{"id":"test","version":1}""", handler.Requests[7].BodyText);
        Assert.Equal(["Revit 2026 : nick.Egis.Revit.SystemFilter_R2026+test -> v1, nick.ExtractRooms_R2026+test -> v1"], log);
    }

    [Fact]
    public async Task Test_publication_on_existing_resources_adds_versions_and_moves_alias()
    {
        using var temp = new TempDirectory();
        Support.WriteValidBundle(Path.Combine(temp.Path, "Egis.Revit.SystemFilter_R2025.zip"), "2025");
        var (handler, http) = Support.Http();
        const string aliases = """{"data":[{"id":"test","version":2},{"id":"prod","version":1}]}""";
        handler.Expect(HttpMethod.Get, $"{Support.Da}/engines", HttpStatusCode.OK, """{"data":["Autodesk.Revit+2025"]}""");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2025/aliases", HttpStatusCode.OK, aliases);
        handler.Expect(HttpMethod.Post, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2025/versions", HttpStatusCode.OK, Upload.Replace("VERSION", "3"));
        handler.Expect(HttpMethod.Post, "https://s3.test/up", HttpStatusCode.OK);
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2025/aliases/test", HttpStatusCode.OK, "{}");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/activities/ExtractRooms_R2025/aliases", HttpStatusCode.OK, aliases);
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities/ExtractRooms_R2025/versions", HttpStatusCode.OK, """{"version":3}""");
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/activities/ExtractRooms_R2025/aliases/test", HttpStatusCode.OK, "{}");

        await Service(http).PublishTestAsync("nick", ["2025"], temp.Path);

        Assert.Equal(0, handler.Pending);
        Assert.DoesNotContain("\"id\"", handler.Requests[2].BodyText);
        Assert.DoesNotContain("\"id\"", handler.Requests[6].BodyText);
        Assert.Equal("""{"version":3}""", handler.Requests[4].BodyText);
    }

    [Fact]
    public async Task Refused_bundle_stops_before_any_write()
    {
        using var temp = new TempDirectory();
        Support.WriteValidBundle(Path.Combine(temp.Path, "Egis.Revit.SystemFilter_R2026.zip"), "2026", e => e["Egis.Revit.SystemFilter.bundle/Contents/RevitAPI.dll"] = [1]);
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/engines", HttpStatusCode.OK, """{"data":["Autodesk.Revit+2026"]}""");

        var error = await Assert.ThrowsAsync<ApsException>(() => Service(http).PublishTestAsync("nick", ["2026"], temp.Path));

        Assert.Contains("refusé par le contrôle : fichier interdit : Egis.Revit.SystemFilter.bundle/Contents/RevitAPI.dll", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Missing_bundle_is_reported()
    {
        using var temp = new TempDirectory();
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/engines", HttpStatusCode.OK, """{"data":["Autodesk.Revit+2027"]}""");

        var error = await Assert.ThrowsAsync<ApsException>(() => Service(http).PublishTestAsync("nick", ["2027"], temp.Path));

        Assert.EndsWith("introuvable : lancer d'abord build-appbundle.sh 2027", error.Message);
    }

    [Fact]
    public async Task Promote_points_prod_to_test_and_logs_rollback()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2026/aliases", HttpStatusCode.OK, """{"data":[{"id":"test","version":5},{"id":"prod","version":4}]}""");
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2026/aliases/prod", HttpStatusCode.OK, "{}");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/activities/ExtractRooms_R2026/aliases", HttpStatusCode.OK, """{"data":[{"id":"test","version":7},{"id":"prod","version":6}]}""");
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities/ExtractRooms_R2026/versions", HttpStatusCode.OK, """{"version":8}""");
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/activities/ExtractRooms_R2026/aliases/prod", HttpStatusCode.OK, "{}");
        var log = new List<string>();

        await Service(http, log).PromoteAsync("nick", ["2026"]);

        Assert.Equal("""{"version":5}""", handler.Requests[1].BodyText);
        Assert.Equal("nick.Egis.Revit.SystemFilter_R2026+prod", JsonNode.Parse(handler.Requests[3].BodyText)!["appbundles"]![0]!.GetValue<string>());
        Assert.Equal("""{"version":8}""", handler.Requests[4].BodyText);
        Assert.Equal("Revit 2026 : Egis.Revit.SystemFilter_R2026+prod -> v5 (avant : v4), ExtractRooms_R2026+prod -> v8 (avant : v6)", log[0]);
        Assert.Contains("--to-version 4", log[1]);
        Assert.Contains("--to-version 6", log[1]);
    }

    [Fact]
    public async Task Promote_without_test_alias_fails()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2026/aliases", HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<ApsException>(() => Service(http).PromoteAsync("nick", ["2026"]));

        Assert.Equal("Egis.Revit.SystemFilter_R2026 : aucun alias test à promouvoir", error.Message);
    }

    [Fact]
    public async Task Rollback_patches_alias_and_status_reads_aliases()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/activities/ExtractRooms_R2027/aliases/prod", HttpStatusCode.OK, "{}");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/Egis.Revit.SystemFilter_R2027/aliases", HttpStatusCode.OK, """{"data":[{"id":"prod","version":2}]}""");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/activities/ExtractRooms_R2027/aliases", HttpStatusCode.NotFound);
        var log = new List<string>();
        var service = Service(http, log);

        await service.RollbackAsync(DaResourceKind.Activity, "2027", 3);
        var status = await service.StatusAsync(["2027"]);

        Assert.Equal("""{"version":3}""", handler.Requests[0].BodyText);
        Assert.Equal(2, status[0].Aliases!["prod"]);
        Assert.Null(status[1].Aliases);
        Assert.Equal(["ExtractRooms_R2027+prod -> v3", "Egis.Revit.SystemFilter_R2027 : {'prod': 2}", "ExtractRooms_R2027 : None"], log);
    }

    private static DeploymentService Service(HttpClient http, List<string>? log = null) =>
        new(DesignAutomationClientTests.Client(http), Definitions, log is null ? null : log.Add);
}
