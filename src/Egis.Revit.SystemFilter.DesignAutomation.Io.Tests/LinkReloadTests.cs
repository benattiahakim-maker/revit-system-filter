using Egis.Revit.SystemFilter.DesignAutomation.Io;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public sealed class LinkReloadTests
{
    [Fact]
    public void HostModel_est_un_alias_de_mainModelPath()
        => Assert.Equal("Links/CVC.rvt", JobSettings.Parse("""{"hostModel":"Links\\CVC.rvt"}""").MainModelPath);

    [Fact]
    public void Messages_de_rechargement()
    {
        Assert.Equal("lien rechargé : A.rvt chargé depuis sub/A.rvt", LinkResolver.ReloadMessage("A.rvt", "sub/A.rvt"));
        Assert.Equal("lien rechargé : A.rvt introuvable dans le dossier de travail", LinkResolver.ReloadMessage("A.rvt", null));
        Assert.Contains("échec", LinkResolver.ReloadMessage("A.rvt", "A.rvt", "X"));
    }

    [Fact]
    public void Resolution_recursive_par_nom_de_fichier()
    {
        var r = new LinkResolver("/tmp/root", new[] { "Main.rvt", "deep/dir/Archi.rvt" }, "Main.rvt");
        Assert.Equal("deep/dir/Archi.rvt", r.Resolve(@"C:\Projets\Archi.rvt"));
        Assert.Null(r.Resolve("Absent.rvt"));
    }
}
