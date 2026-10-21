namespace Egis.Revit.SystemFilter.Aps.Tests;

/// <summary>Variables manquantes (noms seulement) et texte des erreurs inattendues (masqué).</summary>
public sealed class StartupSupportTests
{
    [Fact]
    public void Missing_credential_variables_are_named_never_valued()
    {
        Assert.Equal(["APS_CLIENT_ID", "APS_CLIENT_SECRET (ou APS_TOKEN)"], new ApsSettings().MissingCredentialVariables());
        Assert.Equal(["APS_CLIENT_SECRET (ou APS_TOKEN)"], new ApsSettings { ClientId = "id-visible" }.MissingCredentialVariables());
        Assert.Equal(["APS_CLIENT_ID (ou APS_TOKEN)"], new ApsSettings { ClientSecret = "secret-cache" }.MissingCredentialVariables());
        Assert.Empty(new ApsSettings { Token = "jeton" }.MissingCredentialVariables());
        Assert.Empty(new ApsSettings { ClientId = "id", ClientSecret = "secret" }.MissingCredentialVariables());
        Assert.DoesNotContain(new ApsSettings { ClientSecret = "secret-cache" }.MissingCredentialVariables(), v => v.Contains("secret-cache", StringComparison.Ordinal));
    }

    [Fact]
    public void Token_provider_error_uses_the_shared_message()
    {
        var error = Assert.Throws<ApsException>(() => new ApsSettings().CreateTokenProvider(new HttpClient()));

        Assert.Equal(ApsSettings.MissingCredentialsMessage, error.Message);
    }

    [Fact]
    public void Unexpected_error_text_is_redacted_and_lists_causes()
    {
        var exception = new InvalidOperationException(
            "échec Authorization: Bearer abc.def-123 vers https://s3.test/obj?X-Amz-Signature=xyz",
            new IOException("client_secret=tres-secret"));

        var text = UnexpectedError.Describe(exception, "Interface");

        Assert.StartsWith("Interface : erreur inattendue.", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException : ", text, StringComparison.Ordinal);
        Assert.Contains("IOException : ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def-123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Amz-Signature=xyz", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tres-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_and_null_exceptions_are_described()
    {
        var text = UnexpectedError.Describe(new AggregateException(new InvalidOperationException("un"), new IOException("deux")), "Tâche");

        Assert.Contains("InvalidOperationException : un", text, StringComparison.Ordinal);
        Assert.Contains("IOException : deux", text, StringComparison.Ordinal);
        Assert.StartsWith("Erreur fatale : erreur inattendue.", UnexpectedError.Describe(null, "Erreur fatale"), StringComparison.Ordinal);
    }
}
