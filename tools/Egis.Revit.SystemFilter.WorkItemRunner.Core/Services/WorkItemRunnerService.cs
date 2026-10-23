using System.Globalization;
using System.Text;
using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Oss;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>Demande de lancement saisie dans l'interface : maquette Revit et options facultatives.</summary>
public sealed record RunRequest(string RevitVersion, string Alias, string Nickname, string ModelPath, string OptionsJson);

/// <summary>Lancement préparé (contrôles locaux et lectures seules faits) : plan à confirmer avant toute écriture.</summary>
public sealed record PreparedRun(
    RunRequest Request,
    string RunId,
    string ActivityId,
    string BucketKey,
    IReadOnlyList<ActivityParameter> Parameters,
    CheckResult ModelCheck,
    CheckResult OptionsCheck,
    OperationPlan Plan);

/// <summary>WorkItem soumis ; <see cref="Record"/> (enregistré avant la soumission) suffit à récupérer les sorties.</summary>
public sealed class ActiveRun(RunRecord record)
{
    public RunRecord Record { get; internal set; } = record;
}

/// <summary>
/// Parcours d'un WorkItem : préparation (lecture seule), puis après confirmation téléversement OSS par URL signées,
/// soumission, suivi, rapport et récupération de la sortie <c>systems-inventory.json</c>. Les messages de progression
/// ne citent que des clés d'objets, jamais une URL signée.
/// </summary>
public sealed class WorkItemRunnerService(
    DesignAutomationClient designAutomation,
    OssClient oss,
    DefinitionLoader definitions,
    InventoryReader results,
    RunHistory history,
    TimeProvider? timeProvider = null)
{
    /// <summary>Inventaire extrait du ZIP de sortie (paramètre <c>result</c>) et conservé dans l'historique local.</summary>
    public const string ResultLocalName = "systems-inventory.json";

    /// <summary>Journal du moteur extrait du ZIP de sortie.</summary>
    public const string RunLogName = "run.log";

    /// <summary>Paramètre de sortie de l'Activity.</summary>
    public const string ResultParameter = "result";

    /// <summary>Nom local de l'entrée <c>inputZip</c> (ZIP contenant la maquette à sa racine).</summary>
    public const string ModelLocalName = "input";

    /// <summary>Nom local de l'entrée <c>settings</c>.</summary>
    public const string OptionsLocalName = "settings.json";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public RunHistory History => history;

    public InventoryReader Results => results;

    /// <summary>Activity qualifiée : <c>nickname.SystemsInventory_R&lt;année&gt;+alias</c>.</summary>
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

        var modelCheck = InputChecks.CheckModel(request.ModelPath);
        if (!modelCheck.IsValid)
        {
            throw new InvalidOperationException("Maquette refusée : " + string.Join(" ; ", modelCheck.Errors));
        }

        var optionsCheck = InputChecks.CheckOptions(request.OptionsJson);
        if (!optionsCheck.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ; ", optionsCheck.Errors));
        }

        var activity = definitions.Load(request.RevitVersion, DaResourceKind.Activity, request.Nickname, request.Alias);
        var parameters = ActivityParameter.FromDefinition(activity);

        var unqualified = activity["id"]!.GetValue<string>();
        var aliases = await designAutomation.GetAliasesAsync(DaResourceKind.Activity, unqualified, cancellationToken).ConfigureAwait(false);
        if (aliases is null || !aliases.TryGetValue(request.Alias, out var activityVersion))
        {
            throw new InvalidOperationException($"Activity {unqualified}+{request.Alias} introuvable : la publier d'abord (DaManager).");
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
                if (!HasInput(parameter, request))
                {
                    steps.Add($"Entrée {parameter.Name} ({parameter.Verb}) : non fournie (facultative).");
                    continue;
                }

                var (name, bytes) = DescribeInput(parameter, request);
                steps.Add($"Entrée {parameter.Name} ({parameter.Verb}) : {name} ({Size(bytes)}) -> {objectKey}, URL signée de lecture.");
            }
            else
            {
                steps.Add($"Sortie {parameter.Name} ({parameter.Verb}) : URL signée d'écriture -> {objectKey}.");
            }
        }

        steps.Add("POST /workitems : exécution facturée en crédits APS.");
        steps.Add($"Suivi GET /workitems/{{id}} toutes les {pollInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s, puis rapport et sortie dans l'historique local ({runId}).");
        steps.Add("Les URL signées expirent après 60 min : un WorkItem plus long ne pourrait plus écrire la sortie.");
        steps.AddRange(modelCheck.Warnings.Concat(optionsCheck.Warnings).Select(w => "ATTENTION : " + w));
        return new PreparedRun(request, runId, activityId, bucketKey, parameters, modelCheck, optionsCheck, new OperationPlan("Lancer un WorkItem", steps));
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
            ZipFileName = Path.GetFileName(request.ModelPath),
            BucketKey = prepared.BucketKey,
        };
        history.Save(record);
        if (!string.IsNullOrWhiteSpace(request.OptionsJson))
        {
            await File.WriteAllTextAsync(Path.Combine(history.DirectoryOf(record.RunId), OptionsLocalName), request.OptionsJson, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }

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
                    if (!HasInput(parameter, request))
                    {
                        continue;
                    }

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

    /// <summary>Rapport et sortie : la sortie est finalisée puis téléchargée, lue par <see cref="InventoryReader"/>.</summary>
    public async Task<(RunRecord Record, InventoryView? Result, string? Report)> CollectAsync(ActiveRun run, WorkItemStatus status, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var (record, result, report) = await CollectAsync(run.Record, status, progress, cancellationToken).ConfigureAwait(false);
        run.Record = record;
        return (record, result, report);
    }

    /// <summary>« Récupérer » : relit le statut du WorkItem puis, s'il est terminé, télécharge rapport et sortie.</summary>
    public async Task<(RunRecord Record, InventoryView? Result, string? Report)> RecoverAsync(RunRecord record, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var id = record.WorkItemId ?? throw new InvalidOperationException($"Lancement {record.RunId} : aucun WorkItem soumis.");
        if (!record.Objects.Any(o => !o.IsInput))
        {
            throw new InvalidOperationException($"Lancement {record.RunId} : clés d'objets OSS absentes de l'historique (lancement antérieur) : récupération impossible.");
        }

        WorkItemStatus? status;
        try
        {
            status = await designAutomation.GetWorkItemAsync(id, cancellationToken).ConfigureAwait(false);
            progress($"WorkItem {id} : {status.Status}.");
        }
        catch (ApsException exception)
        {
            status = null;
            progress($"Statut du WorkItem {id} indisponible ({exception.Message}) : récupération de la sortie seulement.");
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

    private async Task<(RunRecord Record, InventoryView? Result, string? Report)> CollectAsync(RunRecord record, WorkItemStatus? status, Action<string> progress, CancellationToken cancellationToken)
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
        InventoryView? result = null;
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
                    completion = exception.Message;
                }
            }

            try
            {
                var bytes = await oss.DownloadObjectAsync(record.BucketKey, output.ObjectKey, cancellationToken).ConfigureAwait(false);
                var localName = output.Parameter == ResultParameter ? "output.zip" : output.LocalName;
                await File.WriteAllBytesAsync(Path.Combine(directory, localName), bytes, cancellationToken).ConfigureAwait(false);
                present[localName] = true;
                progress($"Sortie {output.Parameter} récupérée : {localName} ({Size(bytes.Length)}).");
                if (output.Parameter == ResultParameter)
                {
                    var json = ExtractOutput(bytes, directory);
                    present[ResultLocalName] = json is not null;
                    result = json is null ? null : results.Read(json);
                }
            }
            catch (ApsException exception)
            {
                present[output.LocalName] = false;
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
            ReportDownloaded = report is not null || record.ReportDownloaded,
        };
        history.Save(record);

        var resultPresence = present.GetValueOrDefault(ResultLocalName) ? "présent" : "absent";
        progress($"Relevé : WorkItem {record.WorkItemStatus ?? "?"}, {ResultLocalName} {resultPresence}.");
        return (record, result, report);
    }

    /// <summary>Relit un lancement de l'historique (sortie et rapport locaux).</summary>
    public (InventoryView? Result, string? Report) LoadRun(RunRecord record)
    {
        var directory = history.DirectoryOf(record.RunId);
        var resultPath = Path.Combine(directory, ResultLocalName);
        var reportPath = Path.Combine(directory, RunHistory.ReportFileName);
        return (
            File.Exists(resultPath) ? results.Read(File.ReadAllText(resultPath)) : null,
            File.Exists(reportPath) ? File.ReadAllText(reportPath) : null);
    }

    /// <summary>Extrait systems-inventory.json et run.log du ZIP de sortie (entrées à plat uniquement, 256 Mo max).</summary>
    private static string? ExtractOutput(byte[] zip, string directory)
    {
        string? json = null;
        try
        {
            using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read);
            foreach (var name in new[] { ResultLocalName, RunLogName })
            {
                var entry = archive.GetEntry(name);
                if (entry is null || entry.Length > 256L * 1024 * 1024)
                {
                    continue;
                }

                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = reader.ReadToEnd();
                File.WriteAllText(Path.Combine(directory, name), text, new UTF8Encoding(false));
                if (name == ResultLocalName)
                {
                    json = text;
                }
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return json;
    }

    /// <summary>ZIP d'entrée : la maquette seule, à la racine (pas de liens ; settings.json sans mainModelPath).</summary>
    private static MemoryStream ModelZip(string modelPath)
    {
        var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var target = archive.CreateEntry(Path.GetFileName(modelPath), System.IO.Compression.CompressionLevel.Fastest).Open();
            using var source = File.OpenRead(modelPath);
            source.CopyTo(target);
        }

        stream.Position = 0;
        return stream;
    }

    private static string ObjectKey(string runId, ActivityParameter parameter) =>
        $"{runId}/{(parameter.Zip ? parameter.Name + ".zip" : parameter.LocalName ?? parameter.Name)}";

    /// <summary>Vrai si l'entrée a une source : la maquette toujours, les options seulement si l'éditeur n'est pas vide.</summary>
    private static bool HasInput(ActivityParameter parameter, RunRequest request) =>
        parameter.LocalName == ModelLocalName || (parameter.LocalName == OptionsLocalName && !string.IsNullOrWhiteSpace(request.OptionsJson));

    private static Stream InputFor(ActivityParameter parameter, RunRequest request) =>
        parameter.LocalName == ModelLocalName ? ModelZip(request.ModelPath)
        : parameter.LocalName == OptionsLocalName ? new MemoryStream(new UTF8Encoding(false).GetBytes(request.OptionsJson))
        : throw new InvalidOperationException($"Paramètre d'entrée non pris en charge : {parameter.Name} ({parameter.LocalName}).");

    private static (string Name, long Bytes) DescribeInput(ActivityParameter parameter, RunRequest request) =>
        parameter.LocalName == ModelLocalName
            ? (Path.GetFileName(request.ModelPath), new FileInfo(request.ModelPath).Length)
            : ("settings.json (éditeur)", Encoding.UTF8.GetByteCount(request.OptionsJson));

    private static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{(bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} Mo" : $"{(bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} Ko";
}
