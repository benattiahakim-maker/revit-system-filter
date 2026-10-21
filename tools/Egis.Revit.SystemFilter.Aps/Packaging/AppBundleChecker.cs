using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Egis.Revit.SystemFilter.Aps.Packaging;

/// <summary>
/// Contrôle du ZIP d'AppBundle avant publication : port de <c>check()</c> de <c>src/Packaging/scripts/appbundle.py</c>
/// (mêmes règles et messages). Les deux implémentations doivent évoluer ensemble.
/// </summary>
public static partial class AppBundleChecker
{
    public const string Bundle = "EgisSystemFilter.bundle";
    public const string Contents = Bundle + "/Contents";
    public const string AddinName = "EgisSystemFilter.addin";
    public const string EntryAssembly = "Egis.Revit.SystemFilter.DesignAutomation.dll";
    public const string EntryClass = "Egis.Revit.SystemFilter.DesignAutomation.SystemFilterDbApplication";
    public const string Bridge = "DesignAutomationBridge.dll";

    public static readonly IReadOnlyList<string> AppAssemblies =
    [
        "Egis.Revit.SystemFilter.DesignAutomation.dll",
        "Egis.Revit.SystemFilter.Core.dll",
    ];

    public static readonly IReadOnlyList<string> Required =
    [
        $"{Bundle}/PackageContents.xml",
        $"{Contents}/{AddinName}",
        .. AppAssemblies.Append(Bridge).Select(n => $"{Contents}/{n}"),
    ];

    /// <summary>Symboles facultatifs : uniquement ceux de nos assemblies.</summary>
    public static readonly IReadOnlyList<string> Optional = [.. AppAssemblies.Select(n => $"{Contents}/{n[..^4]}.pdb")];

    /// <summary>Framework attendu de l'assembly d'entrée (build/Revit.props).</summary>
    public static readonly IReadOnlyDictionary<string, string> ExpectedFramework = new Dictionary<string, string>
    {
        ["2026"] = ".NETCoreApp,Version=v8.0",
        ["2027"] = ".NETCoreApp,Version=v10.0",
    };

    private static readonly (Regex Pattern, string Reason)[] Forbidden =
    [
        (RevitApiPattern(), "assembly Revit API (fournie par le moteur)"),
        (AddInUtilityPattern(), "assembly Revit (fournie par le moteur)"),
        (AdWindowsPattern(), "assembly d'interface Revit"),
        (DepsJsonPattern(), "*.deps.json inutile (chargement dans le contexte Revit par défaut)"),
        (RuntimeConfigPattern(), "*.runtimeconfig.json inutile pour un add-in"),
        (SecretFilePattern(), "fichier de configuration ou secret"),
    ];

    public static string Series(string revitVersion) => $"R{revitVersion}";

