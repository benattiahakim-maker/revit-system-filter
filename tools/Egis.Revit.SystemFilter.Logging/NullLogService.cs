namespace Egis.Revit.SystemFilter.Logging;

/// <summary>Implémentation sans effet, pour les tests. En production : <see cref="JsonConsoleLogService"/>.</summary>
public sealed class NullLogService(string correlationId = "none") : ILogService
{
    public string CorrelationId { get; } = correlationId;

    public void Log(LogEvent logEvent)
    {
    }

    public void Flush()
    {
    }
}
