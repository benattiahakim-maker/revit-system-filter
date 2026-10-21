using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Egis.Revit.SystemFilter.Logging;

/// <summary>
/// Journal JSON Lines sur la console (section 5) : une ligne JSON par événement, récupérée via le rapport
/// APS du WorkItem. Aucun fichier de log (décision D8). Secrets masqués par <see cref="SecretRedactor"/>.
/// Les événements liés à une pièce sont limités par code ; le surplus est compté et résumé au flush.
/// </summary>
public sealed class JsonConsoleLogService : ILogService, IDisposable
{
    /// <summary>Nombre maximal d'événements détaillés par code pour les événements de pièce.</summary>
    public const int DefaultRoomEventLimit = 20;

    public const string SuppressedEventCode = "LOG_EVENTS_SUPPRESSED";

    private const int MaxExceptionDepth = 5;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Encodeur par défaut : lignes ASCII pures (accents en \uXXXX), sûres pour le rapport APS.
        Encoder = JavaScriptEncoder.Default,
        Indented = false,
    };

    private readonly object _sync = new();
    private readonly TextWriter _writer;
    private readonly TimeProvider _time;
    private readonly int _roomEventLimit;
    private readonly Dictionary<string, int> _roomEventCounts = new(StringComparer.Ordinal);
    private bool _disposed;

    public JsonConsoleLogService(string correlationId, TextWriter? writer = null, TimeProvider? time = null, int roomEventLimit = DefaultRoomEventLimit)
    {
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? "unknown" : correlationId;
        _writer = writer ?? Console.Out;
        _time = time ?? TimeProvider.System;
        _roomEventLimit = Math.Max(0, roomEventLimit);
    }

    public string CorrelationId { get; }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        lock (_sync)
        {
            if (_disposed || IsSuppressed(logEvent))
            {
                return;
            }

            WriteLine(Serialize(logEvent));
        }
    }

    /// <summary>Résume les événements supprimés puis vide le flux. Appelé en fin de traitement et en terminaison.</summary>
    public void Flush()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            WriteSuppressedSummary();
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        Flush();
        lock (_sync)
        {
            _disposed = true;
        }
    }

    private bool IsSuppressed(LogEvent logEvent)
    {
        if (logEvent.RoomElementId is null)
        {
            return false;
        }

        _roomEventCounts.TryGetValue(logEvent.EventCode, out var count);
        _roomEventCounts[logEvent.EventCode] = count + 1;
        return count >= _roomEventLimit;
    }

    private void WriteSuppressedSummary()
    {
        var suppressed = _roomEventCounts
            .Where(kv => kv.Value > _roomEventLimit)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => (object?)(kv.Value - _roomEventLimit), StringComparer.Ordinal);
        if (suppressed.Count == 0)
        {
            return;
        }

        var total = suppressed.Values.Sum(v => (int)v!);
        WriteLine(Serialize(new LogEvent(LogLevel.Warning, SuppressedEventCode, "logging", $"{total} événement(s) de pièce non détaillé(s) au-delà de {_roomEventLimit} par code.")
        {
            Data = suppressed,
        }));

        foreach (var key in suppressed.Keys)
        {
            _roomEventCounts[key] = _roomEventLimit;
        }
    }

    private void WriteLine(string line)
    {
        _writer.Write(line);
        _writer.Write('\n');
    }

    private string Serialize(LogEvent logEvent)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("timestampUtc", _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            json.WriteString("level", logEvent.Level.ToString());
            json.WriteString("eventCode", logEvent.EventCode);
            json.WriteString("correlationId", CorrelationId);
            json.WriteString("stage", logEvent.Stage);
            json.WriteString("message", SecretRedactor.Redact(logEvent.Message));
            if (logEvent.DocumentPath is not null)
            {
                json.WriteString("documentPath", SecretRedactor.Redact(logEvent.DocumentPath));
            }

            if (logEvent.RoomElementId is not null)
            {
                json.WriteString("roomElementId", logEvent.RoomElementId);
            }

            if (logEvent.Duration is { } duration)
            {
                json.WriteNumber("durationMs", Math.Round(duration.TotalMilliseconds, 3));
            }

            if (logEvent.Data is { Count: > 0 } data)
            {
                json.WritePropertyName("data");
                WriteDictionary(json, data.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value)), depth: 0);
            }

            if (logEvent.Exception is not null)
            {
                json.WritePropertyName("exception");
                WriteException(json, logEvent.Exception, depth: 0);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void WriteException(Utf8JsonWriter json, Exception exception, int depth)
    {
        json.WriteStartObject();
        json.WriteString("type", exception.GetType().FullName);
        json.WriteString("message", SecretRedactor.Redact(exception.Message));
        if (exception.StackTrace is not null)
        {
            json.WriteString("stackTrace", SecretRedactor.Redact(exception.StackTrace));
        }

        if (exception.InnerException is not null && depth < MaxExceptionDepth)
        {
            json.WritePropertyName("inner");
            WriteException(json, exception.InnerException, depth + 1);
        }

        json.WriteEndObject();
    }

    private static void WriteDictionary(Utf8JsonWriter json, IEnumerable<KeyValuePair<string, object?>> entries, int depth)
    {
        json.WriteStartObject();
        foreach (var (key, value) in entries)
        {
            json.WritePropertyName(key);
            if (SecretRedactor.IsSensitiveKey(key))
            {
                json.WriteStringValue(SecretRedactor.Mask);
            }
            else
            {
                WriteValue(json, value, depth + 1);
            }
        }

        json.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter json, object? value, int depth)
    {
        if (depth > MaxExceptionDepth)
        {
            json.WriteStringValue("…");
            return;
        }

        switch (value)
        {
            case null:
                json.WriteNullValue();
                break;
            case string s:
                json.WriteStringValue(SecretRedactor.Redact(s));
                break;
            case bool b:
                json.WriteBooleanValue(b);
                break;
            case int or long or short or byte or sbyte or ushort or uint:
                json.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong ul:
                json.WriteNumberValue(ul);
                break;
            case double d when double.IsFinite(d):
                json.WriteNumberValue(d);
                break;
            case float f when float.IsFinite(f):
                json.WriteNumberValue(f);
                break;
            case decimal m:
                json.WriteNumberValue(m);
                break;
            case TimeSpan ts:
                json.WriteNumberValue(Math.Round(ts.TotalMilliseconds, 3));
                break;
            case DateTime dt:
                json.WriteStringValue(dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                json.WriteStringValue(dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case Enum e:
                json.WriteStringValue(e.ToString());
                break;
            case IDictionary dictionary:
                WriteDictionary(
                    json,
                    dictionary.Keys.Cast<object>().Select(k => new KeyValuePair<string, object?>(Convert.ToString(k, CultureInfo.InvariantCulture) ?? string.Empty, dictionary[k])),
                    depth);
                break;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                WriteDictionary(json, pairs, depth);
                break;
            case IEnumerable sequence:
                json.WriteStartArray();
                foreach (var item in sequence)
                {
                    WriteValue(json, item, depth + 1);
                }

                json.WriteEndArray();
                break;
            default:
                json.WriteStringValue(SecretRedactor.Redact(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
                break;
        }
    }
}
