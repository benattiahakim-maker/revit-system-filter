using System.Globalization;
using System.IO.Compression;
using System.Text;
using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Oss;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>Demande de lancement saisie dans l'interface.</summary>
public sealed record RunRequest(string RevitVersion, string Alias, string Nickname, string ZipPath, string SettingsJson);

/// <summary>Lancement préparé (contrôles locaux et lectures seules faits) : plan à confirmer avant toute écriture.</summary>
public sealed record PreparedRun(
    RunRequest Request,
    string RunId,
    string ActivityId,
    string BucketKey,
    IReadOnlyList<ActivityParameter> Parameters,
    CheckResult ZipCheck,
    CheckResult SettingsCheck,
    OperationPlan Plan);

/// <summary>
/// WorkItem soumis. Tout ce qu'il faut pour récupérer les sorties (bucket, clés d'objets, clés de finalisation) est
/// dans <see cref="Record"/>, enregistré sur disque avant la soumission : « Récupérer » fonctionne après un redémarrage.
/// </summary>
public sealed class ActiveRun(RunRecord record)
{
    public RunRecord Record { get; internal set; } = record;
}

/// <summary>
/// Parcours d'un WorkItem EG_ROOM_TRACKER (EGR-20) : préparation (lecture seule), puis après confirmation
/// téléversement OSS par URL signées, soumission, suivi, rapport et récupération des sorties. Les messages de
/// progression ne citent que des clés d'objets, jamais une URL signée.
/// </summary>
public sealed class WorkItemRunnerService(
    DesignAutomationClient designAutomation,
    OssClient oss,
    DefinitionLoader definitions,
    ResultReader results,
    RunHistory history,
    TimeProvider? timeProvider = null)
{
    public const string ResultLocalName = "systems-inventory.json";

    /// <summary>Paramètre de sortie de l'Activity : le dossier <c>result/</c> zippé (<c>systems-inventory.json</c> et <c>Images/</c>).</summary>
    public const string ResultParameter = "result";

    /// <summary>Fichier de sortie au format TRC (<c>outputFormat</c> <c>trc</c> ou <c>both</c>), à la racine du dossier <c>result/</c>.</summary>
    public const string TrcLocalName = "TRCValidationDict.json";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public RunHistory History => history;

    public ResultReader Results => results;

    /// <summary>Activity qualifiée : <c>nickname.SystemFilter_R&lt;année&gt;+alias</c>.</summary>
    public string QualifiedActivityId(string nickname, string revitVersion, string alias) =>
        $"{nickname}.{definitions.IdOf(revitVersion, DaResourceKind.Activity)}+{alias}";

    /// <summary>Contrôles locaux, vérification de l'alias d'Activity (lecture seule) et plan. Aucune écriture.</summary>
    public async Task<PreparedRun> PrepareAsync(RunRequest request, string? bucketKey, TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucketKey))
        {
            throw new InvalidOperationException("Bucket OSS inconnu : définir APS_BUCKET (ou APS_CLIENT_ID).");
        }

        if (!RunnerOptions.IsValidBucketKey(bucketKey))
        {
            throw new InvalidOperationException($"Bucket « {bucketKey} » invalide : caractères [-_.a-z0-9], 3 à 128 (règle OSS).");
        }

        var zipCheck = InputChecks.CheckZip(request.ZipPath);
        if (!zipCheck.IsValid)
        {
            throw new InvalidOperationException("ZIP refusé : " + string.Join(" ; ", zipCheck.Errors));
        }

        var settingsCheck = InputChecks.CheckSettings(request.SettingsJson);
        if (!settingsCheck.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ; ", settingsCheck.Errors));
        }

        var activity = definitions.Load(request.RevitVersion, DaResourceKind.Activity, request.Nickname, request.Alias);
        var parameters = ActivityParameter.FromDefinition(activity);
        foreach (var parameter in parameters.Where(p => p.IsInput))
        {
            using (InputFor(parameter, request)) // paramètre d'entrée sans source connue : erreur avant tout appel ; flux refermé aussitôt (le ZIP ne reste pas verrouillé)
            {
            }
        }

        var unqualified = activity["id"]!.GetValue<string>();
        var aliases = await designAutomation.GetAliasesAsync(DaResourceKind.Activity, unqualified, cancellationToken).ConfigureAwait(false);
        if (aliases is null || !aliases.TryGetValue(request.Alias, out var activityVersion))
        {
            throw new InvalidOperationException($"Activity {unqualified}+{request.Alias} introuvable : la publier d'abord (DaManager ou deploy.py).");
        }

        var runId = RunHistory.NewRunId(_time.GetUtcNow());
        var activityId = $"{request.Nickname}.{unqualified}+{request.Alias}";
        var steps = new List<string>
        {
            $"WorkItem sur {activityId} (version v{activityVersion}), Revit {request.RevitVersion}.",
            $"OSS : bucket {bucketKey} (transient, objets supprimés après 24 h ; créé s'il n'existe pas).",
        };
        foreach (var parameter in parameters)
        {
            var objectKey = ObjectKey(runId, parameter);
            if (parameter.IsInput)
            {
                var (name, bytes) = DescribeInput(parameter, request);
                steps.Add($"Entrée {parameter.Name} ({parameter.Verb}{(parameter.Zip ? ", zip" : string.Empty)}) : {name} ({Size(bytes)}) -> {objectKey}, URL signée de lecture.");
            }
            else
            {
                steps.Add($"Sortie {parameter.Name} ({parameter.Verb}) : URL signée d'écriture -> {objectKey}.");
            }
        }

        steps.Add("POST /workitems : exécution facturée en crédits APS.");
        steps.Add($"Suivi GET /workitems/{{id}} toutes les {pollInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s, puis rapport (valable 24 h) et sorties dans l'historique local ({runId}).");
        steps.Add("Les URL signées expirent après 60 min : un WorkItem plus long ne pourrait plus écrire systems-inventory.json.");
        steps.Add("Historique local : bucket, clés d'objets et identifiant du WorkItem (jamais d'URL signée ni de jeton) ; « Récupérer » relit le statut et télécharge les sorties plus tard, même après un redémarrage.");
        steps.AddRange(zipCheck.Warnings.Concat(settingsCheck.Warnings).Select(w => "ATTENTION : " + w));
        return new PreparedRun(request, runId, activityId, bucketKey, parameters, zipCheck, settingsCheck, new OperationPlan("Lancer un WorkItem", steps));
    }

    /// <summary>Après confirmation : bucket, téléversements, URL signées, soumission. Retourne le lancement actif.</summary>
    public async Task<ActiveRun> SubmitAsync(PreparedRun prepared, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var request = prepared.Request;
        var record = new RunRecord
        {
            RunId = prepared.RunId,
            CreatedAtUtc = _time.GetUtcNow(),
            RevitVersion = request.RevitVersion,
            Alias = request.Alias,
            ActivityId = prepared.ActivityId,
            ZipFileName = Path.GetFileName(request.ZipPath),
            BucketKey = prepared.BucketKey,
        };
        history.Save(record);
        await File.WriteAllTextAsync(Path.Combine(history.DirectoryOf(record.RunId), "settings.json"), request.SettingsJson, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        try
        {
            if (await oss.CreateBucketAsync(prepared.BucketKey, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                progress($"Bucket {prepared.BucketKey} créé.");
            }

            var inputUrls = new Dictionary<string, string>();
            var outputUrls = new Dictionary<string, string>();
            var objects = new List<RunObject>();
            foreach (var parameter in prepared.Parameters)
            {
                var objectKey = ObjectKey(record.RunId, parameter);
                var localName = parameter.LocalName ?? parameter.Name;
                if (parameter.IsInput)
                {
                    await using var content = InputFor(parameter, request);
                    await oss.UploadObjectAsync(prepared.BucketKey, objectKey, content, content.Length, cancellationToken: cancellationToken).ConfigureAwait(false);
                    progress($"Téléversé : {objectKey}");
                    inputUrls[parameter.Name] = await oss.GetSignedDownloadUrlAsync(prepared.BucketKey, objectKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                    objects.Add(new RunObject(parameter.Name, objectKey, true, localName));
                }
                else
                {
                    var upload = await oss.GetSignedUploadAsync(prepared.BucketKey, objectKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                    outputUrls[parameter.Name] = upload.Urls[0];
                    objects.Add(new RunObject(parameter.Name, objectKey, false, localName, upload.UploadKey));
                }
            }

            // Clés enregistrées avant la soumission : la récupération reste possible si l'application est fermée.
            record = record with { Objects = objects };
            history.Save(record);

            var workItem = WorkItemArguments.Build(prepared.ActivityId, prepared.Parameters, inputUrls, outputUrls);
            var status = await designAutomation.SubmitWorkItemAsync(workItem, cancellationToken).ConfigureAwait(false);
            record = record with { WorkItemId = status.Id, WorkItemStatus = status.Status };
            history.Save(record);
            progress($"WorkItem {status.Id} soumis ({status.Status}).");
            return new ActiveRun(record);
        }
        catch (Exception exception) when (exception is ApsException or IOException or HttpRequestException)
        {
            history.Save(record with { Error = exception.Message });
            throw;
        }
    }

    /// <summary>Suivi jusqu'à un statut terminal ; l'annulation arrête seulement le suivi (le WorkItem continue).</summary>
    public async Task<WorkItemStatus> WaitAsync(ActiveRun run, TimeSpan pollInterval, IProgress<WorkItemStatus>? progress, CancellationToken cancellationToken)
    {
        var id = run.Record.WorkItemId ?? throw new InvalidOperationException("WorkItem non soumis.");
        var status = await designAutomation.WaitForWorkItemAsync(id, pollInterval, progress: progress, timeProvider: _time, cancellationToken: cancellationToken).ConfigureAwait(false);
        run.Record = run.Record with { WorkItemStatus = status.Status };
        history.Save(run.Record);
        return status;
    }

    /// <summary><c>DELETE /workitems/{id}</c> (après confirmation).</summary>
    public Task CancelAsync(ActiveRun run, CancellationToken cancellationToken = default) =>
        designAutomation.CancelWorkItemAsync(run.Record.WorkItemId ?? throw new InvalidOperationException("WorkItem non soumis."), cancellationToken);

    /// <summary>
    /// Rapport et sorties : chaque sortie est finalisée puis téléchargée par une URL signée de lecture demandée à
    /// l'instant ; une sortie absente est notée (D6) sans erreur. <c>systems-inventory.json</c> est lu et validé contre le schéma.
    /// </summary>
    public async Task<(RunRecord Record, ResultView? Result, string? Report)> CollectAsync(ActiveRun run, WorkItemStatus status, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var (record, result, report) = await CollectAsync(run.Record, status, progress, cancellationToken).ConfigureAwait(false);
        run.Record = record;
        return (record, result, report);
    }

    /// <summary>
    /// « Récupérer » : relit le statut du WorkItem d'un lancement de l'historique (même après un redémarrage), puis,
    /// s'il est terminé, télécharge le rapport et les sorties. Un WorkItem encore en cours est seulement mis à jour.
    /// </summary>
    public async Task<(RunRecord Record, ResultView? Result, string? Report)> RecoverAsync(RunRecord record, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var id = record.WorkItemId ?? throw new InvalidOperationException($"Lancement {record.RunId} : aucun WorkItem soumis.");
        if (!record.Objects.Any(o => !o.IsInput))
        {
            throw new InvalidOperationException($"Lancement {record.RunId} : clés d'objets OSS absentes de l'historique (lancement antérieur à cette version) : récupération impossible.");
        }

        WorkItemStatus? status;
        try
        {
            status = await designAutomation.GetWorkItemAsync(id, cancellationToken).ConfigureAwait(false);
            progress($"WorkItem {id} : {status.Status}.");
        }
        catch (ApsException exception)
        {
            // Statut conservé 3 jours par APS : au-delà, on tente quand même les sorties (bucket transient, 24 h).
            status = null;
            progress($"Statut du WorkItem {id} indisponible ({exception.Message}) : récupération des sorties seulement.");
        }

        if (status is { IsCompleted: false })
        {
            record = record with { WorkItemStatus = status.Status };
            history.Save(record);
            progress($"WorkItem {id} toujours {status.Status} : réessayer « Récupérer » plus tard.");
            return (record, null, null);
        }

        return await CollectAsync(record, status, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(RunRecord Record, ResultView? Result, string? Report)> CollectAsync(RunRecord record, WorkItemStatus? status, Action<string> progress, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateDirectory(history.DirectoryOf(record.RunId)).FullName;
        string? report = null;
        if (status?.ReportUrl is not null)
        {
            try
            {
                report = await designAutomation.DownloadReportAsync(status, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(directory, RunHistory.ReportFileName), report, cancellationToken).ConfigureAwait(false);
                progress("Rapport APS téléchargé.");
            }
            catch (ApsException exception)
            {
                progress($"Rapport indisponible : {exception.Message}");
            }
        }

        var present = new Dictionary<string, bool>();
        ResultView? result = null;
        foreach (var output in record.Objects.Where(o => !o.IsInput))
        {
            string? completion = null;
            if (output.UploadKey is not null)
            {
                try
                {
                    await oss.CompleteUploadAsync(record.BucketKey, output.ObjectKey, output.UploadKey, cancellationToken).ConfigureAwait(false);
                }
                catch (ApsException exception)
                {
                    completion = exception.Message; // déjà finalisé (récupération répétée) ou jamais écrit : le téléchargement tranche
                }
            }

            // Sortie ZIP (dossier result/ : systems-inventory.json et Images/) ou fichier seul (lancements antérieurs).
            var isZip = output.ObjectKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            var key = isZip && output.Parameter == ResultParameter ? ResultLocalName : output.LocalName;
            try
            {
                var bytes = await oss.DownloadObjectAsync(record.BucketKey, output.ObjectKey, cancellationToken).ConfigureAwait(false);
                if (isZip)
                {
                    var (resultJson, trcJson) = ExtractOutputZip(directory, output.LocalName, bytes, out var images);
                    present[key] = output.Parameter != ResultParameter || resultJson is not null || trcJson is not null;
                    progress($"Sortie {output.Parameter} récupérée : {output.LocalName}.zip ({Size(bytes.Length)}), décompressée dans {output.LocalName}/ ({images} image(s)).");
                    if (output.Parameter == ResultParameter)
                    {
                        // Contrat (systems-inventory.json) prioritaire ; sinon fichier TRC (données seules, sans statut).
                        if (resultJson is not null)
                        {
                            result = results.Read(resultJson);
                        }
                        else if (trcJson is not null)
                        {
                            result = results.ReadTrc(trcJson);
                        }
                    }
                }
                else
                {
                    await File.WriteAllBytesAsync(Path.Combine(directory, output.LocalName), bytes, cancellationToken).ConfigureAwait(false);
                    present[key] = true;
                    progress($"Sortie {output.Parameter} récupérée : {output.LocalName} ({Size(bytes.Length)}).");
                    if (output.LocalName == ResultLocalName)
                    {
                        result = results.Read(Encoding.UTF8.GetString(bytes));
                    }
                }
            }
            catch (InvalidDataException)
            {
                present[key] = false;
                progress($"Sortie {output.Parameter} : le fichier téléchargé n'est pas un ZIP valide.");
            }
            catch (ApsException exception)
            {
                present[key] = false;
                progress($"Sortie {output.Parameter} absente ({output.LocalName}) : {completion ?? exception.Message}");
            }
        }

        record = record with
        {
            WorkItemStatus = status?.Status ?? record.WorkItemStatus,
            CompletedAtUtc = _time.GetUtcNow(),
            Stats = status?.Stats?.ToJsonString() ?? record.Stats,
            Outputs = present,
            ResultStatus = result?.Status,
            SchemaErrorCount = result?.SchemaErrors.Count,
            ReportDownloaded = report is not null || record.ReportDownloaded,
        };
        history.Save(record);

        // Relevé D6 (src/Packaging/README.md) : statut du WorkItem et présence de systems-inventory.json.
        var resultPresence = present.GetValueOrDefault(ResultLocalName) ? $"présent (status {result?.Status ?? "?"})" : "absent";
        progress($"Relevé : WorkItem {record.WorkItemStatus ?? "?"}, systems-inventory.json {resultPresence}.");
        return (record, result, report);
    }

    /// <summary>Relit un lancement de l'historique (systems-inventory.json et rapport locaux).</summary>
    public (ResultView? Result, string? Report) LoadRun(RunRecord record)
    {
        var directory = history.DirectoryOf(record.RunId);
        var zippedResult = Path.Combine(directory, ResultParameter, ResultLocalName);
        var resultPath = File.Exists(zippedResult) ? zippedResult : Path.Combine(directory, ResultLocalName);
        var trcPath = Path.Combine(directory, ResultParameter, TrcLocalName);
        var reportPath = Path.Combine(directory, RunHistory.ReportFileName);
        return (
            File.Exists(resultPath) ? results.Read(File.ReadAllText(resultPath))
                : File.Exists(trcPath) ? results.ReadTrc(File.ReadAllText(trcPath))
                : null,
            File.Exists(reportPath) ? File.ReadAllText(reportPath) : null);
    }

    /// <summary>
    /// Écrit le ZIP de sortie et le décompresse dans <c>&lt;lancement&gt;/&lt;nom local&gt;/</c> (l'extraction refuse tout chemin
    /// qui sortirait du dossier). Retourne le contenu de <c>systems-inventory.json</c>, null s'il est absent ; <paramref name="images"/> :
    /// nombre de fichiers du dossier <c>Images/</c>.
    /// </summary>
    private static (string? Contract, string? Trc) ExtractOutputZip(string directory, string localName, byte[] bytes, out int images)
    {
        var zipPath = Path.Combine(directory, localName + ".zip");
        File.WriteAllBytes(zipPath, bytes);
        var target = Path.Combine(directory, localName);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        ZipFile.ExtractToDirectory(zipPath, target);
        var imagesDirectory = Path.Combine(target, "Images");
        images = Directory.Exists(imagesDirectory) ? Directory.EnumerateFiles(imagesDirectory).Count() : 0;
        var inner = Path.Combine(target, ResultLocalName);
        var trc = Path.Combine(target, TrcLocalName);
        return (File.Exists(inner) ? File.ReadAllText(inner) : null, File.Exists(trc) ? File.ReadAllText(trc) : null);
    }

    private static string ObjectKey(string runId, ActivityParameter parameter) =>
        $"{runId}/{(parameter.Zip ? parameter.Name + ".zip" : parameter.LocalName ?? parameter.Name)}";

    /// <summary>Source d'une entrée : le ZIP pour un paramètre <c>zip: true</c>, l'éditeur pour <c>settings.json</c>.</summary>
    private static Stream InputFor(ActivityParameter parameter, RunRequest request) =>
        parameter.Zip ? File.OpenRead(request.ZipPath)
        : parameter.LocalName == "settings.json" ? new MemoryStream(new UTF8Encoding(false).GetBytes(request.SettingsJson))
        : throw new InvalidOperationException($"Paramètre d'entrée non pris en charge : {parameter.Name} ({parameter.LocalName}).");

    private static (string Name, long Bytes) DescribeInput(ActivityParameter parameter, RunRequest request) =>
        parameter.Zip ? (Path.GetFileName(request.ZipPath), new FileInfo(request.ZipPath).Length) : ("settings.json (éditeur)", Encoding.UTF8.GetByteCount(request.SettingsJson));

    private static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{(bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} Mo" : $"{(bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} Ko";
}
