using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>
/// Lancement enregistré dans l'historique local (<c>runs/&lt;runId&gt;/run.json</c>). Aucune URL signée ni aucun
/// secret : seulement des identifiants, des noms de fichiers et des statuts.
/// </summary>
/// <summary>
/// Objet OSS d'un lancement (entrée ou sortie). <see cref="UploadKey"/> (sorties seulement) sert à finaliser l'objet
/// écrit par le moteur, y compris après un redémarrage (« Récupérer ») ; ce n'est ni un jeton ni une URL signée, il
/// n'est utilisable qu'avec un jeton APS valide. Les URL signées ne sont jamais enregistrées : elles sont redemandées.
/// </summary>
public sealed record RunObject(string Parameter, string ObjectKey, bool IsInput, string LocalName, string? UploadKey = null);

public sealed record RunRecord
{
    public required string RunId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string RevitVersion { get; init; }
    public required string Alias { get; init; }
    public required string ActivityId { get; init; }
    public required string ZipFileName { get; init; }
    public required string BucketKey { get; init; }

    /// <summary>Objets OSS du lancement (clés seulement), enregistrés avant la soumission.</summary>
    public IReadOnlyList<RunObject> Objects { get; init; } = [];
    public string? WorkItemId { get; init; }
    public string? WorkItemStatus { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? Stats { get; init; }

    /// <summary>Sorties récupérées (nom local → présent ou non), utile pour D6.</summary>
    public IReadOnlyDictionary<string, bool> Outputs { get; init; } = new Dictionary<string, bool>();
    public string? ResultStatus { get; init; }
    public int? SchemaErrorCount { get; init; }
    public bool ReportDownloaded { get; init; }
    public string? Error { get; init; }

    [JsonIgnore]
    public string Summary =>
        $"{CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} · Revit {RevitVersion} +{Alias} · {WorkItemStatus ?? "non soumis"}" +
        (ResultStatus is null ? string.Empty : $" · result.json {ResultStatus}");
}

/// <summary>Historique des lancements : un dossier par lancement (entrées copiées, sorties, rapport, run.json).</summary>
public sealed class RunHistory(string dataDirectory)
{
    public const string RecordFileName = "run.json";
    public const string ReportFileName = "report.txt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string RunsDirectory => Path.Combine(dataDirectory, "runs");

    public string DirectoryOf(string runId) => Path.Combine(RunsDirectory, runId);

    public void Save(RunRecord record)
    {
        var directory = Directory.CreateDirectory(DirectoryOf(record.RunId)).FullName;
        var temporary = Path.Combine(directory, RecordFileName + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(record, JsonOptions));
        File.Move(temporary, Path.Combine(directory, RecordFileName), overwrite: true);
    }

    /// <summary>Lancements du plus récent au plus ancien ; un run.json illisible est ignoré.</summary>
    public IReadOnlyList<RunRecord> Load()
    {
        if (!Directory.Exists(RunsDirectory))
        {
            return [];
        }

        var records = new List<RunRecord>();
        foreach (var file in Directory.EnumerateFiles(RunsDirectory, RecordFileName, SearchOption.AllDirectories))
        {
            try
            {
                if (JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file), JsonOptions) is { } record)
                {
                    records.Add(record);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // run.json illisible, verrouillé ou incomplet : ignoré, les autres lancements restent affichés.
            }
        }

        return [.. records.OrderByDescending(r => r.CreatedAtUtc)];
    }

    /// <summary>Exporte le dossier d'un lancement (run.json, result.json, rapport, settings.json) dans un ZIP.</summary>
    public void Export(string runId, string zipPath)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(DirectoryOf(runId), zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    public static string NewRunId(DateTimeOffset now) => $"{now.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
}
