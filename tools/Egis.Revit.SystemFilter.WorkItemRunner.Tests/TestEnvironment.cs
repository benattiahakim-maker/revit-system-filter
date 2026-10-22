using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;
using Egis.Revit.SystemFilter.WorkItemRunner.ViewModels;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

/// <summary>Faux dépôt (définitions réelles), maquette .rvt et ViewModel branché sur <see cref="FakeAps"/>.</summary>
internal sealed class TestEnvironment : IDisposable
{
    public const string Token = "jeton-tres-secret";
    public const string Bucket = "egsf-test-bucket";

    /// <summary>Exemple minimal de systems-inventory.json (deux types, deux systèmes, trois éléments).</summary>
    public const string ExampleResult = """
        {
          "schemaVersion": "1.0",
          "generatedAtUtc": "2026-10-21T12:00:00Z",
          "model": { "title": "EGI-TTR-0100-CVC", "revitVersion": "2026" },
          "summary": { "systemTypeCount": 2, "systemCount": 2, "elementCount": 3 },
          "systemTypes": [
            { "id": 111, "name": "CTA BUREAUX ASF", "domain": "Duct", "classification": "SupplyAir" },
            { "id": 112, "name": "EU", "domain": "Pipe", "classification": "Sanitary" }
          ],
          "systems": [
            { "id": 201, "uniqueId": "u-201", "name": "ARJ CTA LT 35", "systemTypeId": 111 },
            { "id": 202, "uniqueId": "u-202", "name": "EU-01", "systemTypeId": 112 }
          ],
          "elements": [
            { "id": 9001, "uniqueId": "u-9001", "category": "DuctFitting", "domain": "Duct", "family": "Coude", "type": "R1", "name": "C1", "level": "R+12", "size": "300x200", "lengthMm": 2450.0, "systemId": 201 },
            { "id": 9002, "uniqueId": "u-9002", "category": "MechanicalEquipment", "domain": "Duct", "family": "CTA", "type": "T1", "name": "CTA-1", "level": "R+12", "materiel": "CTA BUREAUX", "numCta": "ASF-01", "systemId": 201 },
            { "id": 9003, "uniqueId": "u-9003", "category": "PipeCurves", "domain": "Pipe", "family": "Tube", "type": "PVC", "name": "P1", "level": "R+1", "size": "DN50", "lengthMm": 1200.0, "systemId": 202 }
          ]
        }
        """;

    private readonly TempDirectory _temp = new();
    private readonly RunnerOptions _options;

    public TestEnvironment(bool withCredentials = true)
    {
        var dataRepo = Path.Combine(AppContext.BaseDirectory, "Data", "repo");
        foreach (var file in Directory.EnumerateFiles(dataRepo, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(dataRepo, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        File.WriteAllText(Path.Combine(Root, "Egis.Revit.SystemFilter.sln"), string.Empty);
        ModelPath = Path.Combine(_temp.Path, "EGI-TTR-0100-CVC.rvt");
        File.WriteAllText(ModelPath, "RVT");
        Server.ActivityAliases["SystemsInventory_R2026"] = new() { ["test"] = 3, ["prod"] = 2 };
        Server.ResultJson = ExampleResult;

        _options = new RunnerOptions
        {
            Aps = new ApsSettings { Token = withCredentials ? Token : null, Nickname = "nick", DaBaseUrl = FakeAps.Da, OssBaseUrl = FakeAps.Oss },
            RepositoryRoot = Root,
            BucketKey = Bucket,
            DataDirectory = DataDirectory,
        };
        ViewModel = new MainViewModel(_options, new HttpClient(Server), Confirmation, Files)
        {
            ModelPath = ModelPath,
            PollIntervalSeconds = 0,
            SelectedRevitVersion = "2026",
            OptionsText = string.Empty,
        };
    }

    public string Root => Path.Combine(_temp.Path, "repo");

    public string DataDirectory => Path.Combine(_temp.Path, "data");

    public string ModelPath { get; }

    public FakeAps Server { get; } = new();

    public FakeConfirmation Confirmation { get; } = new();

    public FakeFiles Files { get; } = new();

    public MainViewModel ViewModel { get; private set; }

    /// <summary>Simule un redémarrage de l'application : nouveau ViewModel, même historique local, journal vide.</summary>
    public void Restart()
    {
        ViewModel = new MainViewModel(_options, new HttpClient(Server), Confirmation, Files);
        ViewModel.Initialize();
    }

    public string LogText => string.Join("\n", ViewModel.Log.Select(l => $"{l.Level} {l.Message}"));

    public IEnumerable<(HttpMethod Method, string Url, string? Authorization, string Body)> Writes =>
        Server.Requests.Where(r => r.Method != HttpMethod.Get);

    public void Dispose() => _temp.Dispose();
}

internal sealed class FakeConfirmation : IConfirmationService
{
    public bool Answer { get; set; } = true;

    public List<OperationPlan> Plans { get; } = [];

    public Task<bool> ConfirmAsync(OperationPlan plan)
    {
        Plans.Add(plan);
        return Task.FromResult(Answer);
    }
}

internal sealed class FakeFiles : IFileDialogService
{
    public string? NextOpen { get; set; }

    public string? NextSave { get; set; }

    public string? OpenFile(string title, string filter, string? initialDirectory) => NextOpen;

    public string? SaveFile(string title, string filter, string defaultFileName) => NextSave;
}
