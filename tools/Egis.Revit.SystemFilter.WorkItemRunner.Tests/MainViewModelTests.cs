using System.Text.Json.Nodes;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class MainViewModelTests
{
    private static void AssertNoSecrets(TestEnvironment env)
    {
        var log = env.LogText + string.Join("\n", env.Confirmation.Plans.Select(p => p.Text));
        Assert.DoesNotContain("Signature", log, StringComparison.Ordinal);
        Assert.DoesNotContain("-sig", log, StringComparison.Ordinal);
        Assert.DoesNotContain(TestEnvironment.Token, log, StringComparison.Ordinal);
        var files = Directory.Exists(env.DataDirectory) ? Directory.EnumerateFiles(env.DataDirectory, "*", SearchOption.AllDirectories) : [];
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Signature", text, StringComparison.Ordinal);
            Assert.DoesNotContain(TestEnvironment.Token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Zipped_result_is_extracted_with_its_images()
    {
        using var env = new TestEnvironment();
        env.Server.ResultImages.Add(("uid_code_1_AdjustedCornerView_1.jpg", [1, 2, 3]));

        await env.ViewModel.RunCommand.ExecuteAsync();

        var run = Assert.Single(new RunHistory(env.DataDirectory).Load());
        var directory = Path.Combine(env.DataDirectory, "runs", run.RunId);
        Assert.True(File.Exists(Path.Combine(directory, "result", "result.json")));
        Assert.True(File.Exists(Path.Combine(directory, "result", "Images", "uid_code_1_AdjustedCornerView_1.jpg")));
        Assert.NotNull(env.ViewModel.Result);
        Assert.True(env.ViewModel.Result!.IsSchemaValid);
        Assert.Contains("1 image(s)", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declined_plan_writes_nothing()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = false;

        await env.ViewModel.RunCommand.ExecuteAsync();

        var plan = Assert.Single(env.Confirmation.Plans);
        Assert.False(plan.IsDestructive);
        Assert.Contains("nick.SystemFilter_R2026+test (version v3)", plan.Text, StringComparison.Ordinal);
        Assert.Contains("/inputZip.zip", plan.Text, StringComparison.Ordinal);
        Assert.Contains("/settings.json", plan.Text, StringComparison.Ordinal);
        Assert.Contains("/result.zip", plan.Text, StringComparison.Ordinal);
        Assert.Contains("60 min", plan.Text, StringComparison.Ordinal);
        Assert.Empty(env.Writes);
        Assert.Empty(env.Server.Buckets);
        Assert.False(Directory.Exists(env.DataDirectory) && Directory.EnumerateFileSystemEntries(env.DataDirectory).Any());
        Assert.Contains("aucun WorkItem soumis", env.LogText, StringComparison.Ordinal);
        AssertNoSecrets(env);
    }

    [Fact]
    public async Task Confirmed_run_uploads_submits_and_collects_a_valid_result()
    {
        using var env = new TestEnvironment();

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.DoesNotContain("error", env.LogText, StringComparison.Ordinal);
        Assert.Contains(TestEnvironment.Bucket, env.Server.Buckets);
        var submitted = env.Server.SubmittedWorkItem!;
        Assert.Equal("nick.SystemFilter_R2026+test", submitted["activityId"]!.GetValue<string>());
        var arguments = submitted["arguments"]!.AsObject();
        Assert.Equal(["inputZip", "settings", "result"], arguments.Select(a => a.Key));
        Assert.Equal("put", arguments["result"]!["verb"]!.GetValue<string>());
        Assert.Contains("download-sig", arguments["inputZip"]!["url"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("upload-sig", arguments["result"]!["url"]!.GetValue<string>(), StringComparison.Ordinal);

        var runId = env.Confirmation.Plans.Single().Text.Split('(', ')').First(s => s.StartsWith("2026", StringComparison.Ordinal) && s.Contains('-', StringComparison.Ordinal));
        Assert.Equal(env.ViewModel.SettingsText, System.Text.Encoding.UTF8.GetString(env.Server.Objects[$"{TestEnvironment.Bucket}/{runId}/settings.json"]));
        Assert.True(env.Server.Objects.ContainsKey($"{TestEnvironment.Bucket}/{runId}/inputZip.zip"));

        // Pas de jeton envoyé à S3 ni au rapport (URL présignées).
        Assert.All(env.Server.Requests.Where(r => r.Url.StartsWith("https://s3.test/", StringComparison.Ordinal) || r.Url == FakeAps.ReportUrl), r => Assert.Null(r.Authorization));

        Assert.Equal("success", env.ViewModel.WorkItemStatus);
        var run = Assert.Single(env.ViewModel.History);
        Assert.Same(run, env.ViewModel.SelectedRun);
        Assert.True(run is { WorkItemId: "wi-1", WorkItemStatus: "success", ResultStatus: "succeededWithWarnings", SchemaErrorCount: 0, ReportDownloaded: true });
        Assert.True(run.Outputs["result.json"]);
        Assert.NotNull(env.ViewModel.Result);
        Assert.Equal(3, env.ViewModel.Result!.Systems.Count);
        Assert.Contains("Job finished", env.ViewModel.ReportText, StringComparison.Ordinal);
        Assert.Contains("Relevé : WorkItem success, result.json présent (status succeededWithWarnings)", env.LogText, StringComparison.Ordinal);

        var directory = Path.Combine(env.DataDirectory, "runs", run.RunId);
        Assert.True(File.Exists(Path.Combine(directory, "result", "result.json")));
        Assert.True(File.Exists(Path.Combine(directory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(directory, RunHistory.ReportFileName)));
        AssertNoSecrets(env);

        env.Files.NextSave = Path.Combine(env.Root, "export.zip");
        await env.ViewModel.ExportRunCommand.ExecuteAsync();
        Assert.True(File.Exists(env.Files.NextSave));
    }

    [Fact]
    public async Task Failed_workitem_without_result_is_recorded_for_D6()
    {
        using var env = new TestEnvironment();
        env.Server.Statuses.Clear();
        env.Server.Statuses.Add("failedInstructions");
        env.Server.ResultJson = null;
        env.ViewModel.SettingsText = """{ "schemaVersion": "2.0" }""";

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.Contains(env.Confirmation.Plans.Single().Steps, s => s.StartsWith("ATTENTION", StringComparison.Ordinal));
        var run = Assert.Single(env.ViewModel.History);
        Assert.Equal("failedInstructions", run.WorkItemStatus);
        Assert.False(run.Outputs["result.json"]);
        Assert.Null(run.ResultStatus);
        Assert.True(run.ReportDownloaded);
        Assert.Null(env.ViewModel.Result);
        Assert.Contains("Relevé : WorkItem failedInstructions, result.json absent.", env.LogText, StringComparison.Ordinal);
        AssertNoSecrets(env);
    }

    [Fact]
    public async Task Invalid_result_is_kept_and_flagged()
    {
        using var env = new TestEnvironment();
        var json = JsonNode.Parse(env.ExampleResult)!.AsObject();
        json.Remove("systems");
        env.Server.ResultJson = json.ToJsonString();

        await env.ViewModel.RunCommand.ExecuteAsync();

        var run = Assert.Single(env.ViewModel.History);
        Assert.True(run.SchemaErrorCount > 0);
        Assert.False(env.ViewModel.Result!.IsSchemaValid);
        Assert.Contains("non conforme au schéma", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stop_following_leaves_the_workitem_running()
    {
        using var env = new TestEnvironment();
        env.Server.Statuses.Clear();
        env.Server.Statuses.Add("inprogress");
        env.Server.OnPoll = async poll =>
        {
            if (poll == 2)
            {
                await env.ViewModel.StopFollowingCommand.ExecuteAsync();
            }
        };

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.False(env.Server.Cancelled);
        Assert.Contains("Suivi arrêté", env.LogText, StringComparison.Ordinal);
        var run = Assert.Single(env.ViewModel.History);
        Assert.Equal("wi-1", run.WorkItemId);
        Assert.False(env.ViewModel.IsBusy);
    }

    [Fact]
    public async Task Cancel_workitem_requires_destructive_confirmation()
    {
        using var env = new TestEnvironment();
        env.Server.Statuses.Clear();
        env.Server.Statuses.Add("inprogress");
        env.Server.OnPoll = async poll =>
        {
            if (poll == 1)
            {
                env.Confirmation.Answer = false;
                await env.ViewModel.CancelWorkItemCommand.ExecuteAsync();
                Assert.False(env.Server.Cancelled);
                env.Confirmation.Answer = true;
                await env.ViewModel.CancelWorkItemCommand.ExecuteAsync();
            }
        };

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.True(env.Server.Cancelled);
        Assert.Equal(2, env.Confirmation.Plans.Count(p => p.IsDestructive));
        Assert.Contains(env.Server.Requests, r => r.Method == HttpMethod.Delete && r.Url.EndsWith("/workitems/wi-1", StringComparison.Ordinal));
        Assert.Equal("cancelled", env.ViewModel.WorkItemStatus);
        Assert.False(Assert.Single(env.ViewModel.History).Outputs["result.json"]);
    }

    [Fact]
    public async Task Missing_alias_stops_before_any_write()
    {
        using var env = new TestEnvironment();
        env.ViewModel.SelectedAlias = "prod";
        env.Server.ActivityAliases["SystemFilter_R2026"].Remove("prod");

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Writes);
        Assert.Contains("SystemFilter_R2026+prod introuvable", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_zip_stops_before_any_call()
    {
        using var env = new TestEnvironment();
        env.ViewModel.ZipPath = env.WriteZip("no-model.zip", ("readme.txt", "x"));

        await env.ViewModel.RunCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Requests);
        Assert.Contains("ZIP refusé", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_editor_validates_live()
    {
        using var env = new TestEnvironment();

        env.ViewModel.SettingsText = "{";
        var invalid = env.ViewModel.SettingsValidation;
        env.ViewModel.SettingsText = File.ReadAllText(Path.Combine(env.Root, "docs", "examples", "settings.example.json"));

        Assert.NotEqual(invalid, env.ViewModel.SettingsValidation);
    }
}
