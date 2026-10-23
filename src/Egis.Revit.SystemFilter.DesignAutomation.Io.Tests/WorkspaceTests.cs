using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public class WorkspaceTests
{
    [Fact]
    public void Fichier_zip_extrait_settings_lus_et_sortie_zippee()
    {
        using var t = new TempDir();
        File.Move(t.Zip("in.zip", ("Main.rvt", "m"), ("Links/CVC.rvt", "c"), ("Other.rvt", "o")), t.File("input"));
        File.WriteAllText(t.File("settings.json"), """{"schemaVersion":"1.0","mainModelPath":"Main.rvt"}""");

        var ws = new JobWorkspace(t.Path);
        ws.Prepare();
        Assert.Equal("Main.rvt", ws.MainModelRelative);
        Assert.True(File.Exists(ws.MainModelFullPath));
        Assert.StartsWith(Path.GetFullPath(t.Path), ws.MainModelFullPath);

        ws.Log.Warn("lien manquant : ARC.rvt");
        ws.WriteInventory(Encoding.UTF8.GetBytes("""{"schemaVersion":"1.0"}"""));
        ws.WriteRunLog();
        JobWorkspace.ZipResult(ws.ResultDirectory, t.File("output.zip"));

        using var zip = ZipFile.OpenRead(t.File("output.zip"));
        Assert.Equal(new[] { "run.log", "systems-inventory.json" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n));
        using var reader = new StreamReader(zip.GetEntry("run.log")!.Open());
        var log = reader.ReadToEnd();
        Assert.Contains("lien manquant : ARC.rvt", log);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('/'), log);
    }

    [Fact]
    public void Dossier_deja_extrait_sans_settings_unique_rvt_racine()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.File("input"));
        File.WriteAllText(Path.Combine(t.File("input"), "Seul.rvt"), "m");
        var ws = new JobWorkspace(t.Path);
        ws.Prepare();
        Assert.Equal("Seul.rvt", ws.MainModelRelative);
        Assert.Contains("absent", ws.Log.ToString());
    }

    [Fact]
    public void Input_absent_refuse()
    {
        using var t = new TempDir();
        Assert.Throws<InputArchiveException>(() => new JobWorkspace(t.Path).Prepare());
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("2027")]
    public void Activity_json_suit_le_contrat_du_backend(string year)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "definitions", year, "activity.json");
        var p = JsonNode.Parse(File.ReadAllText(path))!["parameters"]!.AsObject();
        Assert.Equal(new[] { "inputZip", "result", "settings" }, p.Select(kv => kv.Key).OrderBy(k => k));
        Assert.True(p["inputZip"]!["zip"]!.GetValue<bool>());
        Assert.Equal("input", p["inputZip"]!["localName"]!.GetValue<string>());
        Assert.Equal("get", p["inputZip"]!["verb"]!.GetValue<string>());
        Assert.False(p["settings"]!["zip"]!.GetValue<bool>());
        Assert.Equal("settings.json", p["settings"]!["localName"]!.GetValue<string>());
        Assert.False(p["settings"]!["required"]!.GetValue<bool>());
        Assert.True(p["result"]!["zip"]!.GetValue<bool>());
        Assert.Equal("result", p["result"]!["localName"]!.GetValue<string>());
        Assert.Equal("put", p["result"]!["verb"]!.GetValue<string>());
        var cmd = JsonNode.Parse(File.ReadAllText(path))!["commandLine"]![0]!.GetValue<string>();
        Assert.DoesNotContain("/i ", cmd);
        Assert.Contains($"EgisSystemFilter_R{year}", cmd);
    }
}
