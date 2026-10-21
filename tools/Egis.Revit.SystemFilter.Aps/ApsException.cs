using System.Net;
using Egis.Revit.SystemFilter.Logging;

namespace Egis.Revit.SystemFilter.Aps;

/// <summary>
/// Échec d'un appel APS (équivalent de <c>DeployError</c> dans <c>deploy.py</c>). Le message ne contient jamais de jeton
/// ni d'URL signée : la requête est désignée par un libellé et la réponse est tronquée puis masquée.
/// </summary>
public sealed class ApsException : Exception
{
    public ApsException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(SecretRedactor.Redact(message), innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Statut HTTP reçu, ou null si l'erreur n'est pas une réponse HTTP inattendue.</summary>
    public HttpStatusCode? StatusCode { get; }
}
