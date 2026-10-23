using System.IO.Compression;
using System.Text;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rsf-" + Guid.NewGuid().ToString("N"));
    public TempDir() { Directory.CreateDirectory(Path); }
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }

    /// <summary>Crée un ZIP avec des entrées aux noms bruts (y compris malveillants).</summary>
    public string Zip(string name, params (string Entry, string Content)[] entries)
    {
        var path = File(name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = archive.CreateEntry(entry);
            if (!entry.EndsWith('/')) { using var s = e.Open(); var b = Encoding.UTF8.GetBytes(content); s.Write(b, 0, b.Length); }
        }
        return path;
    }
}
