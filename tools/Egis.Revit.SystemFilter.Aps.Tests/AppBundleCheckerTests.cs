using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.Aps.Tests;

/// <summary>Mêmes cas que <c>src/Packaging/scripts/tests/test_packaging.py</c> pour le contrôle du ZIP.</summary>
public sealed class AppBundleCheckerTests
{
    [Theory]
    [InlineData("2026")]
    [InlineData("2027")]
    public void Valid_bundle_passes(string version)
    {
        using var temp = new TempDirectory();
        var zip = Path.Combine(temp.Path, "b.zip");
        Support.WriteValidBundle(zip, version);

        Assert.Empty(AppBundleChecker.Check(zip, version));
    }

    [Fact]
    public void Wrong_framework_fails()
    {
        using var temp = new TempDirectory();
        var zip = Path.Combine(temp.Path, "b.zip");
        Support.WriteValidBundle(zip, "2026");

        Assert.Contains(AppBundleChecker.Check(zip, "2027"), e => e.Contains("framework"));
    }

    [Fact]
    public void Forbidden_unknown_and_missing_files_are_listed()
    {
        using var temp = new TempDirectory();
        var zip = Path.Combine(temp.Path, "b.zip");
        Support.WriteValidBundle(zip, "2026", entries =>
        {
            entries.Remove("EgisSystemFilter.bundle/Contents/DesignAutomationBridge.dll");
            entries["EgisSystemFilter.bundle/Contents/Egis.Revit.SystemFilter.deps.json"] = [1];
            entries["EgisSystemFilter.bundle/Contents/notes.txt"] = [1];
            entries["Other.bundle/PackageContents.xml"] = [1];
        });

        var errors = AppBundleChecker.Check(zip, "2026");

        Assert.Contains(errors, e => e.StartsWith("fichier interdit : EgisSystemFilter.bundle/Contents/Egis.Revit.SystemFilter.deps.json", StringComparison.Ordinal));
        Assert.Contains("fichier non autorisé : EgisSystemFilter.bundle/Contents/notes.txt", errors);
        Assert.Contains("fichier requis manquant : EgisSystemFilter.bundle/Contents/DesignAutomationBridge.dll", errors);
        Assert.Contains(errors, e => e.Contains("dossier racine"));
    }

    [Fact]
    public void Absolute_assembly_path_in_addin_fails()
    {
        using var temp = new TempDirectory();
        var zip = Path.Combine(temp.Path, "b.zip");
        Support.WriteValidBundle(zip, "2026", entries =>
        {
            var addin = System.Text.Encoding.UTF8.GetString(entries["EgisSystemFilter.bundle/Contents/EgisSystemFilter.addin"]);
            entries["EgisSystemFilter.bundle/Contents/EgisSystemFilter.addin"] = System.Text.Encoding.UTF8.GetBytes(addin.Replace(@".\Egis.Revit.SystemFilter.DesignAutomation.dll", @"C:\Addins\Egis.Revit.SystemFilter.dll"));
        });

        Assert.Contains(AppBundleChecker.Check(zip, "2026"), e => e.Contains("Assembly"));
    }

    [Fact]
    public void Unreadable_zip_is_reported()
    {
        using var temp = new TempDirectory();
        var zip = Path.Combine(temp.Path, "b.zip");
        File.WriteAllText(zip, "pas un zip");

        Assert.StartsWith("ZIP illisible", Assert.Single(AppBundleChecker.Check(zip, "2026")));
    }
}
