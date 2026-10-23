using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class BucketVariableTests
{
    private static RunnerOptions Load(string? key, string? legacy) => RunnerOptions.FromEnvironment(
        new Dictionary<string, string?> { ["APS_BUCKET_KEY"] = key, ["APS_BUCKET"] = legacy, ["APS_CLIENT_ID"] = "client" },
        Path.GetTempPath());

    [Fact]
    public void Aps_bucket_key_seul_est_utilise_sans_avertissement()
    {
        var options = Load("egsf-key", null);
        Assert.Equal("egsf-key", options.BucketKey);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void Aps_bucket_seul_est_utilise_avec_un_seul_avertissement_de_depreciation()
    {
        var options = Load(null, "egsf-legacy");
        Assert.Equal("egsf-legacy", options.BucketKey);
        var warning = Assert.Single(options.Warnings);
        Assert.Contains("déprécié", warning);
        Assert.Contains("APS_BUCKET_KEY", warning);
    }

    [Fact]
    public void Les_deux_differents_aps_bucket_key_l_emporte_avec_avertissement()
    {
        var options = Load("egsf-key", "egsf-legacy");
        Assert.Equal("egsf-key", options.BucketKey);
        var warning = Assert.Single(options.Warnings);
        Assert.Contains("valeurs différentes", warning);
    }

    [Fact]
    public void Les_deux_identiques_sans_avertissement()
    {
        var options = Load("egsf-key", "egsf-key");
        Assert.Equal("egsf-key", options.BucketKey);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void Aucun_des_deux_bucket_derive_de_l_id_client()
    {
        var options = Load(null, null);
        Assert.Equal(RunnerOptions.DefaultBucketKey("client"), options.BucketKey);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void Avertissement_affiche_une_seule_fois_dans_le_journal()
    {
        using var env = new TestEnvironment(warnings: ["APS_BUCKET est déprécié : renommer la variable en APS_BUCKET_KEY."]);
        env.ViewModel.Initialize();
        Assert.Single(env.ViewModel.Log, e => e.Message.Contains("APS_BUCKET est déprécié"));
    }
}
