namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public class ZipTests
{
    [Theory]
    [InlineData("../evil.rvt")]
    [InlineData("a/../../evil.rvt")]
    [InlineData("..\\evil.rvt")]
    [InlineData("/etc/evil.rvt")]
    [InlineData("C:/evil.rvt")]
    [InlineData("C:\\evil.rvt")]
    [InlineData("\\\\server\\share\\evil.rvt")]
    [InlineData("a/./b.rvt")]
    public void Extract_refuse_le_zip_slip_et_les_chemins_absolus(string entry)
    {
        using var t = new TempDir();
        var zip = t.Zip("in.zip", ("ok.rvt", "x"), (entry, "x"));
        var dest = t.File("out");
        Assert.Throws<InputArchiveException>(() => SafeZipExtractor.Extract(zip, dest));
        Assert.False(File.Exists(Path.Combine(t.Path, "evil.rvt")));
    }

    [Fact]
    public void Extract_garde_l_arborescence_relative()
    {
        using var t = new TempDir();
        var zip = t.Zip("in.zip", ("Main.rvt", "m"), ("Links/", ""), ("Links/CVC.rvt", "c"));
        var files = SafeZipExtractor.Extract(zip, t.File("out"));
        Assert.Equal(new[] { "Links/CVC.rvt", "Main.rvt" }, files);
        Assert.Equal("c", File.ReadAllText(Path.Combine(t.File("out"), "Links", "CVC.rvt")));
    }

    [Fact]
    public void Extract_refuse_trop_d_entrees()
    {
        using var t = new TempDir();
        var zip = t.Zip("in.zip", ("a.rvt", "1"), ("b.rvt", "2"), ("c.rvt", "3"));
        var ex = Assert.Throws<InputArchiveException>(() => SafeZipExtractor.Extract(zip, t.File("out"), new ArchiveLimits { MaxEntries = 2 }));
        Assert.Contains("trop d'entrées", ex.Message);
    }

    [Fact]
    public void Extract_compte_les_octets_reellement_decompresses()
    {
        using var t = new TempDir();
        var zip = t.Zip("in.zip", ("a.rvt", new string('x', 600)), ("b.rvt", new string('y', 600)));
        var ex = Assert.Throws<InputArchiveException>(() => SafeZipExtractor.Extract(zip, t.File("out"), new ArchiveLimits { MaxTotalBytes = 1000 }));
        Assert.Contains("taille décompressée", ex.Message);
    }

    [Fact]
    public void Extract_refuse_les_doublons_insensibles_a_la_casse()
    {
        using var t = new TempDir();
        var zip = t.Zip("in.zip", ("Main.rvt", "1"), ("main.RVT", "2"));
        Assert.Throws<InputArchiveException>(() => SafeZipExtractor.Extract(zip, t.File("out")));
    }

    [Fact]
    public void Extract_refuse_un_fichier_qui_n_est_pas_un_zip()
    {
        using var t = new TempDir();
        File.WriteAllText(t.File("in.zip"), "pas un zip");
        Assert.Throws<InputArchiveException>(() => SafeZipExtractor.Extract(t.File("in.zip"), t.File("out")));
    }

    [Fact]
    public void ScanDirectory_refuse_un_lien_symbolique()
    {
        using var t = new TempDir();
        var input = Directory.CreateDirectory(t.File("input")).FullName;
        File.WriteAllText(Path.Combine(input, "Main.rvt"), "m");
        File.CreateSymbolicLink(Path.Combine(input, "Link.rvt"), "/etc/hostname");
        Assert.Throws<InputArchiveException>(() => SafeZipExtractor.ScanDirectory(input));
    }

    [Fact]
    public void ScanDirectory_applique_les_plafonds()
    {
        using var t = new TempDir();
        var input = Directory.CreateDirectory(t.File("input")).FullName;
        File.WriteAllText(Path.Combine(input, "a.rvt"), new string('x', 50));
        File.WriteAllText(Path.Combine(input, "b.rvt"), new string('x', 50));
        Assert.Throws<InputArchiveException>(() => SafeZipExtractor.ScanDirectory(input, new ArchiveLimits { MaxTotalBytes = 80 }));
        Assert.Throws<InputArchiveException>(() => SafeZipExtractor.ScanDirectory(input, new ArchiveLimits { MaxEntries = 1 }));
        Assert.Equal(new[] { "a.rvt", "b.rvt" }, SafeZipExtractor.ScanDirectory(input));
    }
}
