namespace Egis.Revit.SystemFilter.Logging;

public static class LogServiceExtensions
{
    public static void Info(this ILogService log, string eventCode, string stage, string message) =>
        log.Log(new LogEvent(LogLevel.Info, eventCode, stage, message));

    public static void Warning(this ILogService log, string eventCode, string stage, string message) =>
        log.Log(new LogEvent(LogLevel.Warning, eventCode, stage, message));

    public static void Error(this ILogService log, string eventCode, string stage, string message, Exception? exception = null) =>
        log.Log(new LogEvent(LogLevel.Error, eventCode, stage, message) { Exception = exception });
}
