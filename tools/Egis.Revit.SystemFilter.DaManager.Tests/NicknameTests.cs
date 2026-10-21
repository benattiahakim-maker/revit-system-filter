using System.Net;
using Egis.Revit.SystemFilter.DaManager.Services;
using static Egis.Revit.SystemFilter.DaManager.Tests.FakeApsServer;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

/// <summary>Action « Définir le nickname » : plan, confirmation, PATCH /forgeapps/me.</summary>
public sealed class NicknameTests
{
    private static IEnumerable<(HttpMethod Method, string Url, string? Authorization, string Body)> Patches(TestEnvironment env) =>
        env.Server.Writes.Where(w => w.Url.EndsWith("/forgeapps/me", StringComparison.Ordinal));

    [Fact]
    public async Task Plan_shows_current_and_target_nickname_and_writes_nothing_when_declined()
    {
        using var env = new TestEnvironment();
        env.ViewModel.Nickname = "egr_rooms";
        env.Confirmation.Answer = false;

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        var plan = Assert.Single(env.Confirmation.Plans);
        Assert.Equal("Définir le nickname de l'application APS", plan.Title);
        Assert.Contains("GET /forgeapps/me : aucun nickname (APS renvoie l'ID client, non affiché).", plan.Text);
        Assert.Contains("Nickname cible : « egr_rooms ».", plan.Text);
        Assert.Contains("PATCH /forgeapps/me, corps {\"nickname\": \"egr_rooms\"}.", plan.Text);
        Assert.Contains("AUCUNE donnée Design Automation", plan.Text);
        Assert.Contains("une seule fois", plan.Text);
        Assert.Contains("DELETE /forgeapps/me", plan.Text);
        Assert.False(plan.IsDestructive);
        Assert.Empty(env.Server.Writes);
        Assert.DoesNotContain(ClientId, env.LogText, StringComparison.Ordinal);
        Assert.Equal(ClientId, env.Server.ForgeAppNickname);
    }

    [Fact]
    public async Task Confirmed_plan_patches_forgeapps_me_and_checks_the_result()
    {
        using var env = new TestEnvironment();
        env.ViewModel.Nickname = "egr_rooms";
        env.Confirmation.Answer = true;

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        var patch = Assert.Single(Patches(env));
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal($"{BaseUrl}/forgeapps/me", patch.Url);
        Assert.Equal("""{"nickname":"egr_rooms"}""", patch.Body);
        Assert.Equal($"Bearer {TestEnvironment.Token}", patch.Authorization);
        Assert.Equal("egr_rooms", env.Server.ForgeAppNickname);
        Assert.Equal(2, env.Server.Requests.Count(r => r.Method == HttpMethod.Get && r.Url.EndsWith("/forgeapps/me", StringComparison.Ordinal))); // lu avant, relu après
        Assert.Contains("APS_NICKNAME=egr_rooms", env.LogText, StringComparison.Ordinal);
        Assert.Equal("Définition du nickname : terminé.", env.ViewModel.Status);
    }

    [Fact]
    public async Task Same_nickname_needs_no_plan()
    {
        using var env = new TestEnvironment();
        env.Server.ForgeAppNickname = "nick";
        env.Confirmation.Answer = true;

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Writes);
        Assert.Contains("déjà « nick » : rien à faire", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_data_is_announced_and_conflict_is_reported()
    {
        using var env = new TestEnvironment();
        env.Server.ForgeAppNickname = "nick";
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1], ("test", 1));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1], ("test", 1));
        env.ViewModel.Nickname = "egr_rooms";
        env.Confirmation.Answer = true;

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        var plan = Assert.Single(env.Confirmation.Plans);
        Assert.Contains("GET /forgeapps/me : nickname actuel « nick ».", plan.Text);
        Assert.Contains("L'application a déjà 2 AppBundle(s) / Activity(ies) : APS refusera très probablement le changement (409).", plan.Text);
        Assert.Contains("HTTP 409", env.LogText, StringComparison.Ordinal);
        Assert.Contains("déjà des données", env.LogText, StringComparison.Ordinal);
        Assert.Equal("nick", env.Server.ForgeAppNickname);
        Assert.Equal("Définition du nickname : échec (voir le journal).", env.ViewModel.Status);
    }

    [Fact]
    public async Task Taken_nickname_is_reported()
    {
        using var env = new TestEnvironment();
        env.Server.TakenNicknames.Add("egr_rooms");
        env.ViewModel.Nickname = "egr_rooms";
        env.Confirmation.Answer = true;

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        Assert.Contains("déjà utilisé par une autre application", env.LogText, StringComparison.Ordinal);
        Assert.Equal(ClientId, env.Server.ForgeAppNickname);
    }

    [Theory]
    [InlineData("")]
    [InlineData("egr.rooms")]
    [InlineData("egr-rooms")]
    public async Task Invalid_target_stops_before_any_request(string nickname)
    {
        using var env = new TestEnvironment();
        env.ViewModel.Nickname = nickname;
        env.Confirmation.Answer = true;

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        Assert.Empty(env.Server.Requests);
        Assert.Empty(env.Confirmation.Plans);
        Assert.Contains("Saisir le nickname voulu", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Known_client_id_is_recognised_and_never_shown()
    {
        const string clientId = "client-id-de-test";
        using var env = new TestEnvironment(clientId: clientId);
        env.Server.ForgeAppNickname = clientId;
        env.ViewModel.Nickname = "egr_rooms";

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        Assert.Contains("aucun nickname", Assert.Single(env.Confirmation.Plans).Text);
        Assert.DoesNotContain(clientId, env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Works_without_repository()
    {
        using var env = new TestEnvironment();
        env.ViewModel.RepositoryRoot = Path.Combine(env.Root, "absent");
        env.ViewModel.Nickname = "egr_rooms";
        env.Confirmation.Answer = true;

        await env.ViewModel.SetNicknameCommand.ExecuteAsync();

        Assert.Equal("egr_rooms", env.Server.ForgeAppNickname);
    }

    [Theory]
    [InlineData("egr_rooms", null, true)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345", null, false)] // forme d'un ID client
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345", "autre", true)]
    [InlineData("my-client", "my-client", false)]
    public void Client_id_detection(string current, string? clientId, bool hasNickname) =>
        Assert.Equal(hasNickname, PlanBuilder.HasNickname(current, clientId));

    [Fact]
    public void Plan_rejects_an_invalid_target() =>
        Assert.Throws<InvalidOperationException>(() => PlanBuilder.SetNickname("nick", "a+b", null, 0));

    [Fact]
    public void Plan_warns_when_a_nickname_already_exists_without_data()
    {
        var plan = PlanBuilder.SetNickname("old_nick", "new_nick", null, 0);

        Assert.Contains("Un nickname est déjà défini", plan.Text);
        Assert.Contains("APS_NICKNAME=new_nick", plan.Text);
    }
}
