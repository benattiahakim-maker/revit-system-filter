namespace Egis.Revit.SystemFilter.Logging;

/// <summary>
/// Journal structuré (spécifications, section 5) : une ligne JSON par événement sur la console,
/// récupérée via le rapport APS du WorkItem. Toute information d'exécution passe par ce service
/// (aucun dialogue). Ne jamais journaliser tokens, secrets ni URL signées.
/// </summary>
public interface ILogService
{
    /// <summary>Identifiant de corrélation de l'exécution, repris dans chaque événement.</summary>
    string CorrelationId { get; }

    void Log(LogEvent logEvent);

    /// <summary>Vide les tampons ; appelé en fin de traitement et dans le bloc de terminaison.</summary>
    void Flush();
}
