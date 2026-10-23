using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>
    /// Journal court écrit dans le ZIP de sortie (<c>run.log</c>). Toute ligne est nettoyée :
    /// chemins absolus (Windows, UNC, Unix) et valeurs ressemblant à des secrets sont masqués.
    /// </summary>
    public sealed class RunLog
    {
        public const string FileName = "run.log";
        public const int MaxLines = 500;

        private static readonly Regex WindowsPath = new Regex(@"(?i)\b[a-z]:[\\/][^\s""'<>|]*", RegexOptions.Compiled);
        private static readonly Regex UncPath = new Regex(@"\\\\[^\s""'<>|]+", RegexOptions.Compiled);
        private static readonly Regex UnixPath = new Regex(@"(?<![\w.])/(?:[\w.\-]+/)+[\w.\-]*", RegexOptions.Compiled);
        private static readonly Regex Secret = new Regex(@"(?i)(bearer\s+[\w\-.~+/]+=*|(token|secret|password|signature|sig|x-amz-[\w-]+)\s*[=:]\s*[^\s&""']+|https?://\S+)", RegexOptions.Compiled);

        private readonly List<string> _lines = new List<string>();
        public IReadOnlyList<string> Lines => _lines;
        public int Warnings { get; private set; }
        public int Errors { get; private set; }

        public void Info(string message) => Add("INFO", message);
        public void Warn(string message) { Warnings++; Add("WARN", message); }
        public void Error(string message) { Errors++; Add("ERROR", message); }

        private void Add(string level, string message)
        {
            if (_lines.Count >= MaxLines) return;
            _lines.Add($"{level} {Sanitize(message)}");
            if (_lines.Count == MaxLines) _lines.Add("WARN journal tronqué");
        }

        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var s = text.Replace("\r", " ").Replace("\n", " ");
            s = Secret.Replace(s, "<masqué>");
            s = UncPath.Replace(s, "<chemin>");
            s = WindowsPath.Replace(s, "<chemin>");
            s = UnixPath.Replace(s, "<chemin>");
            return s.Length > 400 ? s.Substring(0, 400) : s;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var l in _lines) sb.Append(l).Append('\n');
            return sb.ToString();
        }
    }
}
