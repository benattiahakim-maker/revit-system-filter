using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;
using Egis.Revit.SystemFilter.WorkItemRunner.ViewModels;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

/// <summary>Démarrage sans configuration : fenêtre utilisable, historique local, aucun client APS.</summary>
public sealed class StartupTests
{
    /// <summary>Aucune variable : ni identifiants, ni nickname, ni dépôt, ni bucket ; seul le dossier de données existe.</summary>
    private static (MainViewModel ViewModel, FakeAps Server) Bare(string dataDirectory)
    {
        var server = new FakeAps();
        var options = new RunnerOptions
        {
            Aps = new ApsSettings { DaBaseUrl = FakeAps.Da, OssBaseUrl = FakeAps.Oss },
            RepositoryRoot = null,
            BucketKey = null,
            DataDirectory = dataDirectory,
        };
        return (new MainViewModel(options, new HttpClient(server), new FakeConfirmation { Answer = true }, new FakeFiles()), server);
    }

    private static string LogText(MainViewModel viewModel) => string.Join("\n", viewModel.Log.Select(l => $"{l.Level} {l.Message}"));

    private static async Task<TestEnvironment> WithOneRunAsync()
    {
        var env = new TestEnvironment();
        env.Confirmation.Answer = true;
        await env.ViewModel.RunCommand.ExecuteAsync();
        Assert.Single(env.ViewModel.History);
        return env;
    }

    [Fact]
    public void Options_without_any_variable_do_not_throw()
    {
        var options = RunnerOptions.FromEnvironment(new Dictionary<string, string?>(), Path.GetTempPath());

        Assert.False(options.Aps.HasCredentials);
        Assert.Null(options.BucketKey);
    }

    [Fact]
    public async Task Initialize_without_variables_loads_history_without_any_aps_client()
    {
        using var env = await WithOneRunAsync();
        var (viewModel, server) = Bare(env.DataDirectory);

        viewModel.Initialize();

        var run = Assert.Single(viewModel.History);
        Assert.False(viewModel.HasApsClient);
        Assert.Empty(server.Requests);
        Assert.Equal(["APS_CLIENT_ID", "APS_CLIENT_SECRET (ou APS_TOKEN)", "APS_NICKNAME", "EGR_REPO_ROOT", "APS_BUCKET"], viewModel.MissingVariables);
        Assert.True(viewModel.HasConfigurationBanner);
        Assert.Contains("Variables manquantes : APS_CLIENT_ID, APS_CLIENT_SECRET (ou APS_TOKEN), APS_NICKNAME, EGR_REPO_ROOT, APS_BUCKET.", viewModel.ConfigurationBanner);
        Assert.Contains("historique local reste consultable", viewModel.ConfigurationBanner);
        Assert.Contains("warning Configuration incomplète.", LogText(viewModel));
        Assert.StartsWith("aucun identifiant", viewModel.CredentialSource);

        // Lancement consultable sans APS ni dépôt : rapport affiché, result.json non validé faute de schéma.
        viewModel.SelectedRun = run;
        Assert.NotEqual(string.Empty, viewModel.ReportText);
        Assert.Null(viewModel.Result);
        Assert.False(viewModel.HasApsClient);
    }

    [Fact]
    public async Task Aps_actions_fail_gracefully_with_the_missing_credentials_message()
    {
        using var env = await WithOneRunAsync();
        var (viewModel, server) = Bare(env.DataDirectory);
        viewModel.Initialize();
        viewModel.SelectedRun = viewModel.History[0];

        await viewModel.RecoverCommand.ExecuteAsync();
        Assert.Equal("Récupération : échec (voir le journal).", viewModel.Status);
        await viewModel.RunCommand.ExecuteAsync();
        Assert.Equal("Lancement : échec (voir le journal).", viewModel.Status);

        Assert.Contains($"Identifiants APS absents : {ApsSettings.MissingCredentialsMessage}", LogText(viewModel));
        Assert.False(viewModel.HasApsClient);
        Assert.Empty(server.Requests);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Local_actions_work_without_variables()
    {
        using var env = await WithOneRunAsync();
        var (viewModel, _) = Bare(env.DataDirectory);
        var export = Path.Combine(env.DataDirectory, "..", "export.zip");
        viewModel.Initialize();
        viewModel.SelectedRun = viewModel.History[0];

        await viewModel.RefreshHistoryCommand.ExecuteAsync();
        Assert.Equal("Historique : terminé.", viewModel.Status);

        var files = new FakeFiles { NextSave = export };
        var withFiles = new MainViewModel(
            new RunnerOptions { Aps = new ApsSettings(), DataDirectory = env.DataDirectory },
            new HttpClient(new FakeAps()),
            new FakeConfirmation(),
            files);
        withFiles.Initialize();
        withFiles.SelectedRun = withFiles.History[0];
        await withFiles.ExportRunCommand.ExecuteAsync();

        Assert.True(File.Exists(export));
        Assert.False(withFiles.HasApsClient);
    }

    [Fact]
    public void Unreadable_history_is_not_fatal()
    {
        using var temp = new Egis.Revit.SystemFilter.Aps.Tests.TempDirectory();
        var broken = Path.Combine(temp.Path, "runs", "20261009-120000-abcdef");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, RunHistory.RecordFileName), "{ pas du json");
        var (viewModel, _) = Bare(temp.Path);

        viewModel.Initialize();

        Assert.Empty(viewModel.History);
        Assert.False(viewModel.HasApsClient);
    }

    [Fact]
    public void Invalid_data_directory_is_not_fatal()
    {
        var (viewModel, _) = Bare("\0invalide");

        viewModel.Initialize();

        Assert.Empty(viewModel.History); // chemin refusé ou absent selon l'OS : jamais d'exception
        Assert.False(viewModel.HasApsClient);
    }

    [Fact]
    public void Banner_follows_the_fields_and_disappears_when_complete()
    {
        using var env = new TestEnvironment();

        Assert.False(env.ViewModel.HasConfigurationBanner);
        Assert.Empty(env.ViewModel.MissingVariables);

        env.ViewModel.Nickname = string.Empty;
        Assert.Equal(["APS_NICKNAME"], env.ViewModel.MissingVariables);
        Assert.DoesNotContain("impossibles sans identifiants", env.ViewModel.ConfigurationBanner);
        Assert.DoesNotContain(TestEnvironment.Token, env.ViewModel.ConfigurationBanner);

        env.ViewModel.Nickname = "nick";
        Assert.False(env.ViewModel.HasConfigurationBanner);
    }
}
