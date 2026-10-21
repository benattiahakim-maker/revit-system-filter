using Egis.Revit.SystemFilter.Logging;

namespace Egis.Revit.SystemFilter.Aps;

/// <summary>
/// Texte d'une erreur inattendue pour les gestionnaires globaux des outils (fenêtre d'erreur au lieu d'une fermeture) :
/// type et message de l'exception et de ses causes, masqués par <see cref="SecretRedactor"/>. Pas de pile d'appels.
/// </summary>
public static class UnexpectedError
{
    public static string Describe(Exception? exception, string context)
    {
        var lines = new List<string> { $"{context} : erreur inattendue. L'application reste ouverte ; l'opération en cours a été interrompue." };
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AggregateException { InnerExceptions.Count: > 1 } aggregate)
            {
                lines.AddRange(aggregate.InnerExceptions.Select(e => $"{e.GetType().Name} : {e.Message}"));
                break;
            }

            lines.Add($"{current.GetType().Name} : {current.Message}");
        }

        return SecretRedactor.Redact(string.Join(Environment.NewLine, lines));
    }
}
