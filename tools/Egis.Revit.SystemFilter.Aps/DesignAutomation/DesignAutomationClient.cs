using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.Aps.DesignAutomation;

/// <summary>
/// Client de l'Automation API v3 (AppBundles, Activities, alias, engines, WorkItems). Reprend les appels de
/// <c>src/Packaging/scripts/deploy.py</c> ; l'orchestration test → prod est dans <see cref="Packaging.DeploymentService"/>.
/// </summary>
public sealed class DesignAutomationClient
{
    private static readonly HttpStatusCode[] UploadExpected = [HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent];

    private readonly HttpClient _httpClient;
    private readonly ApsHttp _http;

    /// <param name="httpClient">Client HTTP (gestionnaire injectable pour les tests).</param>
    /// <param name="tokenProvider">Jeton 2-legged avec le scope <c>code:all</c>.</param>
    /// <param name="baseUrl">URL de base, par défaut <see cref="ApsSettings.DefaultDaBaseUrl"/> (<c>APS_DA_BASE_URL</c>).</param>
    public DesignAutomationClient(HttpClient httpClient, IApsTokenProvider tokenProvider, string baseUrl = ApsSettings.DefaultDaBaseUrl)
    {
        _httpClient = httpClient;
        _http = new ApsHttp(httpClient, tokenProvider, baseUrl);
    }

    // ---- Engines -------------------------------------------------------------------------------------------------

    /// <summary><c>GET /engines</c>, toutes pages (<c>paginationToken</c>).</summary>
    public Task<IReadOnlyList<string>> GetEnginesAsync(CancellationToken cancellationToken = default) =>
        GetAllPagesAsync("/engines", cancellationToken);

    /// <summary>Vérifie que <c>Autodesk.Revit+&lt;année&gt;</c> existe pour chaque version (<c>check_engines</c>).</summary>
    public async Task EnsureRevitEnginesAsync(IEnumerable<string> revitVersions, CancellationToken cancellationToken = default)
    {
        var engines = (await GetEnginesAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var missing = revitVersions.Select(RevitEngine).Where(e => !engines.Contains(e)).ToList();
        if (missing.Count > 0)
        {
            throw new ApsException($"engine(s) indisponible(s) : {string.Join(", ", missing)}");
        }
    }

    public static string RevitEngine(string revitVersion) => $"Autodesk.Revit+{revitVersion}";

    // ---- ForgeApps (nickname) -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>GET /forgeapps/me</c> : nickname de l'application, ou son ID client si aucun nickname n'est défini (la réponse
    /// est une chaîne JSON).
    /// </summary>
    public async Task<string> GetNicknameAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.CallAsync(HttpMethod.Get, "/forgeapps/me", null, cancellationToken).ConfigureAwait(false);
        return response.Body is JsonValue value && value.TryGetValue<string>(out var nickname) && !string.IsNullOrWhiteSpace(nickname)
            ? nickname.Trim()
            : throw new ApsException("GET /forgeapps/me : nickname absent de la réponse.");
    }

    /// <summary>
    /// <c>PATCH /forgeapps/me</c> avec <c>{"nickname": "…"}</c> (nickname dans le corps, pas dans l'URL). Accepté
    /// seulement si l'application n'a encore aucune donnée Design Automation ; ensuite, seul <c>DELETE /forgeapps/me</c>
    /// (qui supprime toutes les données) permet d'en changer. Nickname déjà pris : 409 Conflict.
    /// </summary>
    public async Task SetNicknameAsync(string nickname, CancellationToken cancellationToken = default)
    {
        if (!IsValidNickname(nickname))
        {
            throw new ApsException($"nickname « {nickname} » refusé : lettres, chiffres et « _ » uniquement ({MaxNicknameLength} caractères au plus).");
        }

        try
        {
            await _http.CallAsync(HttpMethod.Patch, "/forgeapps/me", new JsonObject { ["nickname"] = nickname }, [HttpStatusCode.OK, HttpStatusCode.NoContent], cancellationToken).ConfigureAwait(false);
        }
        catch (ApsException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            throw new ApsException(
                $"PATCH /forgeapps/me : HTTP 409, nickname « {nickname} » refusé : déjà utilisé par une autre application, ou application qui a déjà des données Design Automation.",
                HttpStatusCode.Conflict,
                exception);
        }
    }

