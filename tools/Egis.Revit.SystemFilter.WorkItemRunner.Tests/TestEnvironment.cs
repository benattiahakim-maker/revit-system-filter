using System.IO.Compression;
using System.Text;
using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;
using Egis.Revit.SystemFilter.WorkItemRunner.ViewModels;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

/// <summary>Faux dépôt (définitions, schéma, exemples réels), ZIP de maquette et ViewModel branché sur <see cref="FakeAps"/>.</summary>
internal sealed class TestEnvironment : IDisposable
{
    public const string Token = "jeton-tres-secret";
    public const string Bucket = "egr-test-bucket";

    private readonly TempDirectory _temp = new();
    private readonly RunnerOptions _options;

    public TestEnvironment(bool withCredentials = true)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Data", "repo"), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "Data", "repo"), file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        File.WriteAllText(Path.Combine(Root, "Egis.Revit.SystemFilter.sln"), string.Empty);
        ZipPath = WriteZip("model.zip", ("Projet/EG_Batiment_A.rvt", "RVT"));
        Server.ActivityAliases["SystemFilter_R2026"] = new() { ["test"] = 3, ["prod"] = 2 };
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
            ZipPath = ZipPath,
            PollIntervalSeconds = 0,
            SelectedRevitVersion = "2026",
            SettingsText = File.ReadAllText(Path.Combine(Root, "docs", "examples", "settings.example.json")),
        };
    }

    public string Root => Path.Combine(_temp.Path, "repo");

    public string DataDirectory => Path.Combine(_temp.Path, "data");

    public string ZipPath { get; }

    public string ExampleResult => File.ReadAllText(Path.Combine(Root, "docs", "examples", "result.example.json"));

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

    public string WriteZip(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_temp.Path, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var stream = archive.CreateEntry(entry).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        return path;
    }

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
