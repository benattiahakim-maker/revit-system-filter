using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class RunTests
{
    [Fact]
    public async Task Full_run_uploads_model_submits_and_shows_inventory()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.NotNull(env.Server.SubmittedWorkItem);
        Assert.Equal("nick.SystemsInventory_R2026+test", env.Server.SubmittedWorkItem!["activityId"]!.GetValue<string>());
        Assert.Contains(env.Server.Objects.Keys, k => k.EndsWith("/input.rvt", StringComparison.Ordinal));

        Assert.Equal("success", env.ViewModel.WorkItemStatus);
        Assert.NotNull(env.ViewModel.Result);
        Assert.True(env.ViewModel.Result!.IsValid);
        Assert.Equal(2, env.ViewModel.Result.Tree.Count);
        Assert.Single(env.ViewModel.History);
    }

    [Fact]
    public async Task Declined_plan_writes_nothing()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = false;

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.Null(env.Server.SubmittedWorkItem);
        Assert.Empty(env.Writes);
        Assert.Empty(env.ViewModel.History);
    }

    [Fact]
    public async Task Options_are_uploaded_only_when_provided()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;
        env.ViewModel.OptionsText = "{ \"includePipingSystems\": false }";

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.Contains(env.Server.Objects.Keys, k => k.EndsWith("/options.json", StringComparison.Ordinal));
        Assert.Contains("options", env.Server.SubmittedWorkItem!["arguments"]!.AsObject().Select(a => a.Key));
    }

    [Fact]
    public async Task Options_are_skipped_when_empty()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;
        env.ViewModel.OptionsText = string.Empty;

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.DoesNotContain(env.Server.Objects.Keys, k => k.EndsWith("/options.json", StringComparison.Ordinal));
        Assert.DoesNotContain("options", env.Server.SubmittedWorkItem!["arguments"]!.AsObject().Select(a => a.Key));
    }

    [Fact]
    public async Task No_signed_url_appears_in_the_log()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.DoesNotContain("X-Amz-Signature", env.LogText, StringComparison.Ordinal);
    }
}
