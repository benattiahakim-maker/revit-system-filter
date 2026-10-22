namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class RecoveryAndStartupTests
{
    [Fact]
    public async Task Selecting_a_run_after_restart_shows_the_local_inventory()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;
        await env.ViewModel.RunCommand.ExecuteAsync();

        env.Restart();

        Assert.Single(env.ViewModel.History);
        env.ViewModel.SelectedRun = env.ViewModel.History.First();
        Assert.NotNull(env.ViewModel.Result);
        Assert.True(env.ViewModel.Result!.IsValid);
        Assert.Equal(2, env.ViewModel.Result.Tree.Count);
    }

    [Fact]
    public async Task Recover_refetches_status_and_output_after_restart()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;
        await env.ViewModel.RunCommand.ExecuteAsync();

        env.Restart();
        env.ViewModel.SelectedRun = env.ViewModel.History.First();
        await env.ViewModel.RecoverCommand.ExecuteAsync();

        Assert.Equal("success", env.ViewModel.WorkItemStatus);
        Assert.NotNull(env.ViewModel.Result);
    }

    [Fact]
    public void Missing_credentials_raise_the_configuration_banner()
    {
        using var env = new TestEnvironment(withCredentials: false);
        env.ViewModel.Initialize();

        Assert.True(env.ViewModel.HasConfigurationBanner);
        Assert.NotEmpty(env.ViewModel.MissingVariables);
    }

    [Fact]
    public void Complete_configuration_shows_no_banner()
    {
        using var env = new TestEnvironment();

        Assert.False(env.ViewModel.HasConfigurationBanner);
        Assert.Empty(env.ViewModel.MissingVariables);
    }
}
