using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class RecoveryTests
{
    private static async Task<TestEnvironment> StoppedRunAsync()
    {
        var env = new TestEnvironment();
        env.Server.Statuses.Clear();
        env.Server.Statuses.Add("inprogress");
        env.Server.OnPoll = async poll =>
        {
            if (poll == 1)
            {
                await env.ViewModel.StopFollowingCommand.ExecuteAsync();
            }
        };
        await env.ViewModel.RunCommand.ExecuteAsync();
        env.Server.OnPoll = null;
        return env;
    }

    private static void AssertNothingSecretOnDisk(TestEnvironment env)
    {
        foreach (var file in Directory.EnumerateFiles(env.DataDirectory, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("https://", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Signature", text, StringComparison.Ordinal);
            Assert.DoesNotContain("-sig", text, StringComparison.Ordinal);
            Assert.DoesNotContain(TestEnvironment.Token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task History_keeps_object_keys_but_no_signed_url_or_token()
    {
        using var env = await StoppedRunAsync();

        var run = Assert.Single(new RunHistory(env.DataDirectory).Load());
        Assert.Equal("wi-1", run.WorkItemId);
        Assert.Equal(TestEnvironment.Bucket, run.BucketKey);
        Assert.Equal("test", run.Alias);
        Assert.Equal("nick.SystemFilter_R2026+test", run.ActivityId);
        Assert.Equal(
            [$"{run.RunId}/inputZip.zip", $"{run.RunId}/settings.json", $"{run.RunId}/result.zip"],
            run.Objects.Select(o => o.ObjectKey));
        Assert.All(run.Objects.Where(o => o.IsInput), o => Assert.Null(o.UploadKey));
        Assert.NotNull(run.Objects.Single(o => !o.IsInput).UploadKey);
        AssertNothingSecretOnDisk(env);
    }

    [Fact]
    public async Task Recover_after_restart_downloads_result_and_report()
    {
        using var env = await StoppedRunAsync();
        env.Server.Statuses.Clear();
        env.Server.Statuses.Add("success");

        env.Restart();
        Assert.Empty(env.ViewModel.Log);
        env.ViewModel.SelectedRun = Assert.Single(env.ViewModel.History);
        Assert.True(env.ViewModel.RecoverCommand.CanExecute(null));
        var signedDownloadsBefore = env.Server.Requests.Count(r => r.Url.Contains("/signeds3download", StringComparison.Ordinal));

        await env.ViewModel.RecoverCommand.ExecuteAsync();

        Assert.DoesNotContain("error", env.LogText, StringComparison.Ordinal);
        var run = Assert.Single(env.ViewModel.History);
        Assert.Same(run, env.ViewModel.SelectedRun);
        Assert.True(run is { WorkItemStatus: "success", ResultStatus: "succeededWithWarnings", SchemaErrorCount: 0, ReportDownloaded: true });
        Assert.True(run.Outputs["result.json"]);
        Assert.Equal(3, env.ViewModel.Result!.Systems.Count);
        Assert.Contains("Job finished", env.ViewModel.ReportText, StringComparison.Ordinal);
        Assert.Equal("success", env.ViewModel.WorkItemStatus);
        Assert.Contains("Relevé : WorkItem success, result.json présent", env.LogText, StringComparison.Ordinal);

        // URL signée de lecture redemandée à APS, sans jeton envoyé à S3 ni au rapport.
        Assert.True(env.Server.Requests.Count(r => r.Url.Contains("/signeds3download", StringComparison.Ordinal)) > signedDownloadsBefore);
        Assert.All(env.Server.Requests.Where(r => r.Url.StartsWith("https://s3.test/", StringComparison.Ordinal) || r.Url == FakeAps.ReportUrl), r => Assert.Null(r.Authorization));
        Assert.DoesNotContain("Signature", env.LogText, StringComparison.Ordinal);
        AssertNothingSecretOnDisk(env);
    }

    [Fact]
    public async Task Recover_while_running_only_updates_the_status()
    {
        using var env = await StoppedRunAsync();
        env.Restart();
        env.ViewModel.SelectedRun = env.ViewModel.History.Single();
        var writes = env.Writes.Count();

        await env.ViewModel.RecoverCommand.ExecuteAsync();

        Assert.Contains("toujours inprogress", env.LogText, StringComparison.Ordinal);
        Assert.Equal(writes, env.Writes.Count());
        var run = env.ViewModel.History.Single();
        Assert.Equal("inprogress", run.WorkItemStatus);
        Assert.Empty(run.Outputs);
        Assert.Null(env.ViewModel.Result);
    }

    [Fact]
    public async Task Recover_can_be_repeated_after_outputs_are_finalized()
    {
        using var env = new TestEnvironment();
        await env.ViewModel.RunCommand.ExecuteAsync();
        File.Delete(Path.Combine(env.DataDirectory, "runs", env.ViewModel.History.Single().RunId, "result", "result.json"));

        env.Restart();
        env.ViewModel.SelectedRun = env.ViewModel.History.Single();
        await env.ViewModel.RecoverCommand.ExecuteAsync();

        var run = env.ViewModel.History.Single();
        Assert.True(run.Outputs["result.json"]);
        Assert.Equal("succeededWithWarnings", run.ResultStatus);
        Assert.NotNull(env.ViewModel.Result);
    }

    [Fact]
    public async Task Failed_workitem_without_output_is_recovered_as_absent()
    {
        using var env = await StoppedRunAsync();
        env.Server.Statuses.Clear();
        env.Server.Statuses.Add("failedInstructions");
        env.Server.ResultJson = null;
        env.Restart();
        env.ViewModel.SelectedRun = env.ViewModel.History.Single();

        await env.ViewModel.RecoverCommand.ExecuteAsync();

        var run = env.ViewModel.History.Single();
        Assert.Equal("failedInstructions", run.WorkItemStatus);
        Assert.False(run.Outputs["result.json"]);
        Assert.Contains("Relevé : WorkItem failedInstructions, result.json absent.", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_without_object_keys_cannot_be_recovered()
    {
        using var env = new TestEnvironment();
        var history = new RunHistory(env.DataDirectory);
        history.Save(new RunRecord
        {
            RunId = "ancien",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RevitVersion = "2026",
            Alias = "test",
            ActivityId = "nick.SystemFilter_R2026+test",
            ZipFileName = "model.zip",
            BucketKey = TestEnvironment.Bucket,
            WorkItemId = "wi-1",
        });
        env.Restart();
        env.ViewModel.SelectedRun = env.ViewModel.History.Single();

        await env.ViewModel.RecoverCommand.ExecuteAsync();

        Assert.Contains("clés d'objets OSS absentes", env.LogText, StringComparison.Ordinal);
        Assert.Empty(env.Server.Requests);
    }

    [Fact]
    public void Recover_requires_a_submitted_run()
    {
        using var env = new TestEnvironment();

        Assert.False(env.ViewModel.RecoverCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz012345")]
    [InlineData("  spaced-id  ")]
    [InlineData("x")]
    public void Default_bucket_is_derived_from_the_client_id(string clientId)
    {
        var key = RunnerOptions.DefaultBucketKey(clientId, "nick")!;

        Assert.True(RunnerOptions.IsValidBucketKey(key), key);
        Assert.Matches("^egr-workitems-[0-9a-f]{20}$", key);
        Assert.Equal(key, RunnerOptions.DefaultBucketKey(clientId, "autre")); // déterministe, indépendant du nickname
        Assert.DoesNotContain(clientId.Trim().ToLowerInvariant(), key, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_bucket_differs_between_client_ids_and_falls_back_to_nickname()
    {
        Assert.NotEqual(RunnerOptions.DefaultBucketKey("client-a"), RunnerOptions.DefaultBucketKey("client-b"));
        Assert.Equal("egr-workitems-mon-nick", RunnerOptions.DefaultBucketKey(null, "Mon Nick"));
        Assert.Equal("egr-workitems-" + new string('a', 114), RunnerOptions.DefaultBucketKey(null, new string('A', 200)));
        Assert.Null(RunnerOptions.DefaultBucketKey(" ", null));
    }

    [Fact]
    public void Options_prefer_aps_bucket_then_client_id()
    {
        using var env = new TestEnvironment();
        var withBucket = RunnerOptions.FromEnvironment(new Dictionary<string, string?> { ["APS_BUCKET"] = "mon-bucket", ["APS_CLIENT_ID"] = "id", ["EGR_RUNNER_DATA"] = env.DataDirectory });
        var withClient = RunnerOptions.FromEnvironment(new Dictionary<string, string?> { ["APS_CLIENT_ID"] = "id", ["APS_NICKNAME"] = "nick", ["EGR_RUNNER_DATA"] = env.DataDirectory });

        Assert.Equal("mon-bucket", withBucket.BucketKey);
        Assert.Equal(RunnerOptions.DefaultBucketKey("id"), withClient.BucketKey);
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("abc", true)]
    [InlineData("Abc", false)]
    [InlineData("a_b.c-1", true)]
    [InlineData("a b", false)]
    public void Bucket_key_rule_matches_oss(string key, bool valid) =>
        Assert.Equal(valid, RunnerOptions.IsValidBucketKey(key));

    [Fact]
    public async Task Invalid_bucket_stops_before_any_write()
    {
        using var env = new TestEnvironment();
        env.ViewModel.BucketKey = "Bucket Invalide";

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Requests);
        Assert.Contains("Bucket « Bucket Invalide » invalide", env.LogText, StringComparison.Ordinal);
    }
}
