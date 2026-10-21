namespace Egis.Revit.SystemFilter.Logging;

/// <summary>Niveaux de log (section 5).</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Événement de log. Le service ajoute <c>timestampUtc</c> et <c>correlationId</c>.</summary>
public sealed record LogEvent(LogLevel Level, string EventCode, string Stage, string Message)
{
    /// <summary>Chemin du document relatif au répertoire d'entrée.</summary>
    public string? DocumentPath { get; init; }

    public string? RoomElementId { get; init; }

    public TimeSpan? Duration { get; init; }

    public Exception? Exception { get; init; }

    /// <summary>Données complémentaires (compteurs, codes) ; jamais de secret.</summary>
    public IReadOnlyDictionary<string, object?>? Data { get; init; }
}
