using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

/// <summary>
/// APS simulé en mémoire : alias d'Activities, WorkItems (statuts scriptés), rapport, OSS (buckets, URL S3 signées,
/// finalisation, lecture). À la fin d'un WorkItem, le « moteur » écrit <see cref="ResultJson"/> sur l'URL de sortie.
/// </summary>
public sealed partial class FakeAps : HttpMessageHandler
{
    public const string Da = "https://da.test/v3";
    public const string Oss = "https://oss.test/oss/v2";
    public const string ReportUrl = "https://reports.test/wi.txt?X-Amz-Signature=report-sig";

    private readonly Dictionary<string, byte[]> _pending = [];
    private int _polls;

    public Dictionary<string, Dictionary<string, int>> ActivityAliases { get; } = [];

    public HashSet<string> Buckets { get; } = [];

    public Dictionary<string, byte[]> Objects { get; } = [];

    public List<(HttpMethod Method, string Url, string? Authorization, string Body)> Requests { get; } = [];

    /// <summary>Statuts renvoyés successivement par GET /workitems/{id} (le dernier est répété).</summary>
    public List<string> Statuses { get; } = ["pending", "inprogress", "success"];

    /// <summary>Contenu écrit par le moteur sur la sortie result ; null : aucune sortie (cas D6).</summary>
    public string? ResultJson { get; set; }

    /// <summary>Fichiers supplémentaires de la sortie ZIP, sous <c>Images/</c> (nom, octets).</summary>
    public List<(string Name, byte[] Bytes)> ResultImages { get; } = [];

