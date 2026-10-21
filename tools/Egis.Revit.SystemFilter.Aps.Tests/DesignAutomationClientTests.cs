using System.Net;
using System.Text.Json.Nodes;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;

namespace Egis.Revit.SystemFilter.Aps.Tests;

public sealed class DesignAutomationClientTests
{
    private const string UploadUrl = "https://s3.test/bucket";
    private const string UploadResponse =
        """{"version":1,"uploadParameters":{"endpointURL":"https://s3.test/bucket","formData":{"key":"apps/k","policy":"p0l","x-amz-signature":"s1g","success_action_status":"200"}}}""";

    [Fact]
    public async Task Engines_are_read_across_pages_with_bearer_token()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/engines", HttpStatusCode.OK, """{"data":["Autodesk.Revit+2025"],"paginationToken":"p2"}""");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/engines?page=p2", HttpStatusCode.OK, """{"data":["Autodesk.Revit+2026"],"paginationToken":""}""");
        var client = Client(http);

        var engines = await client.GetEnginesAsync();

        Assert.Equal(["Autodesk.Revit+2025", "Autodesk.Revit+2026"], engines);
        Assert.All(handler.Requests, r => Assert.Equal($"Bearer {Support.Token}", r.Authorization));
    }

    [Fact]
    public async Task Missing_engine_is_reported()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/engines", HttpStatusCode.OK, """{"data":["Autodesk.Revit+2025","Autodesk.Revit+2026"]}""");

        var error = await Assert.ThrowsAsync<ApsException>(() => Client(http).EnsureRevitEnginesAsync(["2025", "2027"]));

        Assert.Equal("engine(s) indisponible(s) : Autodesk.Revit+2027", error.Message);
    }

    [Fact]
    public async Task Aliases_return_null_on_404_and_a_map_otherwise()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/EgisSystemFilter_R2026/aliases", HttpStatusCode.NotFound, """{"detail":"not found"}""");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/activities/SystemsInventory_R2026/aliases", HttpStatusCode.OK, """{"data":[{"id":"test","version":3},{"id":"prod","version":2}]}""");
        var client = Client(http);

        Assert.Null(await client.GetAliasesAsync(DaResourceKind.AppBundle, "EgisSystemFilter_R2026"));
        var aliases = await client.GetAliasesAsync(DaResourceKind.Activity, "SystemsInventory_R2026");

        Assert.Equal(3, aliases!["test"]);
        Assert.Equal(2, aliases["prod"]);
    }

    [Fact]
    public async Task AppBundle_create_upload_and_alias()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Post, $"{Support.Da}/appbundles", HttpStatusCode.OK, UploadResponse);
        handler.Expect(HttpMethod.Post, UploadUrl, HttpStatusCode.OK);
        handler.Expect(HttpMethod.Post, $"{Support.Da}/appbundles/EgisSystemFilter_R2026/aliases", HttpStatusCode.OK, """{"id":"test","version":1}""");
        var client = Client(http);
        var definition = new JsonObject { ["id"] = "EgisSystemFilter_R2026", ["engine"] = "Autodesk.Revit+2026" };

        var created = await client.CreateAppBundleAsync(definition);
        await client.UploadAppBundleAsync(created.UploadParameters, new MemoryStream("ZIPDATA"u8.ToArray()), "EgisSystemFilter_R2026.zip");
        await client.SetAliasAsync(DaResourceKind.AppBundle, "EgisSystemFilter_R2026", "test", created.Version, existing: null);

        Assert.Equal(1, created.Version);
        Assert.Equal("""{"id":"EgisSystemFilter_R2026","engine":"Autodesk.Revit+2026"}""", handler.Requests[0].BodyText);
        Assert.Equal("application/json; charset=utf-8", handler.Requests[0].ContentType);

        var upload = handler.Requests[1];
        Assert.Null(upload.Authorization); // jamais de jeton APS vers S3
        Assert.StartsWith("multipart/form-data; boundary=", upload.ContentType);
        var body = upload.BodyText;
        var positions = new[] { "name=\"key\"", "name=\"policy\"", "name=\"x-amz-signature\"", "name=\"success_action_status\"", "name=\"file\"; filename=\"EgisSystemFilter_R2026.zip\"" }
            .Select(marker => body.IndexOf(marker, StringComparison.Ordinal))
            .ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions); // champs dans l'ordre reçu, fichier en dernier
        Assert.Contains("apps/k\r\n", body);
        Assert.Contains("Content-Type: application/octet-stream\r\n\r\nZIPDATA\r\n", body);
        Assert.DoesNotContain("text/plain", body);

        Assert.Equal("""{"id":"test","version":1}""", handler.Requests[2].BodyText);
        Assert.Equal(0, handler.Pending);
        Assert.DoesNotContain("s1g", created.UploadParameters.ToString());
    }

    [Fact]
    public async Task New_versions_omit_id_and_existing_alias_is_patched()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Post, $"{Support.Da}/appbundles/EgisSystemFilter_R2026/versions", HttpStatusCode.OK, UploadResponse.Replace("\"version\":1", "\"version\":4"));
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/appbundles/EgisSystemFilter_R2026/aliases/test", HttpStatusCode.OK, """{"id":"test","version":4}""");
        var client = Client(http);
        var definition = new JsonObject { ["id"] = "EgisSystemFilter_R2026", ["engine"] = "Autodesk.Revit+2026" };

        var created = await client.CreateAppBundleVersionAsync("EgisSystemFilter_R2026", definition);
        await client.SetAliasAsync(DaResourceKind.AppBundle, "EgisSystemFilter_R2026", "test", created.Version, new Dictionary<string, int> { ["test"] = 3 });

        Assert.Equal(4, created.Version);
        Assert.Equal("""{"engine":"Autodesk.Revit+2026"}""", handler.Requests[0].BodyText);
        Assert.True(definition.ContainsKey("id")); // la définition d'origine n'est pas modifiée
        Assert.Equal("""{"version":4}""", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task Activity_create_and_version()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities", HttpStatusCode.OK, """{"id":"nick.SystemsInventory_R2026","version":1}""");
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities/SystemsInventory_R2026/versions", HttpStatusCode.OK, """{"id":"nick.SystemsInventory_R2026","version":2}""");
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities/SystemsInventory_R2026/aliases", HttpStatusCode.OK, """{"id":"prod","version":2}""");
        var client = Client(http);
        var activity = new JsonObject
        {
            ["id"] = "SystemsInventory_R2026",
            ["commandLine"] = new JsonArray("$(engine.path)\\revitcoreconsole.exe /al \"$(appbundles[EgisSystemFilter_R2026].path)\""),
        };

        Assert.Equal(1, await client.CreateActivityAsync(activity));
        Assert.Equal(2, await client.CreateActivityVersionAsync("SystemsInventory_R2026", activity));
        await client.CreateAliasAsync(DaResourceKind.Activity, "SystemsInventory_R2026", "prod", 2);

        // commandLine transmis à l'identique (une seule barre oblique inverse dans la valeur).
        Assert.Contains("\"$(engine.path)\\\\revitcoreconsole.exe", handler.Requests[0].BodyText);
        Assert.Equal(
            JsonNode.Parse(handler.Requests[0].BodyText)!["commandLine"]![0]!.GetValue<string>(),
            activity["commandLine"]![0]!.GetValue<string>());
        Assert.DoesNotContain("\"id\"", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task Unexpected_status_raises_ApsException_with_truncated_redacted_body()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Post, $"{Support.Da}/activities", HttpStatusCode.Conflict,
            """{"diagnostic":"exists","echo":"Bearer jeton-de-test https://s3.test/x?X-Amz-Signature=abc"}""" + new string('x', 600));

        var error = await Assert.ThrowsAsync<ApsException>(() => Client(http).CreateActivityAsync(new JsonObject { ["id"] = "A" }));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.StartsWith("POST /activities : HTTP 409 ", error.Message);
        Assert.DoesNotContain(Support.Token, error.Message);
        Assert.DoesNotContain("X-Amz-Signature=abc", error.Message);
        Assert.True(error.Message.Length < 600);
    }

    [Fact]
    public async Task Upload_failure_does_not_reveal_endpoint_or_form_data()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Post, UploadUrl, HttpStatusCode.Forbidden, "AccessDenied");
        var upload = new UploadParameters(UploadUrl, [new("policy", "p0l")]);

        var error = await Assert.ThrowsAsync<ApsException>(() => Client(http).UploadAppBundleAsync(upload, new MemoryStream([1]), "b.zip"));

        Assert.Equal("POST <endpointURL de téléversement> : HTTP 403 AccessDenied", error.Message);
    }

    internal static DesignAutomationClient Client(HttpClient http) => new(http, new StaticTokenProvider(Support.Token), Support.Da);
}
