using System.Net;
using static Egis.Revit.SystemFilter.DaManager.Tests.FakeApsServer;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task Refresh_lists_engines_resources_versions_and_aliases()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2, 3, 4], ("test", 4), ("prod", 3));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2, 3, 5, 6], ("test", 6));

        await env.ViewModel.RefreshCommand.ExecuteAsync();

        Assert.Equal(["Autodesk.Revit+2025", "Autodesk.Revit+2026", "Autodesk.Revit+2027"], env.ViewModel.Engines);
        Assert.Equal(4, env.ViewModel.Resources.Count);
        var bundle = env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2026");
        Assert.Equal("v1-v4", bundle.VersionsText);
        Assert.Equal("prod → v3, test → v4", bundle.AliasesText);
        Assert.Equal("v1-v3, v5-v6", env.ViewModel.Resources.Single(r => r.Id == "SystemsInventory_R2026").VersionsText);
        Assert.Equal("inexistant", env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2027").VersionsText);
        Assert.Equal(["nick.EgisSystemFilter_R2026"], env.ViewModel.AllAppBundles);
        Assert.Empty(env.Server.Writes);
        Assert.All(env.Server.Requests, r => Assert.Equal($"Bearer {TestEnvironment.Token}", r.Authorization));
    }

    [Fact]
    public async Task Publish_shows_plan_and_writes_nothing_when_declined()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = false;

        await env.ViewModel.PublishCommand.ExecuteAsync();

        var plan = Assert.Single(env.Confirmation.Plans);
        Assert.Contains("POST /appbundles (création de EgisSystemFilter_R2026, v1) + téléversement du ZIP", plan.Text);
        Assert.Contains("POST /activities (création de SystemsInventory_R2026, AppBundle nick.EgisSystemFilter_R2026+test)", plan.Text);
        Assert.Contains("GET /engines", plan.Text);
        Assert.False(plan.IsDestructive);
        Assert.Empty(env.Server.Writes);
        Assert.Contains("Annulé : Publier une nouvelle version en test (rien n'a été écrit)", env.LogText);
    }

    [Fact]
    public async Task Publish_after_confirmation_creates_versions_and_test_aliases()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2], ("test", 2), ("prod", 1));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2], ("test", 2), ("prod", 1));
        env.Confirmation.Answer = true;

        await env.ViewModel.PublishCommand.ExecuteAsync();

        Assert.Contains("POST /appbundles/EgisSystemFilter_R2026/versions (nouvelle version après v2)", env.Confirmation.Plans[0].Text);
        Assert.Contains("PATCH /appbundles/EgisSystemFilter_R2026/aliases/test : v2 -> nouvelle version", env.Confirmation.Plans[0].Text);
        Assert.Equal(3, env.Server.AppBundles["EgisSystemFilter_R2026"].Aliases["test"]);
        Assert.Equal(1, env.Server.AppBundles["EgisSystemFilter_R2026"].Aliases["prod"]);
        Assert.Equal(3, env.Server.Activities["SystemsInventory_R2026"].Aliases["test"]);
        Assert.Contains(env.Server.Requests, r => r.Url == UploadUrl && r.Authorization is null);
        Assert.Contains("Revit 2026 : nick.EgisSystemFilter_R2026+test -> v3, nick.SystemsInventory_R2026+test -> v3", env.LogText);
        Assert.DoesNotContain("SIGNATURE-SECRETE", env.LogText);
        Assert.Equal("v1-v3", env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2026").VersionsText); // état relu
    }

    [Fact]
    public async Task Publish_with_refused_zip_stops_before_confirmation()
    {
        using var env = new TestEnvironment();
        File.WriteAllText(Path.Combine(env.BundleDirectory, "EgisSystemFilter_R2026.zip"), "pas un zip");
        env.Confirmation.Answer = true;

        await env.ViewModel.PublishCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Requests);
        Assert.Contains("refusé par le contrôle : ZIP illisible", env.LogText);
    }

    [Fact]
    public async Task Promote_after_confirmation_points_prod_to_test()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Confirmation.Answer = true;

        await env.ViewModel.PromoteCommand.ExecuteAsync();

        var plan = env.Confirmation.Plans.Single().Text;
        Assert.Contains("EgisSystemFilter_R2026+prod -> v3 (actuellement v2)", plan);
        Assert.Contains("Retour arrière possible : AppBundle v2, Activity v2", plan);
        Assert.Equal(3, env.Server.AppBundles["EgisSystemFilter_R2026"].Aliases["prod"]);
        Assert.Equal(4, env.Server.Activities["SystemsInventory_R2026"].Aliases["prod"]);
        Assert.Equal("nick.EgisSystemFilter_R2026+prod", env.Server.Activities["SystemsInventory_R2026"].Bodies.Last()["appbundles"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Promote_without_test_alias_is_refused_without_writing()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;

        await env.ViewModel.PromoteCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Writes);
        Assert.Contains("aucun alias test à promouvoir", env.LogText);
    }

    [Fact]
    public async Task Rollback_moves_alias_after_confirmation()
    {
        using var env = new TestEnvironment();
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2, 3], ("prod", 3));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "SystemsInventory_R2026");
        env.ViewModel.AliasName = "prod";
        env.ViewModel.AliasVersion = "2";
        env.Confirmation.Answer = true;

        await env.ViewModel.SetAliasCommand.ExecuteAsync();

        Assert.Contains("PATCH /activities/SystemsInventory_R2026/aliases/prod : v3 -> v2", env.Confirmation.Plans.Single().Text);
        Assert.Equal(2, env.Server.Activities["SystemsInventory_R2026"].Aliases["prod"]);
    }

    [Fact]
    public async Task Alias_to_missing_version_is_refused()
    {
        using var env = new TestEnvironment();
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2]);
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "SystemsInventory_R2026");
        env.ViewModel.AliasName = "test";
        env.ViewModel.AliasVersion = "9";

        await env.ViewModel.SetAliasCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Writes);
        Assert.Contains("la version v9 n'existe pas", env.LogText);
    }

    [Fact]
    public async Task Cleanup_lists_versions_and_deletes_nothing_when_declined()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With(Enumerable.Range(1, 12), ("prod", 2), ("test", 12));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2026");
        env.ViewModel.KeepLatest = 3;
        env.ViewModel.ProtectedVersions = "v5, 6";

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        var plan = env.Confirmation.Plans.Single();
        Assert.True(plan.IsDestructive);
        Assert.Contains("conserver v2 (alias prod)", plan.Text);
        Assert.Contains("conserver v5 (protégée (journal))", plan.Text);
        foreach (var version in new[] { 1, 3, 4, 7, 8, 9 })
        {
            Assert.Contains($"SUPPRIMER v{version} : DELETE /appbundles/EgisSystemFilter_R2026/versions/{version}", plan.Text);
        }

        Assert.DoesNotContain("SUPPRIMER v2 ", plan.Text);
        Assert.DoesNotContain("SUPPRIMER v12", plan.Text);
        Assert.Empty(env.Server.Writes);
        Assert.Equal(12, env.Server.AppBundles["EgisSystemFilter_R2026"].Versions.Count);
    }

    [Fact]
    public async Task Cleanup_after_confirmation_never_deletes_aliased_versions()
    {
        using var env = new TestEnvironment();
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With(Enumerable.Range(1, 95), ("prod", 1), ("test", 50));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        Assert.Contains("SystemsInventory_R2026 : 95 versions (quota 100)", env.LogText);
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "SystemsInventory_R2026");
        env.ViewModel.KeepLatest = 5;
        env.Confirmation.Answer = true;

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        Assert.Equal([1, 50, 91, 92, 93, 94, 95], env.Server.Activities["SystemsInventory_R2026"].Versions);
        Assert.Equal(88, env.Server.Writes.Count(w => w.Method == HttpMethod.Delete));
        Assert.Contains("SystemsInventory_R2026 : 88 version(s) supprimée(s).", env.LogText);
    }

    [Fact]
    public async Task Nothing_to_clean_does_not_ask_for_confirmation()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2], ("test", 2));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2026");

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Contains("aucune version à supprimer", env.LogText);
    }

    [Fact]
    public async Task Invalid_protected_versions_are_rejected()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2, 3]);
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2026");
        env.ViewModel.ProtectedVersions = "2, abc";

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Contains("Version protégée invalide : « abc »", env.LogText);
    }

    [Fact]
    public async Task Errors_are_logged_without_secrets()
    {
        using var env = new TestEnvironment();
        env.Server.FailNext = ("/engines", HttpStatusCode.Unauthorized, $$"""{"detail":"Bearer {{TestEnvironment.Token}} refused","url":"https://s3.test/x?X-Amz-Signature=abc"}""");

        await env.ViewModel.RefreshCommand.ExecuteAsync();

        Assert.Contains("error Actualisation : GET /engines : HTTP 401", env.LogText);
        Assert.DoesNotContain(TestEnvironment.Token, env.LogText);
        Assert.DoesNotContain("X-Amz-Signature=abc", env.LogText);
        Assert.Equal("Actualisation : échec (voir le journal).", env.ViewModel.Status);
        Assert.False(env.ViewModel.IsBusy);
    }

    [Fact]
    public async Task Missing_credentials_are_reported_without_any_request()
    {
        using var env = new TestEnvironment(withCredentials: false);

        await env.ViewModel.RefreshCommand.ExecuteAsync();

        Assert.Empty(env.Server.Requests);
        Assert.Contains("APS_CLIENT_ID et APS_CLIENT_SECRET (ou APS_TOKEN) sont obligatoires", env.LogText);
        Assert.StartsWith("aucun identifiant", env.ViewModel.CredentialSource);
    }

    [Fact]
    public async Task Write_commands_require_a_selected_existing_resource()
    {
        using var env = new TestEnvironment();
        await env.ViewModel.RefreshCommand.ExecuteAsync();

        Assert.False(env.ViewModel.SetAliasCommand.CanExecute(null));
        env.ViewModel.SelectedResource = env.ViewModel.Resources[0]; // inexistant
        Assert.False(env.ViewModel.CleanupCommand.CanExecute(null));
    }
}
