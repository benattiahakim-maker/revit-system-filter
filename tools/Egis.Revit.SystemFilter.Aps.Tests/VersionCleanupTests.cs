using System.Net;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.Aps.Tests;

public sealed class VersionCleanupTests
{
    private static readonly Dictionary<string, int> Aliases = new() { ["test"] = 9, ["prod"] = 3 };

    [Fact]
    public void Keeps_latest_and_aliased_versions_and_deletes_the_rest()
    {
        var plan = VersionCleanupPlanner.Plan(DaResourceKind.AppBundle, "Egis.Revit.SystemFilter_R2026", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], Aliases, keepLatest: 2);

        Assert.Equal([1, 2, 4, 5, 6, 7, 8], plan.ToDelete);
        Assert.Equal([3, 9, 10], plan.Kept.Select(k => k.Version));
        Assert.Equal(KeepReason.Aliased, plan.Kept[0].Reasons);
        Assert.Equal(["prod"], plan.Kept[0].Aliases);
        Assert.Equal(KeepReason.Aliased | KeepReason.Latest, plan.Kept[1].Reasons);
        Assert.Equal(KeepReason.Latest, plan.Kept[2].Reasons);
    }

    [Fact]
    public void Aliased_versions_are_never_deleted_whatever_keepLatest()
    {
        var aliases = new Dictionary<string, int> { ["test"] = 1, ["prod"] = 1, ["old"] = 2 };

        var plan = VersionCleanupPlanner.Plan(DaResourceKind.Activity, "ExtractRooms_R2026", Enumerable.Range(1, 100), aliases, keepLatest: 1);

        Assert.DoesNotContain(1, plan.ToDelete);
        Assert.DoesNotContain(2, plan.ToDelete);
        Assert.DoesNotContain(100, plan.ToDelete);
        Assert.Equal(97, plan.ToDelete.Count);
        Assert.Equal(["old"], plan.Kept.Single(k => k.Version == 2).Aliases);
        Assert.Equal(["prod", "test"], plan.Kept.Single(k => k.Version == 1).Aliases);
    }

    [Fact]
    public void Protected_versions_from_the_journal_are_kept()
    {
        var plan = VersionCleanupPlanner.Plan(DaResourceKind.AppBundle, "B", [1, 2, 3, 4], new Dictionary<string, int>(), keepLatest: 1, protectedVersions: [2]);

        Assert.Equal([1, 3], plan.ToDelete);
        Assert.Equal(KeepReason.Protected, plan.Kept.Single(k => k.Version == 2).Reasons);
    }

    [Fact]
    public void Nothing_to_delete_when_few_versions()
    {
        var plan = VersionCleanupPlanner.Plan(DaResourceKind.AppBundle, "B", [2, 1], Aliases, keepLatest: 5);

        Assert.True(plan.IsEmpty);
        Assert.Equal([1, 2], plan.Kept.Select(k => k.Version));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void KeepLatest_must_be_at_least_one(int keep) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => VersionCleanupPlanner.Plan(DaResourceKind.AppBundle, "B", [1], Aliases, keep));

    [Fact]
    public void Description_lists_every_version_to_delete_with_its_request()
    {
        var plan = VersionCleanupPlanner.Plan(DaResourceKind.Activity, "ExtractRooms_R2026", [1, 2, 3, 9, 10], Aliases, keepLatest: 1);

        var text = string.Join("\n", plan.Describe());

        Assert.Contains("activities/ExtractRooms_R2026 : 5 version(s), 3 conservée(s), 2 à supprimer.", text);
        Assert.Contains("conserver v3 (alias prod)", text);
        Assert.Contains("SUPPRIMER v1 : DELETE /activities/ExtractRooms_R2026/versions/1", text);
        Assert.Contains("SUPPRIMER v2 : DELETE /activities/ExtractRooms_R2026/versions/2", text);
    }

    [Fact]
    public async Task Plan_reads_versions_and_aliases_then_execute_deletes_after_recheck()
    {
        var (handler, http) = Support.Http();
        const string aliasesJson = """{"data":[{"id":"test","version":4},{"id":"prod","version":2}]}""";
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/B/aliases", HttpStatusCode.OK, aliasesJson);
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/B/versions", HttpStatusCode.OK, """{"data":[1,2,3],"paginationToken":"n"}""");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/B/versions?page=n", HttpStatusCode.OK, """{"data":[4,5]}""");
        handler.Expect(HttpMethod.Get, $"{Support.Da}/appbundles/B/aliases", HttpStatusCode.OK, aliasesJson);
        handler.Expect(HttpMethod.Delete, $"{Support.Da}/appbundles/B/versions/1", HttpStatusCode.NoContent);
        handler.Expect(HttpMethod.Delete, $"{Support.Da}/appbundles/B/versions/3", HttpStatusCode.NoContent);
        var log = new List<string>();
        var service = new VersionCleanupService(DesignAutomationClientTests.Client(http), log.Add);

        var plan = await service.PlanAsync(DaResourceKind.AppBundle, "B", keepLatest: 1);
        var deleted = await service.ExecuteAsync(plan);

        Assert.Equal([1, 3], plan.ToDelete);
        Assert.Equal([1, 3], deleted);
        Assert.Equal(0, handler.Pending);
        Assert.All(handler.Requests, r => Assert.Equal($"Bearer {Support.Token}", r.Authorization));
        Assert.Equal(["B : v1 supprimée", "B : v3 supprimée"], log);
    }

    [Fact]
    public async Task Execute_refuses_when_a_planned_version_became_aliased()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/activities/A/aliases", HttpStatusCode.OK, """{"data":[{"id":"prod","version":1}]}""");
        var plan = VersionCleanupPlanner.Plan(DaResourceKind.Activity, "A", [1, 2, 3], new Dictionary<string, int>(), keepLatest: 1);

        var error = await Assert.ThrowsAsync<ApsException>(() => new VersionCleanupService(DesignAutomationClientTests.Client(http)).ExecuteAsync(plan));

        Assert.Contains("v1 référencée(s) par un alias", error.Message);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Planning_a_missing_resource_fails()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/activities/A/aliases", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<ApsException>(() => new VersionCleanupService(DesignAutomationClientTests.Client(http)).PlanAsync(DaResourceKind.Activity, "A", 5));
    }
}
