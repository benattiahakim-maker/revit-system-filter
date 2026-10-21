using System.IO.Compression;
using System.Text;

namespace Egis.Revit.SystemFilter.Aps.Tests;

internal static class Support
{
    public const string Da = "https://da.test/v3";
    public const string Oss = "https://oss.test/oss/v2";
    public const string Token = "jeton-de-test";

    public static string DefinitionsDirectory => Path.Combine(AppContext.BaseDirectory, "Data", "definitions");

    public static (FakeHttpHandler Handler, HttpClient Client) Http()
    {
        var handler = new FakeHttpHandler();
        return (handler, new HttpClient(handler));
    }

    /// <summary>ZIP conforme à AppBundleChecker pour <paramref name="version"/>.</summary>
    public static void WriteValidBundle(string path, string version, Action<Dictionary<string, byte[]>>? mutate = null)
    {
        const string bundle = "EgisSystemFilter.bundle";
        const string contents = bundle + "/Contents";
        var framework = version == "2027" ? ".NETCoreApp,Version=v10.0" : ".NETCoreApp,Version=v8.0";
        var entries = new Dictionary<string, byte[]>
        {
            [$"{bundle}/PackageContents.xml"] = Encoding.UTF8.GetBytes(
                $"""
                <ApplicationPackage>
                  <Components>
                    <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R{version}" SeriesMax="R{version}" />
                    <ComponentEntry AppName="EgisSystemFilter_R{version}" ModuleName="./Contents/EgisSystemFilter.addin" />
                  </Components>
                </ApplicationPackage>
                """),
            [$"{contents}/EgisSystemFilter.addin"] = Encoding.UTF8.GetBytes(
                """
                <RevitAddIns>
                  <AddIn Type="DBApplication">
                    <Assembly>.\Egis.Revit.SystemFilter.DesignAutomation.dll</Assembly>
                    <AddInId>2f3c6c1e-6d1a-4e3b-9a55-0e8a9c4b7d21</AddInId>
                    <FullClassName>Egis.Revit.SystemFilter.DesignAutomation.SystemFilterDbApplication</FullClassName>
                  </AddIn>
                </RevitAddIns>
                """),
            [$"{contents}/Egis.Revit.SystemFilter.DesignAutomation.dll"] = Encoding.UTF8.GetBytes("MZ…" + framework),
            [$"{contents}/Egis.Revit.SystemFilter.Core.dll"] = [0x4D, 0x5A],
            [$"{contents}/DesignAutomationBridge.dll"] = [0x4D, 0x5A],
        };
        mutate?.Invoke(entries);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(data);
        }
    }
}

/// <summary>Dossier temporaire supprimé en fin de test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("egr-aps-").FullName;

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
