using System.Text.Json;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>Dernière configuration de lancement, rappelée au démarrage (ni secret, ni identifiant APS, ni URL signée).</summary>
public sealed record LastConfig(string? RevitVersion, string? Alias, string? ModelPath, string? OptionsJson);

/// <summary>
/// Mémorise la dernière configuration (version Revit, alias, ZIP, contenu de settings.json) dans le dossier de données
/// de l'outil. Lecture et écriture tolérantes : un fichier absent, illisible ou non inscriptible n'empêche jamais
/// l'application de fonctionner.
/// </summary>
public sealed class LastConfigStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string FilePath => Path.Combine(dataDirectory, "last-config.json");

    public LastConfig? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<LastConfig>(File.ReadAllText(FilePath), Options) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(LastConfig config)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(config, Options));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Mémorisation facultative.
        }
    }
}
