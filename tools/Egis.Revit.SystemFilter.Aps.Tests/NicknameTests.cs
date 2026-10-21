using System.Net;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;

namespace Egis.Revit.SystemFilter.Aps.Tests;

/// <summary>GET / PATCH /forgeapps/me (référence Automation API v3, ForgeApps).</summary>
public sealed class NicknameTests
{
    [Fact]
    public async Task Get_reads_the_nickname_string()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/forgeapps/me", HttpStatusCode.OK, "\"egr_rooms\"");

        Assert.Equal("egr_rooms", await DesignAutomationClientTests.Client(http).GetNicknameAsync());
        Assert.Equal($"Bearer {Support.Token}", Assert.Single(handler.Requests).Authorization);
    }

    [Fact]
    public async Task Get_without_body_is_an_error()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Da}/forgeapps/me", HttpStatusCode.OK, string.Empty);

        var error = await Assert.ThrowsAsync<ApsException>(() => DesignAutomationClientTests.Client(http).GetNicknameAsync());

        Assert.Contains("nickname absent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_sends_the_nickname_in_the_json_body()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/forgeapps/me", HttpStatusCode.OK);

        await DesignAutomationClientTests.Client(http).SetNicknameAsync("egr_rooms");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v3/forgeapps/me", request.PathAndQuery); // jamais en paramètre de requête
        Assert.Equal("""{"nickname":"egr_rooms"}""", request.BodyText);
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        Assert.Equal($"Bearer {Support.Token}", request.Authorization);
    }

    [Fact]
    public async Task Conflict_explains_both_causes()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/forgeapps/me", HttpStatusCode.Conflict, """{"diagnostic":"in use"}""");

        var error = await Assert.ThrowsAsync<ApsException>(() => DesignAutomationClientTests.Client(http).SetNicknameAsync("egr_rooms"));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Contains("déjà utilisé par une autre application", error.Message, StringComparison.Ordinal);
        Assert.Contains("déjà des données", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_errors_are_reported_as_is()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Patch, $"{Support.Da}/forgeapps/me", HttpStatusCode.Forbidden, """{"diagnostic":"no"}""");

        var error = await Assert.ThrowsAsync<ApsException>(() => DesignAutomationClientTests.Client(http).SetNicknameAsync("egr_rooms"));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.StartsWith("PATCH /forgeapps/me : HTTP 403", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("egr.rooms")]
    [InlineData("egr+rooms")]
    [InlineData("egr rooms")]
    [InlineData("egr-rooms")]
    [InlineData("salle_é")]
    public async Task Invalid_nickname_is_refused_before_any_request(string nickname)
    {
        var (handler, http) = Support.Http();

        await Assert.ThrowsAsync<ApsException>(() => DesignAutomationClientTests.Client(http).SetNicknameAsync(nickname));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("egr_rooms", true)]
    [InlineData("EGR2026", true)]
    [InlineData(null, false)]
    [InlineData("a.b", false)]
    public void Nickname_rule(string? nickname, bool valid)
    {
        Assert.Equal(valid, DesignAutomationClient.IsValidNickname(nickname));
        Assert.False(DesignAutomationClient.IsValidNickname(new string('a', DesignAutomationClient.MaxNicknameLength + 1)));
        Assert.True(DesignAutomationClient.IsValidNickname(new string('a', DesignAutomationClient.MaxNicknameLength)));
    }
}
