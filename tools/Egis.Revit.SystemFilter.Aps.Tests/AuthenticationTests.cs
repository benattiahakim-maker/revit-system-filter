using System.Net;
using System.Text;

namespace Egis.Revit.SystemFilter.Aps.Tests;

public sealed class AuthenticationTests
{
    private const string TokenUrl = ApsSettings.DefaultAuthenticationUrl;

    [Fact]
    public void Settings_read_deploy_py_variable_names()
    {
        var settings = ApsSettings.FromEnvironment(new Dictionary<string, string?>
        {
            ["APS_CLIENT_ID"] = "id",
            ["APS_CLIENT_SECRET"] = "secret",
            ["APS_TOKEN"] = "tok",
            ["APS_NICKNAME"] = "nick",
            ["APS_DA_BASE_URL"] = "https://da.example/v3",
        });

        Assert.True(settings.HasClientCredentials);
        Assert.True(settings.HasToken);
        Assert.Equal("nick", settings.RequireNickname());
        Assert.Equal("https://da.example/v3", settings.DaBaseUrl);
    }

    [Fact]
    public void Settings_default_base_url_and_blank_values_are_absent()
    {
        var settings = ApsSettings.FromEnvironment(new Dictionary<string, string?> { ["APS_CLIENT_ID"] = "  ", ["APS_TOKEN"] = "" });

        Assert.Equal("https://developer.api.autodesk.com/da/us-east/v3", settings.DaBaseUrl);
        Assert.False(settings.HasClientCredentials);
        Assert.False(settings.HasToken);
        Assert.Throws<ApsException>(() => settings.RequireNickname());
        Assert.Throws<ApsException>(() => settings.CreateTokenProvider(new HttpClient(new FakeHttpHandler())));
    }

    [Fact]
    public void Client_credentials_take_precedence_over_APS_TOKEN()
    {
        var settings = new ApsSettings { ClientId = "id", ClientSecret = "secret", Token = "tok" };
        Assert.IsType<TwoLeggedTokenProvider>(settings.CreateTokenProvider(new HttpClient(new FakeHttpHandler())));

        var fallback = new ApsSettings { Token = "tok" };
        Assert.IsType<StaticTokenProvider>(fallback.CreateTokenProvider(new HttpClient(new FakeHttpHandler())));
    }

    [Fact]
    public void Secrets_are_never_rendered()
    {
        var settings = new ApsSettings { ClientId = "my-client-id", ClientSecret = "my-secret", Token = "my-token", Nickname = "nick" };
        var text = settings + " " + new StaticTokenProvider("my-token");

        Assert.DoesNotContain("my-client-id", text);
        Assert.DoesNotContain("my-secret", text);
        Assert.DoesNotContain("my-token", text);
        Assert.Contains("nick", text);
    }

    [Fact]
    public async Task Two_legged_token_is_requested_with_basic_credentials_and_cached()
    {
        var (handler, client) = Support.Http();
        handler.Expect(HttpMethod.Post, TokenUrl, HttpStatusCode.OK, """{"access_token":"abc","token_type":"Bearer","expires_in":3599}""");
        var time = new ManualTimeProvider();
        var provider = new TwoLeggedTokenProvider(client, "id", "secret", ApsScopes.DesignAutomation, timeProvider: time);

        Assert.Equal("abc", await provider.GetTokenAsync());
        Assert.Equal("abc", await provider.GetTokenAsync());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("id:secret")), request.Authorization);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Equal("grant_type=client_credentials&scope=code%3Aall", request.BodyText);
    }

    [Fact]
    public async Task Two_legged_token_is_renewed_one_minute_before_expiry()
    {
        var (handler, client) = Support.Http();
        handler.Expect(HttpMethod.Post, TokenUrl, HttpStatusCode.OK, """{"access_token":"first","expires_in":600}""");
        handler.Expect(HttpMethod.Post, TokenUrl, HttpStatusCode.OK, """{"access_token":"second","expires_in":600}""");
        var time = new ManualTimeProvider();
        var provider = new TwoLeggedTokenProvider(client, "id", "secret", ApsScopes.All, timeProvider: time);

        Assert.Equal("first", await provider.GetTokenAsync());
        time.Advance(TimeSpan.FromMinutes(8.9));
        Assert.Equal("first", await provider.GetTokenAsync());
        time.Advance(TimeSpan.FromMinutes(0.2));
        Assert.Equal("second", await provider.GetTokenAsync());
        Assert.Contains("scope=code%3Aall+bucket%3Acreate", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task Authentication_failure_raises_ApsException_without_credentials()
    {
        var (handler, client) = Support.Http();
        handler.Expect(HttpMethod.Post, TokenUrl, HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        var provider = new TwoLeggedTokenProvider(client, "my-client-id", "my-secret", ApsScopes.DesignAutomation);

        var error = await Assert.ThrowsAsync<ApsException>(() => provider.GetTokenAsync().AsTask());

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Contains("HTTP 401", error.Message);
        Assert.DoesNotContain("my-secret", error.Message);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("my-client-id:my-secret")), error.Message);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
