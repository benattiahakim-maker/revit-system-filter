namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public class LinkAndLogTests
{
    private static readonly string[] Entries = { "Main/Main.rvt", "Links/CVC.rvt", "Links/PSY.rvt", "Other/PSY.rvt" };

    [Fact]
    public void Lien_relatif_au_modele_principal()
    {
        using var t = new TempDir();
        var r = new LinkResolver(t.Path, Entries, "Main/Main.rvt");
        Assert.Equal("Links/CVC.rvt", r.Resolve("..\\Links\\CVC.rvt"));
    }

    [Fact]
    public void Lien_absolu_resolu_par_nom_unique_dans_le_zip()
    {
        using var t = new TempDir();
        var r = new LinkResolver(t.Path, Entries, "Main/Main.rvt");
        Assert.Equal("Links/CVC.rvt", r.Resolve("C:\\Projets\\X\\CVC.rvt"));
    }

    [Fact]
    public void Lien_ambigu_ou_absent_ou_hors_dossier_non_resolu()
    {
        using var t = new TempDir();
        var r = new LinkResolver(t.Path, Entries, "Main/Main.rvt");
        Assert.Null(r.Resolve("C:\\Projets\\PSY.rvt"));          // deux PSY.rvt : ambigu
        Assert.Null(r.Resolve("C:\\Projets\\ARC.rvt"));          // absent
        Assert.Null(r.Resolve("../../../etc/ARC.rvt"));          // sort du dossier, et absent
        Assert.Null(r.Resolve(null));
        Assert.Throws<InputArchiveException>(() => r.FullPath("../x.rvt"));
    }

    [Fact]
    public void DisplayName_ne_garde_que_le_nom_de_fichier()
    {
        Assert.Equal("ARC.rvt", LinkResolver.DisplayName("\\\\srv\\projets\\ARC.rvt"));
    }

    [Theory]
    [InlineData("Ouverture de C:\\Users\\bob\\work\\Main.rvt échouée")]
    [InlineData("chemin /tmp/da/work/input/Main.rvt introuvable")]
    [InlineData("partage \\\\srv\\share\\Main.rvt")]
    [InlineData("url https://bucket.s3.amazonaws.com/x?X-Amz-Signature=abc")]
    [InlineData("Authorization: Bearer abc.def")]
    [InlineData("token=supersecret")]
    public void RunLog_masque_chemins_absolus_et_secrets(string message)
    {
        var log = new RunLog();
        log.Warn(message);
        var line = log.ToString();
        Assert.DoesNotContain("bob", line);
        Assert.DoesNotContain("/tmp/da", line);
        Assert.DoesNotContain("srv", line);
        Assert.DoesNotContain("abc", line);
        Assert.DoesNotContain("supersecret", line);
        Assert.Equal(1, log.Warnings);
    }

    [Fact]
    public void RunLog_garde_les_chemins_relatifs()
    {
        var log = new RunLog();
        log.Warn("lien manquant : Links/ARC.rvt");
        Assert.Contains("Links/ARC.rvt", log.ToString());
    }
}