    public static IReadOnlyList<string> Check(string zipPath, string revitVersion)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [$"ZIP illisible : {exception.Message}"];
        }

        using (archive)
        {
            return Check(archive, revitVersion);
        }
    }

    public static IReadOnlyList<string> Check(ZipArchive archive, string revitVersion)
    {
        if (!ExpectedFramework.TryGetValue(revitVersion, out var framework))
        {
            return [$"version Revit non prise en charge : {revitVersion}"];
        }

        var errors = new List<string>();
        var all = archive.Entries.Select(e => e.FullName).ToList();
        var names = all.Where(n => !n.EndsWith('/')).ToList();
        foreach (var name in names)
        {
            if (name.StartsWith('/') || name.Contains('\\') || name.Split('/').Contains(".."))
            {
                errors.Add($"chemin interdit dans le ZIP : {name}");
            }
        }

        var tops = all.Select(n => n.Split('/', 2)[0]).Distinct().Order(StringComparer.Ordinal).ToList();
        if (tops.Count != 1 || tops[0] != Bundle)
        {
            errors.Add($"un seul dossier racine attendu ({Bundle}), trouvé : [{string.Join(", ", tops.Select(t => $"'{t}'"))}]");
        }

        foreach (var name in names)
        {
            foreach (var (pattern, reason) in Forbidden)
            {
                if (pattern.IsMatch(name))
                {
                    errors.Add($"fichier interdit : {name} ({reason})");
                }
            }
        }

        var allowed = Required.Concat(Optional).ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!allowed.Contains(name) && !Forbidden.Any(f => f.Pattern.IsMatch(name)))
            {
                errors.Add($"fichier non autorisé : {name}");
            }
        }

        foreach (var name in Required)
        {
            if (!names.Contains(name))
            {
                errors.Add($"fichier requis manquant : {name}");
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        errors.AddRange(CheckPackageContents(Read(archive, $"{Bundle}/PackageContents.xml"), revitVersion, names));
        errors.AddRange(CheckAddin(Read(archive, $"{Contents}/{AddinName}"), names));
        if (IndexOf(Read(archive, $"{Contents}/{EntryAssembly}"), Encoding.UTF8.GetBytes(framework)) < 0)
        {
            errors.Add($"{EntryAssembly} : framework attendu {framework} introuvable (mauvaise RevitVersion ?)");
        }

        return errors;
    }

    private static IEnumerable<string> CheckPackageContents(byte[] data, string revitVersion, List<string> names)
    {
        XElement root;
        try
        {
            root = XElement.Parse(Encoding.UTF8.GetString(data));
        }
        catch (System.Xml.XmlException exception)
        {
            return [$"PackageContents.xml invalide : {exception.Message}"];
        }

        var requirements = root.Elements("Components").Elements("RuntimeRequirements").ToList();
        var entries = root.Elements("Components").Elements("ComponentEntry").ToList();
        if (requirements.Count != 1 || entries.Count != 1)
        {
            return ["PackageContents.xml : un seul RuntimeRequirements et un seul ComponentEntry attendus"];
        }

        var errors = new List<string>();
        var requirement = requirements[0];
        var expected = Series(revitVersion);
        if ((string?)requirement.Attribute("Platform") != "Revit" || (string?)requirement.Attribute("OS") != "Win64")
        {
            errors.Add("PackageContents.xml : Platform=Revit et OS=Win64 attendus");
        }

        if ((string?)requirement.Attribute("SeriesMin") != expected || (string?)requirement.Attribute("SeriesMax") != expected)
        {
            errors.Add($"PackageContents.xml : SeriesMin/SeriesMax = {expected} attendus (engine Autodesk.Revit+{revitVersion})");
        }

        var module = (string?)entries[0].Attribute("ModuleName") ?? string.Empty;
        if (module != $"./Contents/{AddinName}" || !names.Contains($"{Bundle}/{module[2..]}"))
        {
            errors.Add($"PackageContents.xml : ModuleName ./Contents/{AddinName} attendu, trouvé '{module}'");
        }

        if (Encoding.UTF8.GetString(data).Contains('@'))
        {
            errors.Add("PackageContents.xml : gabarit non remplacé (@…@)");
        }

        return errors;
    }

    private static IEnumerable<string> CheckAddin(byte[] data, List<string> names)
    {
        XElement root;
        try
        {
            root = XElement.Parse(Encoding.UTF8.GetString(data));
        }
        catch (System.Xml.XmlException exception)
        {
            return [$"{AddinName} invalide : {exception.Message}"];
        }

        var addins = root.Elements("AddIn").ToList();
        if (addins.Count != 1)
        {
            return [$"{AddinName} : un seul AddIn attendu"];
        }

        var errors = new List<string>();
        var addin = addins[0];
        if ((string?)addin.Attribute("Type") != "DBApplication")
        {
            errors.Add($"{AddinName} : Type=DBApplication attendu");
        }

        var assembly = ((string?)addin.Element("Assembly") ?? string.Empty).Trim();
        var relative = assembly.Replace('\\', '/');
        relative = relative.StartsWith("./", StringComparison.Ordinal) ? relative[2..] : relative;
        if (AbsolutePathPattern().IsMatch(relative) || !names.Contains($"{Contents}/{relative}"))
        {
            errors.Add($"{AddinName} : Assembly relative vers {EntryAssembly} attendue, trouvé '{assembly}'");
        }

        if (((string?)addin.Element("FullClassName") ?? string.Empty).Trim() != EntryClass)
        {
            errors.Add($"{AddinName} : FullClassName {EntryClass} attendu");
        }

        if (!GuidPattern().IsMatch(((string?)addin.Element("AddInId") ?? string.Empty).Trim()))
        {
            errors.Add($"{AddinName} : AddInId GUID attendu");
        }

        if (((string?)addin.Element("ManifestSettings")?.Element("UseRevitContext") ?? string.Empty).Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{AddinName} : contexte isolé (UseRevitContext=False) non prévu sans *.deps.json");
        }

        return errors;
    }

    private static byte[] Read(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    [GeneratedRegex(@"(^|/)RevitAPI[^/]*\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex RevitApiPattern();

    [GeneratedRegex(@"(^|/)RevitAddInUtility\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex AddInUtilityPattern();

    [GeneratedRegex(@"(^|/)AdWindows\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex AdWindowsPattern();

    [GeneratedRegex(@"\.deps\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex DepsJsonPattern();

    [GeneratedRegex(@"\.runtimeconfig\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex RuntimeConfigPattern();

    [GeneratedRegex(@"(^|/)(settings\.json|\.env|.*\.(pfx|pem|key))$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretFilePattern();

    [GeneratedRegex(@"^([A-Za-z]:|/)")]
    private static partial Regex AbsolutePathPattern();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    private static partial Regex GuidPattern();
}
