using System.Net;
using System.Text.Json.Nodes;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;

namespace Egis.Revit.SystemFilter.Aps.Tests;

public sealed class WorkItemTests
{
    private const string ReportUrl = "https://dasprod.test/reports/wi1.txt?X-Amz-Signature=secret";

    [Fact]
    public async Task Submit_posts_activity_and_arguments()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Post, $"{Support.Da}/workitems", HttpStatusCode.OK, """{"id":"wi1","status":"pending"}""");
        var workItem = new JsonObject
        {
            ["activityId"] = "nick.ExtractRooms_R2026+test",
            ["arguments"] = new JsonObject { ["settings"] = new JsonObject { ["url"] = "https://s3.test/settings", ["verb"] = "get" } },
        };

        var status = await DesignAutomationClientTests.Client(http).SubmitWorkItemAsync(workItem);

        Assert.Equal(new WorkItemStatus("wi1", "pending", null, null), status);
        Assert.False(status.IsCompleted);
        Assert.True(JsonNode.DeepEquals(workItem, JsonNode.Parse(handler.Requests[0].BodyText)));
    }

    [Fact]
    public async Task Polling_reports_each_status_until_terminal()
    {
        var (handler, http) = Support.Http();
        var url = $"{Support.Da}/workitems/wi1";
        handler.Expect(HttpMethod.Get, url, HttpStatusCode.OK, """{"id":"wi1","status":"pending"}""");
        handler.Expect(HttpMethod.Get, url, HttpStatusCode.OK, """{"id":"wi1","status":"inprogress"}""");
        handler.Expect(HttpMethod.Get, url, HttpStatusCode.OK, $$$"""{"id":"wi1","status":"success","reportUrl":"{{{ReportUrl}}}","stats":{"timeQueued":"2026-10-09T13:00:00Z"}}""");
        var seen = new List<string>();

        var final = await DesignAutomationClientTests.Client(http).WaitForWorkItemAsync("wi1", TimeSpan.Zero, progress: new SyncProgress(s => seen.Add(s.Status)));

        Assert.Equal(["pending", "inprogress", "success"], seen);
        Assert.True(final.IsSuccess);
        Assert.Equal("2026-10-09T13:00:00Z", final.Stats!["timeQueued"]!.GetValue<string>());
        Assert.DoesNotContain("secret", final.ToString());
    }

    [Theory]
    [InlineData("failedInstructions")]
    [InlineData("failedUpload")]
    [InlineData("cancelled")]
    public async Task Failure_statuses_end_polling(string terminal)
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/workitems/wi1", HttpStatusCode.OK, $$"""{"id":"wi1","status":"{{terminal}}"}""");

        var final = await DesignAutomationClientTests.Client(http).WaitForWorkItemAsync("wi1", TimeSpan.Zero);

        Assert.True(final.IsCompleted);
        Assert.False(final.IsSuccess);
    }

    [Fact]
    public async Task Polling_honours_cancellation()
    {
        var (handler, http) = Support.Http();
        for (var i = 0; i < 1000; i++)
        {
            handler.Expect(HttpMethod.Get, $"{Support.Da}/workitems/wi1", HttpStatusCode.OK, """{"id":"wi1","status":"inprogress"}""");
        }

        using var cancellation = new CancellationTokenSource();
        var polls = 0;
        var progress = new SyncProgress(_ =>
        {
            if (++polls == 3)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DesignAutomationClientTests.Client(http).WaitForWorkItemAsync("wi1", TimeSpan.FromMilliseconds(1), progress: progress, cancellationToken: cancellation.Token));
        Assert.Equal(3, polls);
    }

    [Fact]
    public async Task Report_is_downloaded_without_bearer_token()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, ReportUrl, HttpStatusCode.OK, "[10/09/2026 13:00:00] Job finished");

        var report = await DesignAutomationClientTests.Client(http).DownloadReportAsync(new WorkItemStatus("wi1", "success", ReportUrl, null));

        Assert.Contains("Job finished", report);
        Assert.Null(handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task Report_failure_does_not_reveal_signed_url()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, ReportUrl, HttpStatusCode.Forbidden);

        var error = await Assert.ThrowsAsync<ApsException>(() =>
            DesignAutomationClientTests.Client(http).DownloadReportAsync(new WorkItemStatus("wi1", "success", ReportUrl, null)));

        Assert.DoesNotContain("dasprod.test", error.Message);
    }

    /// <summary>IProgress synchrone (Progress&lt;T&gt; poste sur le contexte et rendrait l'ordre non déterministe).</summary>
    private sealed class SyncProgress(Action<WorkItemStatus> report) : IProgress<WorkItemStatus>
    {
        public void Report(WorkItemStatus value) => report(value);
    }
}
