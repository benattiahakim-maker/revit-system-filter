using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

/// <summary>
/// Automation API simulée en mémoire (engines, AppBundles, Activities, versions, alias, téléversement S3). Chaque requête
/// est enregistrée ; aucune ne sort de la machine.
/// </summary>
public sealed partial class FakeApsServer : HttpMessageHandler
{
    public const string BaseUrl = "https://da.test/v3";
    public const string UploadUrl = "https://s3.test/upload";

    public List<string> Engines { get; } = ["Autodesk.Revit+2025", "Autodesk.Revit+2026", "Autodesk.Revit+2027", "Autodesk.AutoCAD+25"];

    /// <summary>ID client simulé, renvoyé par GET /forgeapps/me tant qu'aucun nickname n'est défini.</summary>
    public const string ClientId = "ABCDEFGHIJKLMNOPQRSTUVWXYZ012345";

    /// <summary>Réponse de GET /forgeapps/me : nickname, ou ID client sans nickname.</summary>
    public string ForgeAppNickname { get; set; } = ClientId;

    /// <summary>Nicknames pris par d'autres applications (PATCH : 409).</summary>
    public HashSet<string> TakenNicknames { get; } = [];

    public Dictionary<string, Resource> AppBundles { get; } = [];

    public Dictionary<string, Resource> Activities { get; } = [];

    public List<(HttpMethod Method, string Url, string? Authorization, string Body)> Requests { get; } = [];

    public IEnumerable<(HttpMethod Method, string Url, string? Authorization, string Body)> Writes =>
        Requests.Where(r => r.Method != HttpMethod.Get);

    /// <summary>Réponse forcée (par exemple une erreur) pour la prochaine requête dont l'URL contient ce texte.</summary>
    public (string UrlPart, HttpStatusCode Status, string Body)? FailNext { get; set; }

    public sealed class Resource
    {
        public SortedSet<int> Versions { get; } = [];

        public Dictionary<string, int> Aliases { get; } = [];

        public List<JsonObject> Bodies { get; } = [];

        public static Resource With(IEnumerable<int> versions, params (string Alias, int Version)[] aliases)
        {
            var resource = new Resource();
            resource.Versions.UnionWith(versions);
            foreach (var (alias, version) in aliases)
            {
                resource.Aliases[alias] = version;
            }

            return resource;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add((request.Method, url, request.Headers.Authorization?.ToString(), body));
        if (FailNext is { } fail && url.Contains(fail.UrlPart, StringComparison.Ordinal))
        {
            FailNext = null;
            return Json(fail.Status, fail.Body);
        }

        if (url == UploadUrl)
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        var path = url[BaseUrl.Length..].Split('?')[0];
        if (request.Method == HttpMethod.Get && path == "/engines")
        {
            return Json(HttpStatusCode.OK, new JsonObject { ["data"] = new JsonArray([.. Engines.Select(e => JsonValue.Create(e))]) });
        }

        if (path == "/forgeapps/me")
        {
            return ForgeApp(request.Method, body);
        }

        var match = RoutePattern().Match(path);
        if (!match.Success)
        {
            return Json(HttpStatusCode.NotFound, "{}");
        }

        var kind = match.Groups["kind"].Value;
        var store = kind == "appbundles" ? AppBundles : Activities;
        var id = match.Groups["id"].Value;
        var rest = match.Groups["rest"].Value;
        var json = string.IsNullOrEmpty(body) ? null : JsonNode.Parse(body)!.AsObject();

        if (id.Length == 0)
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, new JsonObject { ["data"] = new JsonArray([.. store.Keys.Select(k => JsonValue.Create("nick." + k))]) });
            }

            var newId = json!["id"]!.GetValue<string>();
            if (store.ContainsKey(newId))
            {
                return Json(HttpStatusCode.Conflict, "{}");
            }

