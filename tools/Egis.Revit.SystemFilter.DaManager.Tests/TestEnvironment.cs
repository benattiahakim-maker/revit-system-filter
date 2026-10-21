using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.Packaging;
using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.DaManager.Services;
using Egis.Revit.SystemFilter.DaManager.ViewModels;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

/// <summary>Faux dépôt (Egis.Revit.SystemFilter.sln, définitions réelles, ZIP conformes) et ViewModel branché sur <see cref="FakeApsServer"/>.</summary>
internal sealed class TestEnvironment : IDisposable
{
    public const string Token = "jeton-tres-secret";

    private readonly TempDirectory _temp = new();

    public TestEnvironment(bool withCredentials = true, string? clientId = null)
    {
        Directory.CreateDirectory(Path.Combine(Root, "src", "Packaging"));
        File.WriteAllText(Path.Combine(Root, "Egis.Revit.SystemFilter.sln"), string.Empty);
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Data", "definitions"), Path.Combine(Root, "src", "Packaging", "definitions"));
        Directory.CreateDirectory(BundleDirectory);
        foreach (var version in new[] { "2026", "2027" })
        {
            Support.WriteValidBundle(
                Path.Combine(BundleDirectory, DeploymentService.BundleFileName(version)), version);
        }

        var options = new DaManagerOptions
        {
            Aps = new ApsSettings { Token = withCredentials ? Token : null, ClientId = clientId, Nickname = "nick", DaBaseUrl = FakeApsServer.BaseUrl },
            RevitVersions = ["2026"],
            RepositoryRoot = Root,
            BundleDirectory = BundleDirectory,
            JournalPath = JournalPath,
        };
        ViewModel = new MainViewModel(options, new HttpClient(Server), Confirmation, Files);
    }

    public string Root => _temp.Path;

    public string BundleDirectory => Path.Combine(Root, "artifacts", "appbundles");

    public string JournalPath => Path.Combine(Root, "journal", "publication-journal.json");

    public FakeApsServer Server { get; } = new();

    public FakeFiles Files { get; } = new();

    public FakeConfirmation Confirmation { get; } = new();

    public MainViewModel ViewModel { get; }

    public string LogText => string.Join("\n", ViewModel.Log.Select(l => $"{l.Level} {l.Message}"));

    public void Dispose() => _temp.Dispose();

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}

/// <summary>Confirmation simulée : enregistre chaque plan et répond <see cref="Answer"/>.</summary>
internal sealed class FakeConfirmation : IConfirmationService
{
    public bool Answer { get; set; }

    public List<OperationPlan> Plans { get; } = [];

    public Task<bool> ConfirmAsync(OperationPlan plan)
    {
        Plans.Add(plan);
        return Task.FromResult(Answer);
    }
}

/// <summary>Dialogues de fichiers simulés : renvoient le chemin préparé par le test.</summary>
internal sealed class FakeFiles : IFileDialogService
{
    public string? NextFolder { get; set; }

    public string? NextOpen { get; set; }

    public string? NextSave { get; set; }

    public string? PickFolder(string title, string? initialDirectory) => NextFolder;

    public string? OpenFile(string title, string filter) => NextOpen;

    public string? SaveFile(string title, string filter, string defaultFileName) => NextSave;
}
