using System;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io
{
    /// <summary>
    /// Entrée refusée (ZIP, settings.json ou modèle principal). Le message ne contient
    /// jamais de chemin absolu : il peut être écrit tel quel dans run.log.
    /// </summary>
    public sealed class InputArchiveException : Exception
    {
        public InputArchiveException(string message) : base(message) { }
    }
}