    /// <summary>Longueur maximale acceptée par les outils (règle prudente, la documentation APS n'en fixe pas).</summary>
    public const int MaxNicknameLength = 64;

    /// <summary>
    /// Règle prudente des outils : lettres ASCII, chiffres et « _ », comme les identifiants d'AppBundle et d'Activity
    /// (le nickname préfixe <c>nickname.Activity+alias</c> : ni point, ni « + », ni espace).
    /// </summary>
    public static bool IsValidNickname(string? nickname) =>
        !string.IsNullOrEmpty(nickname) && nickname.Length <= MaxNicknameLength && nickname.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    // ---- Lecture des ressources ----------------------------------------------------------------------------------

    /// <summary><c>GET /appbundles</c> ou <c>GET /activities</c> : identifiants qualifiés, toutes pages.</summary>
    public Task<IReadOnlyList<string>> ListAsync(DaResourceKind kind, CancellationToken cancellationToken = default) =>
        GetAllPagesAsync($"/{kind.PathSegment()}", cancellationToken);

    /// <summary><c>GET /{kind}/{id}/versions</c> : numéros de version, toutes pages.</summary>
    public async Task<IReadOnlyList<int>> GetVersionsAsync(DaResourceKind kind, string id, CancellationToken cancellationToken = default)
    {
        var versions = new List<int>();
        await foreach (var item in EnumeratePagesAsync($"/{kind.PathSegment()}/{Escape(id)}/versions", cancellationToken).ConfigureAwait(false))
        {
            versions.Add(item.GetValue<int>());
        }

        return versions;
    }

    /// <summary>
    /// <c>GET /{kind}/{id}/aliases</c> : alias → version, ou null si la ressource n'existe pas (404), comme <c>aliases()</c>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>?> GetAliasesAsync(DaResourceKind kind, string id, CancellationToken cancellationToken = default)
    {
        var path = $"/{kind.PathSegment()}/{Escape(id)}/aliases";
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var page = string.Empty;
        do
        {
            var response = await _http.CallAsync(HttpMethod.Get, path + PageQuery(page), null, [HttpStatusCode.OK, HttpStatusCode.NotFound], cancellationToken).ConfigureAwait(false);
            if (response.Status == HttpStatusCode.NotFound)
            {
                return null;
            }

            foreach (var alias in response.Body?["data"]?.AsArray() ?? [])
            {
                result[alias!["id"]!.GetValue<string>()] = alias["version"]!.GetValue<int>();
            }

            page = response.Body?["paginationToken"]?.GetValue<string>() ?? string.Empty;
        }
        while (page.Length > 0);

        return result;
    }

    /// <summary>
    /// <c>DELETE /{kind}/{id}/versions/{version}</c> (204). Aucune vérification ici : passer par
    /// <see cref="Packaging.VersionCleanupService"/>, qui exclut les versions référencées par un alias.
    /// </summary>
    public Task DeleteVersionAsync(DaResourceKind kind, string id, int version, CancellationToken cancellationToken = default) =>
        _http.CallAsync(HttpMethod.Delete, $"/{kind.PathSegment()}/{Escape(id)}/versions/{version}", null, [HttpStatusCode.NoContent, HttpStatusCode.OK], cancellationToken);

    // ---- AppBundles ----------------------------------------------------------------------------------------------

    /// <summary><c>POST /appbundles</c> : première version (la définition contient <c>id</c>).</summary>
    public Task<AppBundleVersion> CreateAppBundleAsync(JsonObject definition, CancellationToken cancellationToken = default) =>
        PostAppBundleAsync("/appbundles", definition, cancellationToken);

    /// <summary><c>POST /appbundles/{id}/versions</c> : nouvelle version, <c>id</c> retiré du corps.</summary>
    public Task<AppBundleVersion> CreateAppBundleVersionAsync(string id, JsonObject definition, CancellationToken cancellationToken = default) =>
        PostAppBundleAsync($"/appbundles/{Escape(id)}/versions", Packaging.DefinitionLoader.WithoutId(definition), cancellationToken);

