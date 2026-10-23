using System.Text;
using System.Text.Json;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>Résultat d'un contrôle local : erreurs bloquantes et avertissements (affichés dans le plan).</summary>
public sealed record CheckResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Contrôles locaux avant soumission : ils évitent de payer un WorkItem voué à l'échec. L'entrée est une maquette
/// Revit (<c>.rvt</c>) et, optionnellement, un <c>settings.json</c>.
/// </summary>
public static class InputChecks
{
    /// <summary>Taille maximale d'un fichier envoyé (au-delà, préférer un transfert dédié).</summary>
    public const long MaxModelBytes = 5L * 1024 * 1024 * 1024;

    public static CheckResult CheckModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return new CheckResult(["Maquette Revit non renseignée."], []);
        }

        if (!File.Exists(modelPath))
        {
            return new CheckResult([$"Maquette introuvable : {Path.GetFileName(modelPath)}"], []);
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        if (!modelPath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Le fichier ne porte pas l'extension .rvt.");
        }

        if (new FileInfo(modelPath).Length > MaxModelBytes)
        {
            errors.Add("Maquette trop volumineuse (5 Gio au plus).");
        }

        return new CheckResult(errors, warnings);
    }

    /// <summary>
    /// <c>settings.json</c> est facultatif : vide = valide (tous les réseaux). Non vide, il doit être un objet JSON.
    /// </summary>
    public static CheckResult CheckOptions(string optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return new CheckResult([], []);
        }

        if (Encoding.UTF8.GetByteCount(optionsJson) > 1024 * 1024)
        {
            return new CheckResult(["settings.json dépasse 1 Mo."], []);
        }

        try
        {
            using var document = JsonDocument.Parse(optionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new CheckResult(["settings.json : objet JSON attendu."], []);
            }
        }
        catch (JsonException exception)
        {
            return new CheckResult([$"settings.json illisible : {exception.Message}"], []);
        }

        return new CheckResult([], []);
    }
}
