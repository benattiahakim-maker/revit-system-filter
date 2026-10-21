namespace Egis.Revit.SystemFilter.DaManager.Services;

/// <summary>
/// Commit courant du dépôt, lu dans <c>.git</c> sans exécuter git. Affiché dans les plans : ZIP et définitions doivent venir
/// du même commit (docs/PUBLISHING.md sections 3 et 7).
/// </summary>
public static class GitHead
{
    public static string? Read(string? repositoryRoot)
    {
        try
        {
            if (repositoryRoot is null)
            {
                return null;
            }

            var gitDir = Path.Combine(repositoryRoot, ".git");
            var headFile = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headFile))
            {
                return null;
            }

            var head = File.ReadAllText(headFile).Trim();
            if (!head.StartsWith("ref: ", StringComparison.Ordinal))
            {
                return Short(head);
            }

            var reference = head[5..];
            var refFile = Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(refFile))
            {
                return $"{Short(File.ReadAllText(refFile).Trim())} ({reference.Replace("refs/heads/", string.Empty, StringComparison.Ordinal)})";
            }

            var packed = Path.Combine(gitDir, "packed-refs");
            var line = File.Exists(packed) ? File.ReadLines(packed).FirstOrDefault(l => l.EndsWith(" " + reference, StringComparison.Ordinal)) : null;
            return line is null ? null : $"{Short(line.Split(' ')[0])} ({reference.Replace("refs/heads/", string.Empty, StringComparison.Ordinal)})";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null; // commit inconnu : jamais bloquant
        }
    }

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;
}
