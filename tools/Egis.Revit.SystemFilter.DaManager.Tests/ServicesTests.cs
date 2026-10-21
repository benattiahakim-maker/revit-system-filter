using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.DaManager.Services;
using Egis.Revit.SystemFilter.DaManager.ViewModels;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

public sealed class ServicesTests
{
    [Fact]
    public void Options_use_deploy_py_variables()
    {
        using var temp = new TempDirectory();
        var options = DaManagerOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["APS_CLIENT_ID"] = "id",
            ["APS_CLIENT_SECRET"] = "very-secret",
            ["APS_NICKNAME"] = "nick",
            ["REVIT_VERSIONS"] = "2026 2024 2027",
            ["APPBUNDLE_DIR"] = "/tmp/zips",
            ["EGSF_REPO_ROOT"] = temp.Path,
        });

        Assert.Equal(["2026", "2027"], options.RevitVersions);
        Assert.Equal("/tmp/zips", options.BundleDirectory);
        Assert.Equal(temp.Path, options.RepositoryRoot);
        Assert.Equal("nick", options.Aps.Nickname);
        Assert.Contains("OAuth 2-legged", options.CredentialSource);
        Assert.DoesNotContain("very-secret", options.CredentialSource);
    }

    [Fact]
    public void Repository_root_is_found_from_a_subdirectory_and_bundle_dir_defaults_to_artifacts()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "Egis.Revit.SystemFilter.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(temp.Path, "src", "Packaging", "definitions"));
        var start = Directory.CreateDirectory(Path.Combine(temp.Path, "tools", "x", "bin")).FullName;

        var options = DaManagerOptions.FromEnvironment(new Dictionary<string, string?> { ["APS_TOKEN"] = "t" }, start);

        Assert.Equal(temp.Path, options.RepositoryRoot);
        Assert.Equal(Path.Combine(temp.Path, "artifacts", "appbundles"), options.BundleDirectory);
        Assert.Equal(["2026", "2027"], options.RevitVersions);
        Assert.Equal("APS_TOKEN (jeton fourni)", options.CredentialSource);
    }

    [Fact]
    public void Git_head_reads_branch_packed_refs_and_detached_head()
    {
        using var temp = new TempDirectory();
        var git = Directory.CreateDirectory(Path.Combine(temp.Path, ".git")).FullName;
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(git, "packed-refs"), "# pack-refs\n0123456789abcdef0123 refs/heads/main\n");
        Assert.Equal("0123456789ab (main)", GitHead.Read(temp.Path));

        Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
        File.WriteAllText(Path.Combine(git, "refs", "heads", "main"), "fedcba9876543210fedc\n");
        Assert.Equal("fedcba987654 (main)", GitHead.Read(temp.Path));

        File.WriteAllText(Path.Combine(git, "HEAD"), "aaaaaaaaaaaaaaaaaaaa\n");
        Assert.Equal("aaaaaaaaaaaa", GitHead.Read(temp.Path));
        Assert.Null(GitHead.Read(null));
    }

    [Theory]
    [InlineData(new int[0], "aucune")]
    [InlineData(new[] { 1 }, "v1")]
    [InlineData(new[] { 3, 1, 2, 5, 7, 8 }, "v1-v3, v5, v7-v8")]
    public void Versions_are_compressed(int[] versions, string expected) =>
        Assert.Equal(expected, ResourceItemViewModel.Compress(versions));

    [Fact]
    public void Protected_versions_parse_commas_spaces_and_v_prefix() =>
        Assert.Equal([3, 5, 12], MainViewModel.ParseVersions("v3, 5;12 "));

    [Fact]
    public void Alias_plan_creates_or_moves_and_warns_for_prod()
    {
        var state = new ResourceState("2026", DaResourceKind.Activity, "SystemsInventory_R2026", true, [1, 2, 3], new Dictionary<string, int> { ["prod"] = 3 });

        var move = PlanBuilder.SetAlias(state, "prod", 2);
        var create = PlanBuilder.SetAlias(state, "test", 3);

        Assert.Equal("PATCH /activities/SystemsInventory_R2026/aliases/prod : v3 -> v2", move.Steps[0]);
        Assert.Contains("Retour possible : repointer prod sur v3.", move.Steps);
        Assert.Contains(move.Steps, s => s.Contains("PUBLISHING.md section 8", StringComparison.Ordinal));
        Assert.Equal("POST /activities/SystemsInventory_R2026/aliases : création de test -> v3", create.Steps[0]);
        Assert.Throws<InvalidOperationException>(() => PlanBuilder.SetAlias(state, "test", 4));
    }

    [Fact]
    public void Publish_plan_warns_near_quota_and_shows_commit()
    {
        var bundle = new ResourceState("2026", DaResourceKind.AppBundle, "EgisSystemFilter_R2026", true, [.. Enumerable.Range(1, 92)], new Dictionary<string, int>());
        var activity = new ResourceState("2026", DaResourceKind.Activity, "SystemsInventory_R2026", false, [], new Dictionary<string, int>());

        var plan = PlanBuilder.Publish("nick", "abc123 (main)", [("2026", bundle, activity, "/z/EgisSystemFilter_R2026.zip", 2 * 1024 * 1024)]);

        Assert.Contains("commit abc123 (main)", plan.Text);
        Assert.Contains("contrôle du ZIP EgisSystemFilter_R2026.zip (2.0 Mo)", plan.Text.Replace(',', '.'));
        Assert.Contains("ATTENTION : EgisSystemFilter_R2026 a 92 versions (quota 100)", plan.Text);
        Assert.Contains("POST /appbundles/EgisSystemFilter_R2026/aliases : création de test -> nouvelle version", plan.Text);
    }

    [Fact]
    public void Settings_never_render_secrets_through_options()
    {
        var options = new DaManagerOptions { Aps = new ApsSettings { Token = "super-secret-token" }, RevitVersions = [] };
        Assert.DoesNotContain("super-secret-token", options.CredentialSource + options.Aps);
    }
}
