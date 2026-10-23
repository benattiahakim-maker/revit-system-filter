namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public class SettingsTests
{
    private static readonly string[] Entries = { "Main.rvt", "Links/CVC.rvt", "Links/PSY.rvt" };

    [Fact]
    public void Defauts_sans_fichier()
    {
        var s = JobSettings.Load(null);
        Assert.Equal("systems-inventory.json", s.OutputFileName);
        Assert.True(s.IncludeDuctSystems);
        Assert.True(s.IncludePipingSystems);
        Assert.Null(s.MainModelPath);
    }

    [Fact]
    public void Lit_toutes_les_cles()
    {
        var s = JobSettings.Parse("""{"schemaVersion":"1.0","mainModelPath":"Links\\CVC.rvt","outputFileName":"x.json","includeDuctSystems":false,"includePipingSystems":true}""");
        Assert.Equal("Links/CVC.rvt", s.MainModelPath);
        Assert.Equal("x.json", s.OutputFileName);
        Assert.False(s.IncludeDuctSystems);
        Assert.Equal("Links/CVC.rvt", s.ResolveMainModel(Entries));
    }

    [Theory]
    [InlineData("""{"foo":1}""")]
    [InlineData("""[]""")]
    [InlineData("""{"mainModelPath":"../Main.rvt"}""")]
    [InlineData("""{"mainModelPath":"C:/Main.rvt"}""")]
    [InlineData("""{"mainModelPath":"Main.txt"}""")]
    [InlineData("""{"outputFileName":"../x.json"}""")]
    [InlineData("""{"outputFileName":"sub/x.json"}""")]
    [InlineData("""{"outputFileName":"x.txt"}""")]
    [InlineData("""{"outputFileName":"run.log"}""")]
    [InlineData("""{"includeDuctSystems":"yes"}""")]
    [InlineData("""{"schemaVersion":"2.0"}""")]
    [InlineData("""pas du json""")]
    public void Refuse_les_reglages_invalides(string json) =>
        Assert.Throws<InputArchiveException>(() => JobSettings.Parse(json));

    [Fact]
    public void MainModelPath_doit_exister_dans_le_zip()
    {
        var s = JobSettings.Parse("""{"mainModelPath":"Absent.rvt"}""");
        Assert.Throws<InputArchiveException>(() => s.ResolveMainModel(Entries));
    }

    [Fact]
    public void Sans_mainModelPath_l_unique_rvt_a_la_racine_est_retenu()
    {
        Assert.Equal("Main.rvt", new JobSettings().ResolveMainModel(Entries));
    }

    [Fact]
    public void Sans_mainModelPath_plusieurs_rvt_a_la_racine_est_refuse()
    {
        Assert.Throws<InputArchiveException>(() => new JobSettings().ResolveMainModel(new[] { "A.rvt", "B.rvt" }));
        Assert.Throws<InputArchiveException>(() => new JobSettings().ResolveMainModel(new[] { "Links/A.rvt" }));
    }
}