            store[newId] = new Resource();
            return AddVersion(kind, store[newId], json);
        }

        if (!store.TryGetValue(id, out var resource))
        {
            return Json(HttpStatusCode.NotFound, "{}");
        }

        return (request.Method.Method, rest) switch
        {
            ("GET", "/aliases") => Json(HttpStatusCode.OK, new JsonObject
            {
                ["data"] = new JsonArray([.. resource.Aliases.Select(a => (JsonNode)new JsonObject { ["id"] = a.Key, ["version"] = a.Value })]),
            }),
            ("GET", "/versions") => Json(HttpStatusCode.OK, new JsonObject { ["data"] = new JsonArray([.. resource.Versions.Select(v => JsonValue.Create(v))]) }),
            ("POST", "/versions") => json!.ContainsKey("id") ? Json(HttpStatusCode.BadRequest, "{}") : AddVersion(kind, resource, json),
            ("POST", "/aliases") => SetAlias(resource, json!["id"]!.GetValue<string>(), json["version"]!.GetValue<int>(), create: true),
            ("PATCH", var r) when r.StartsWith("/aliases/", StringComparison.Ordinal) => SetAlias(resource, r[9..], json!["version"]!.GetValue<int>(), create: false),
            ("DELETE", var r) when r.StartsWith("/versions/", StringComparison.Ordinal) => DeleteVersion(resource, int.Parse(r[10..], System.Globalization.CultureInfo.InvariantCulture)),
            _ => Json(HttpStatusCode.NotFound, "{}"),
        };
    }

    /// <summary>GET : chaîne JSON ; PATCH : refusé (409) si le nickname est pris ou si l'application a des données.</summary>
    private HttpResponseMessage ForgeApp(HttpMethod method, string body)
    {
        if (method == HttpMethod.Get)
        {
            return Json(HttpStatusCode.OK, JsonValue.Create(ForgeAppNickname)!);
        }

        if (method != HttpMethod.Patch)
        {
            return Json(HttpStatusCode.NotFound, "{}");
        }

        var nickname = JsonNode.Parse(body)!["nickname"]!.GetValue<string>();
        if (TakenNicknames.Contains(nickname) || AppBundles.Count + Activities.Count > 0)
        {
            return Json(HttpStatusCode.Conflict, """{"diagnostic":"conflict"}""");
        }

        ForgeAppNickname = nickname;
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static HttpResponseMessage AddVersion(string kind, Resource resource, JsonObject body)
    {
        var version = resource.Versions.Count == 0 ? 1 : resource.Versions.Max + 1;
        resource.Versions.Add(version);
        resource.Bodies.Add(body);
        var response = new JsonObject { ["version"] = version };
        if (kind == "appbundles")
        {
            response["uploadParameters"] = new JsonObject
            {
                ["endpointURL"] = UploadUrl,
                ["formData"] = new JsonObject { ["key"] = "k", ["x-amz-signature"] = "SIGNATURE-SECRETE" },
            };
        }

        return Json(HttpStatusCode.OK, response);
    }

    private static HttpResponseMessage SetAlias(Resource resource, string alias, int version, bool create)
    {
        if (create == resource.Aliases.ContainsKey(alias) || !resource.Versions.Contains(version))
        {
            return Json(create ? HttpStatusCode.Conflict : HttpStatusCode.NotFound, "{}");
        }

        resource.Aliases[alias] = version;
        return Json(HttpStatusCode.OK, new JsonObject { ["id"] = alias, ["version"] = version });
    }

    private static HttpResponseMessage DeleteVersion(Resource resource, int version)
    {
        if (resource.Aliases.ContainsValue(version))
        {
            throw new InvalidOperationException($"Suppression d'une version référencée par un alias : v{version}");
        }

        return resource.Versions.Remove(version) ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(HttpStatusCode.NotFound, "{}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) => Json(status, node.ToJsonString());

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [GeneratedRegex(@"^/(?<kind>appbundles|activities)(/(?<id>[^/]+))?(?<rest>/.*)?$")]
    private static partial Regex RoutePattern();
}