    /// <summary>
    /// Téléverse le ZIP sur <c>endpointURL</c> en multipart/form-data : champs <c>formData</c> dans l'ordre reçu, puis le
    /// fichier en dernier (« The 'file' field must be at the end »). Aucun en-tête Authorization n'est envoyé à S3.
    /// </summary>
    public async Task UploadAppBundleAsync(UploadParameters upload, Stream zip, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(zip);
        using var content = new MultipartFormDataContent($"----egr{Guid.NewGuid():N}");
        foreach (var (key, value) in upload.FormData)
        {
            var field = new StringContent(value);
            field.Headers.ContentType = null; // comme deploy.py : aucun Content-Type par champ
            field.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = Quote(key) };
            content.Add(field);
        }

        var file = new StreamContent(zip);
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"file\"", FileName = Quote(fileName) };
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file);

        using var request = new HttpRequestMessage(HttpMethod.Post, upload.EndpointUrl) { Content = content };
        await ApsHttp.SendAsync(_httpClient, request, "POST", "<endpointURL de téléversement>", UploadExpected, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Téléverse un fichier ZIP local (voir <see cref="UploadAppBundleAsync(UploadParameters, Stream, string, CancellationToken)"/>).</summary>
    public async Task UploadAppBundleAsync(UploadParameters upload, string zipPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(zipPath);
        await UploadAppBundleAsync(upload, stream, Path.GetFileName(zipPath), cancellationToken).ConfigureAwait(false);
    }

    // ---- Activities ----------------------------------------------------------------------------------------------

    /// <summary><c>POST /activities</c> : première version ; retourne le numéro de version.</summary>
    public async Task<int> CreateActivityAsync(JsonObject definition, CancellationToken cancellationToken = default)
    {
        var response = await _http.CallAsync(HttpMethod.Post, "/activities", definition, cancellationToken).ConfigureAwait(false);
        return ApsHttp.Require(response, "version", "POST /activities").GetValue<int>();
    }

    /// <summary><c>POST /activities/{id}/versions</c> : nouvelle version, <c>id</c> retiré du corps.</summary>
    public async Task<int> CreateActivityVersionAsync(string id, JsonObject definition, CancellationToken cancellationToken = default)
    {
        var path = $"/activities/{Escape(id)}/versions";
        var response = await _http.CallAsync(HttpMethod.Post, path, Packaging.DefinitionLoader.WithoutId(definition), cancellationToken).ConfigureAwait(false);
        return ApsHttp.Require(response, "version", $"POST {path}").GetValue<int>();
    }

    // ---- Alias ---------------------------------------------------------------------------------------------------

    /// <summary><c>POST /{kind}/{id}/aliases</c>.</summary>
    public Task CreateAliasAsync(DaResourceKind kind, string id, string alias, int version, CancellationToken cancellationToken = default) =>
        _http.CallAsync(HttpMethod.Post, $"/{kind.PathSegment()}/{Escape(id)}/aliases", new JsonObject { ["id"] = alias, ["version"] = version }, cancellationToken);

    /// <summary><c>PATCH /{kind}/{id}/aliases/{alias}</c>.</summary>
    public Task UpdateAliasAsync(DaResourceKind kind, string id, string alias, int version, CancellationToken cancellationToken = default) =>
        _http.CallAsync(HttpMethod.Patch, $"/{kind.PathSegment()}/{Escape(id)}/aliases/{Escape(alias)}", new JsonObject { ["version"] = version }, cancellationToken);

    /// <summary>Crée l'alias ou le déplace s'il figure déjà dans <paramref name="existing"/> (<c>set_alias</c>).</summary>
    public Task SetAliasAsync(DaResourceKind kind, string id, string alias, int version, IReadOnlyDictionary<string, int>? existing, CancellationToken cancellationToken = default) =>
        existing is not null && existing.ContainsKey(alias)
            ? UpdateAliasAsync(kind, id, alias, version, cancellationToken)
            : CreateAliasAsync(kind, id, alias, version, cancellationToken);

    // ---- WorkItems -----------------------------------------------------------------------------------------------

    /// <summary><c>POST /workitems</c> : <c>activityId</c> qualifié (<c>nickname.Activity+alias</c>) et arguments.</summary>
    public async Task<WorkItemStatus> SubmitWorkItemAsync(JsonObject workItem, CancellationToken cancellationToken = default)
    {
        var response = await _http.CallAsync(HttpMethod.Post, "/workitems", workItem, cancellationToken).ConfigureAwait(false);
        return WorkItemStatus.FromJson(response.Body, "POST /workitems");
    }

    /// <summary><c>GET /workitems/{id}</c>.</summary>
    public async Task<WorkItemStatus> GetWorkItemAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _http.CallAsync(HttpMethod.Get, $"/workitems/{Escape(id)}", null, cancellationToken).ConfigureAwait(false);
        return WorkItemStatus.FromJson(response.Body, "GET /workitems/{id}");
    }

    /// <summary>
    /// Interroge <c>GET /workitems/{id}</c> jusqu'à un statut terminal. <paramref name="progress"/> reçoit chaque statut lu.
    /// L'annulation (ou <paramref name="timeout"/>) lève <see cref="OperationCanceledException"/> sans annuler le WorkItem.
    /// </summary>
    public async Task<WorkItemStatus> WaitForWorkItemAsync(
        string id,
        TimeSpan pollInterval,
        TimeSpan? timeout = null,
        IProgress<WorkItemStatus>? progress = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        timeProvider ??= TimeProvider.System;
        using var timeoutSource = timeout is null ? null : new CancellationTokenSource(timeout.Value, timeProvider);
        using var linked = timeoutSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        while (true)
        {
            var status = await GetWorkItemAsync(id, linked.Token).ConfigureAwait(false);
            progress?.Report(status);
            if (status.IsCompleted)
            {
                return status;
            }

            await Task.Delay(pollInterval, timeProvider, linked.Token).ConfigureAwait(false);
        }
    }

    /// <summary><c>DELETE /workitems/{id}</c> : annulation d'un WorkItem en attente ou en cours.</summary>
    public Task CancelWorkItemAsync(string id, CancellationToken cancellationToken = default) =>
        _http.CallAsync(HttpMethod.Delete, $"/workitems/{Escape(id)}", null, [HttpStatusCode.OK, HttpStatusCode.NoContent], cancellationToken);

    /// <summary>Télécharge le rapport (<c>reportUrl</c>, URL signée : aucun jeton envoyé, URL jamais citée).</summary>
    public async Task<string> DownloadReportAsync(WorkItemStatus status, CancellationToken cancellationToken = default)
    {
        var url = status.ReportUrl ?? throw new ApsException($"WorkItem {status.Id} : reportUrl absent.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApsException($"GET <reportUrl> : HTTP {(int)response.StatusCode}", response.StatusCode);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Interne -------------------------------------------------------------------------------------------------

    private async Task<AppBundleVersion> PostAppBundleAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        var label = $"POST {path}";
        var response = await _http.CallAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
        var version = ApsHttp.Require(response, "version", label).GetValue<int>();
        var upload = UploadParameters.FromJson(ApsHttp.Require(response, "uploadParameters", label), label);
        return new AppBundleVersion(version, upload);
    }

    private async Task<IReadOnlyList<string>> GetAllPagesAsync(string path, CancellationToken cancellationToken)
    {
        var items = new List<string>();
        await foreach (var item in EnumeratePagesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            items.Add(item.GetValue<string>());
        }

        return items;
    }

    private async IAsyncEnumerable<JsonNode> EnumeratePagesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = string.Empty;
        do
        {
            var response = await _http.CallAsync(HttpMethod.Get, path + PageQuery(page), null, cancellationToken).ConfigureAwait(false);
            foreach (var item in response.Body?["data"]?.AsArray() ?? [])
            {
                if (item is not null)
                {
                    yield return item;
                }
            }

            page = response.Body?["paginationToken"]?.GetValue<string>() ?? string.Empty;
        }
        while (page.Length > 0);
    }

    private static string PageQuery(string page) => page.Length == 0 ? string.Empty : $"?page={Uri.EscapeDataString(page)}";

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
