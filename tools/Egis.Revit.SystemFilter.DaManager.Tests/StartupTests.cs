using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.DaManager.Services;
using Egis.Revit.SystemFilter.DaManager.ViewModels;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

/// <summary>Démarrage sans configuration : fenêtre utilisable, journal local, aucun client APS.</summary>
public sealed class StartupTests
{
    private static string LogText(MainViewModel viewModel) => string.Join("\n", viewModel.Log.Select(l => $"{l.Level} {l.Message}"));

    private static (MainViewModel ViewModel, FakeApsServer Server) Bare(string? journalPath, string? repositoryRoot = null)
    {
        var server = new FakeApsServer();
        var options = new DaManagerOptions
        {
            Aps = new ApsSettings { DaBaseUrl = FakeApsServer.BaseUrl },
            RevitVersions = ["2026"],
            RepositoryRoot = repositoryRoot,
            JournalPath = journalPath,
        };
        return (new MainViewModel(options, new HttpClient(server), new FakeConfirmation { Answer = true }, new FakeFiles()), server);
    }

    [Fact]
    public void Options_without_any_variable_do_not_throw()
    {
        var options = DaManagerOptions.FromEnvironment(new Dictionary<string, string?>(), Path.GetTempPath());

        Assert.False(options.Aps.HasCredentials);
        Assert.NotNull(options.JournalPath);
    }

    [Fact]
    public void Startup_without_variables_loads_the_journal_without_any_aps_client()
    {
        using var temp = new Egis.Revit.SystemFilter.Aps.Tests.TempDirectory();
        var journal = Path.Combine(temp.Path, "publication-journal.json");
        new PublicationJournal([new JournalEntry("2026-10-08", null, "2026", "test", new(4, null), new(7, null), null)]).Save(journal);

        var (viewModel, server) = Bare(journal);

        Assert.Single(viewModel.Journal);
        Assert.False(viewModel.HasApsClient);
        Assert.Empty(server.Requests);
        Assert.Equal(["APS_CLIENT_ID", "APS_CLIENT_SECRET (ou APS_TOKEN)", "APS_NICKNAME", "EGSF_REPO_ROOT"], viewModel.MissingVariables);
        Assert.Contains("Variables manquantes : APS_CLIENT_ID, APS_CLIENT_SECRET (ou APS_TOKEN), APS_NICKNAME, EGSF_REPO_ROOT.", viewModel.ConfigurationBanner);
        Assert.Contains("warning Configuration incomplète.", LogText(viewModel));
        Assert.Null(viewModel.Commit);
    }

    [Fact]
    public async Task Aps_actions_fail_gracefully_with_the_missing_credentials_message()
    {
        using var env = new TestEnvironment(withCredentials: false);

        await env.ViewModel.RefreshCommand.ExecuteAsync();
        await env.ViewModel.PublishCommand.ExecuteAsync();
        env.ViewModel.Nickname = "egr_rooms";
        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        Assert.Equal(3, env.ViewModel.Log.Count(l => l.Level == "error" && l.Message.Contains($"Identifiants APS absents : {ApsSettings.MissingCredentialsMessage}", StringComparison.Ordinal)));
        Assert.False(env.ViewModel.HasApsClient);
        Assert.Empty(env.Server.Requests);
        Assert.Equal(["APS_CLIENT_ID", "APS_CLIENT_SECRET (ou APS_TOKEN)"], env.ViewModel.MissingVariables);
    }

    [Theory]
    [InlineData("{ journal corrompu")]
    [InlineData("")]
    public void Unreadable_journal_is_not_fatal(string content)
    {
        using var temp = new Egis.Revit.SystemFilter.Aps.Tests.TempDirectory();
        var journal = Path.Combine(temp.Path, "publication-journal.json");
        File.WriteAllText(journal, content);

        var (viewModel, _) = Bare(journal);

        Assert.Empty(viewModel.Journal);
        Assert.Contains("illisible", viewModel.JournalStatus, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\0invalide.json")]
    [InlineData("C:\\inexistant\\dossier\\journal.json")]
    public void Invalid_journal_path_is_not_fatal(string journal)
    {
        var (viewModel, _) = Bare(journal);

        Assert.Empty(viewModel.Journal);
        Assert.False(viewModel.HasApsClient);
    }

    [Fact]
    public void Invalid_repository_root_is_not_fatal()
    {
        var (viewModel, _) = Bare(null, repositoryRoot: "\0dépôt");

        Assert.Contains("EGSF_REPO_ROOT", viewModel.MissingVariables);
        Assert.Null(viewModel.Commit);
    }

    [Fact]
    public void Complete_configuration_has_no_banner()
    {
        using var env = new TestEnvironment();

        Assert.False(env.ViewModel.HasConfigurationBanner);
        Assert.Equal(string.Empty, env.ViewModel.ConfigurationBanner);
    }
}
