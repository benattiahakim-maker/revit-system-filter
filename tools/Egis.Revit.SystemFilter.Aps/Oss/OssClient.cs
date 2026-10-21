using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.Aps.Oss;

/// <summary>URL S3 signée de téléversement (<c>signeds3upload</c>). <see cref="UploadKey"/> sert à finaliser l'objet.</summary>
public sealed record SignedUpload(string UploadKey, IReadOnlyList<string> Urls)
{
    public override string ToString() => $"SignedUpload {{ UploadKey = ***, Urls = {Urls.Count} URL signée(s) }}";
}

/// <summary>
/// Client OSS v2 nécessaire à EGR-20 : bucket, téléversement par URL S3 signées, URL signées de lecture/écriture pour les
/// arguments de WorkItem. Les URL signées ne figurent dans aucun message d'erreur.
/// </summary>
public sealed class OssClient
{
    /// <summary>Durée de validité maximale d'une URL signée OSS (minutes).</summary>
    public const int MaxMinutesExpiration = 60;

    /// <summary>Taille d'une partie pour le téléversement en plusieurs parties (minimum S3 : 5 Mio, hors dernière partie).</summary>
    public const int DefaultPartSize = 64 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly ApsHttp _http;

    /// <param name="tokenProvider">Jeton 2-legged avec les scopes <see cref="ApsScopes.Oss"/>.</param>
    public OssClient(HttpClient httpClient, IApsTokenProvider tokenProvider, string baseUrl = ApsSettings.DefaultOssBaseUrl)
    {
        _httpClient = httpClient;
        _http = new ApsHttp(httpClient, tokenProvider, baseUrl);
    }

    /// <summary>
    /// <c>POST /buckets</c> (politique <c>transient</c> par défaut : 24 h). Retourne false si le bucket existe déjà (409).
    /// </summary>
    public async Task<bool> CreateBucketAsync(string bucketKey, string policyKey = "transient", string? region = null, CancellationToken cancellationToken = default)
    {
        var headers = region is null ? null : new Dictionary<string, string> { ["x-ads-region"] = region };
        var response = await _http.CallAsync(
            HttpMethod.Post,
            "/buckets",
            new JsonObject { ["bucketKey"] = bucketKey, ["policyKey"] = policyKey },
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            cancellationToken,
            headers).ConfigureAwait(false);
        return response.Status == HttpStatusCode.OK;
    }

    /// <summary>
    /// <c>GET /buckets/{bucket}/objects/{object}/signeds3upload</c> : une URL par partie. Utilisable comme argument
    /// <c>put</c> d'un WorkItem (une partie), puis <see cref="CompleteUploadAsync"/> après le WorkItem.
    /// </summary>
    public async Task<SignedUpload> GetSignedUploadAsync(string bucketKey, string objectKey, int parts = 1, int minutesExpiration = MaxMinutesExpiration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        var path = $"{ObjectPath(bucketKey, objectKey)}/signeds3upload?parts={parts}&minutesExpiration={ClampMinutes(minutesExpiration)}";
        var response = await _http.CallAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var uploadKey = ApsHttp.Require(response, "uploadKey", "GET …/signeds3upload").GetValue<string>();
        var urls = ApsHttp.Require(response, "urls", "GET …/signeds3upload").AsArray().Select(u => u!.GetValue<string>()).ToList();
        if (urls.Count != parts)
        {
            throw new ApsException($"GET …/signeds3upload : {urls.Count} URL reçue(s) pour {parts} partie(s).");
        }

        return new SignedUpload(uploadKey, urls);
    }

    /// <summary><c>POST /buckets/{bucket}/objects/{object}/signeds3upload</c> : finalise l'objet ; retourne la réponse (objectId, size…).</summary>
    public async Task<JsonNode?> CompleteUploadAsync(string bucketKey, string objectKey, string uploadKey, CancellationToken cancellationToken = default)
    {
        var response = await _http.CallAsync(
            HttpMethod.Post,
            $"{ObjectPath(bucketKey, objectKey)}/signeds3upload",
            new JsonObject { ["uploadKey"] = uploadKey },
            cancellationToken).ConfigureAwait(false);
        return response.Body;
    }

    /// <summary>
    /// Téléverse un flux : URL signées (une par partie de <paramref name="partSize"/>), <c>PUT</c> de chaque partie sans
    /// jeton, puis finalisation.
    /// </summary>
    public async Task<JsonNode?> UploadObjectAsync(string bucketKey, string objectKey, Stream content, long length, int partSize = DefaultPartSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(partSize, 5 * 1024 * 1024);
        var parts = (int)Math.Max(1, (length + partSize - 1) / partSize);
        var signed = await GetSignedUploadAsync(bucketKey, objectKey, parts, cancellationToken: cancellationToken).ConfigureAwait(false);
        var buffer = new byte[(int)Math.Min(partSize, Math.Max(length, 0))];
        for (var index = 0; index < parts; index++)
        {
            var read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, signed.Urls[index]) { Content = new ByteArrayContent(buffer, 0, read) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            await ApsHttp.SendAsync(_httpClient, request, "PUT", $"<URL signée S3, partie {index + 1}/{parts}>", [HttpStatusCode.OK], cancellationToken).ConfigureAwait(false);
        }

        return await CompleteUploadAsync(bucketKey, objectKey, signed.UploadKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Téléverse un fichier local (voir <see cref="UploadObjectAsync(string, string, Stream, long, int, CancellationToken)"/>).</summary>
    public async Task<JsonNode?> UploadFileAsync(string bucketKey, string objectKey, string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await UploadObjectAsync(bucketKey, objectKey, stream, stream.Length, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>GET /buckets/{bucket}/objects/{object}/signeds3download</c> : URL S3 signée de lecture (argument <c>get</c>).</summary>
    public async Task<string> GetSignedDownloadUrlAsync(string bucketKey, string objectKey, int minutesExpiration = MaxMinutesExpiration, CancellationToken cancellationToken = default)
    {
        var path = $"{ObjectPath(bucketKey, objectKey)}/signeds3download?minutesExpiration={ClampMinutes(minutesExpiration)}";
        var response = await _http.CallAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        return ApsHttp.Require(response, "url", "GET …/signeds3download").GetValue<string>();
    }

    /// <summary>Télécharge un objet via son URL signée de lecture (aucun jeton envoyé à S3).</summary>
    public async Task<byte[]> DownloadObjectAsync(string bucketKey, string objectKey, CancellationToken cancellationToken = default)
    {
        var url = await GetSignedDownloadUrlAsync(bucketKey, objectKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApsException($"GET <URL signée S3> ({objectKey}) : HTTP {(int)response.StatusCode}", response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ObjectPath(string bucketKey, string objectKey) =>
        $"/buckets/{Uri.EscapeDataString(bucketKey)}/objects/{Uri.EscapeDataString(objectKey)}";

    private static int ClampMinutes(int minutes) => Math.Clamp(minutes, 1, MaxMinutesExpiration);
}