    /// <summary>Sortie ZIP du moteur : <c>result.json</c> à la racine et <c>Images/</c>, comme le dossier <c>result/</c> zippé par APS.</summary>
    private byte[] ResultZip(string json)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("systems-inventory.json").Open(), new UTF8Encoding(false)))
            {
                writer.Write(json);
            }

            foreach (var (name, bytes) in ResultImages)
            {
                using var entry = archive.CreateEntry("Images/" + name).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    public JsonObject? SubmittedWorkItem { get; private set; }

    public bool Cancelled { get; private set; }

    /// <summary>Appelé à chaque GET /workitems/{id} (numéro de l'interrogation).</summary>
    public Func<int, Task>? OnPoll { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add((request.Method, url, request.Headers.Authorization?.ToString(), Encoding.UTF8.GetString(bytes)));
        var path = request.RequestUri.AbsolutePath;
        var method = request.Method.Method;

        if (url.StartsWith("https://s3.test/", StringComparison.Ordinal))
        {
            var key = Uri.UnescapeDataString(path[1..]);
            return method switch
            {
                "PUT" => Store(key, bytes),
                "GET" when Objects.TryGetValue(key, out var data) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        if (url == ReportUrl)
        {
            return Text(HttpStatusCode.OK, "[09/10/2026 13:00:00] Starting work item\n[09/10/2026 13:00:40] Job finished");
        }

        if (url.StartsWith(Oss, StringComparison.Ordinal))
        {
            return HandleOss(method, path, request.RequestUri.Query, bytes);
        }

        var activityAliases = ActivityAliasPattern().Match(path);
        if (method == "GET" && activityAliases.Success)
        {
            return ActivityAliases.TryGetValue(activityAliases.Groups[1].Value, out var aliases)
                ? Json(new JsonObject { ["data"] = new JsonArray([.. aliases.Select(a => (JsonNode)new JsonObject { ["id"] = a.Key, ["version"] = a.Value })]) })
                : Text(HttpStatusCode.NotFound, "{}");
        }

        if (method == "POST" && path.EndsWith("/workitems", StringComparison.Ordinal))
        {
            SubmittedWorkItem = JsonNode.Parse(bytes)!.AsObject();
            return Json(new JsonObject { ["id"] = "wi-1", ["status"] = "pending" });
        }

        if (path.EndsWith("/workitems/wi-1", StringComparison.Ordinal))
        {
            if (method == "DELETE")
            {
                Cancelled = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            _polls++;
            if (OnPoll is not null)
            {
                await OnPoll(_polls);
            }

            var status = Cancelled ? "cancelled" : Statuses[Math.Min(_polls - 1, Statuses.Count - 1)];
            if (status is not ("pending" or "inprogress") && ResultJson is not null && !Cancelled)
            {
                // Le moteur écrit result.json sur l'URL signée de sortie (PUT S3), comme APS.
                var output = SubmittedWorkItem!["arguments"]!["result"]!["url"]!.GetValue<string>();
                var outputKey = Uri.UnescapeDataString(new Uri(output).AbsolutePath[1..]);
                Store(outputKey, outputKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ResultZip(ResultJson) : Encoding.UTF8.GetBytes(ResultJson));
            }

            var body = new JsonObject { ["id"] = "wi-1", ["status"] = status };
            if (status is not ("pending" or "inprogress"))
            {
                body["reportUrl"] = ReportUrl;
                body["stats"] = new JsonObject { ["timeQueued"] = "2026-10-09T11:00:00Z" };
            }

            return Json(body);
        }

        return Text(HttpStatusCode.NotFound, "{}");
    }

    private HttpResponseMessage HandleOss(string method, string path, string query, byte[] body)
    {
        if (method == "POST" && path.EndsWith("/buckets", StringComparison.Ordinal))
        {
            var key = JsonNode.Parse(body)!["bucketKey"]!.GetValue<string>();
            return Buckets.Add(key) ? Json(new JsonObject { ["bucketKey"] = key }) : Text(HttpStatusCode.Conflict, "{}");
        }

        var match = OssObjectPattern().Match(path);
        if (!match.Success)
        {
            return Text(HttpStatusCode.NotFound, "{}");
        }

        var objectPath = $"{Uri.UnescapeDataString(match.Groups["bucket"].Value)}/{Uri.UnescapeDataString(match.Groups["object"].Value)}";
        var s3 = "https://s3.test/" + string.Join('/', objectPath.Split('/').Select(Uri.EscapeDataString));
        return (method, match.Groups["op"].Value) switch
        {
            ("GET", "signeds3upload") => Json(new JsonObject { ["uploadKey"] = "UK:" + objectPath, ["urls"] = new JsonArray(s3 + "?X-Amz-Signature=upload-sig") }),
            ("POST", "signeds3upload") => Complete(objectPath, JsonNode.Parse(body)!["uploadKey"]!.GetValue<string>()),
            ("GET", "signeds3download") when Objects.ContainsKey(objectPath) => Json(new JsonObject { ["url"] = s3 + "?X-Amz-Signature=download-sig" }),
            _ => Text(HttpStatusCode.NotFound, "{}"),
        };
    }

    private HttpResponseMessage Store(string key, byte[] bytes)
    {
        _pending[key] = bytes;
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private HttpResponseMessage Complete(string objectPath, string uploadKey)
    {
        if (uploadKey != "UK:" + objectPath || !_pending.Remove(objectPath, out var data))
        {
            return Text(HttpStatusCode.BadRequest, """{"reason":"upload not found"}""");
        }

        Objects[objectPath] = data;
        return Json(new JsonObject { ["objectKey"] = objectPath, ["size"] = data.Length });
    }

    private static HttpResponseMessage Json(JsonNode node) => Text(HttpStatusCode.OK, node.ToJsonString());

    private static HttpResponseMessage Text(HttpStatusCode status, string text) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    [GeneratedRegex("^/v3/activities/([^/]+)/aliases$")]
    private static partial Regex ActivityAliasPattern();

    [GeneratedRegex("^/oss/v2/buckets/(?<bucket>[^/]+)/objects/(?<object>[^/]+)/(?<op>signeds3upload|signeds3download)$")]
    private static partial Regex OssObjectPattern();
}
