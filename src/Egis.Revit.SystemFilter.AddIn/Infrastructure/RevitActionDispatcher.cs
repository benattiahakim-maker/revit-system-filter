using System;
using Autodesk.Revit.UI;

namespace Egis.Revit.SystemFilter.AddIn.Infrastructure
{
    /// <summary>
    /// Fait le pont entre la fenêtre WPF (non modale) et le contexte API de Revit.
    /// Une action postée est exécutée sur le thread principal de Revit via un
    /// <see cref="ExternalEvent"/>, seul endroit où l'API est utilisable.
    /// </summary>
    public sealed class RevitActionDispatcher : IExternalEventHandler
    {
        private readonly ExternalEvent _externalEvent;
        private Action<UIApplication>? _pendingAction;

        public RevitActionDispatcher()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>Poste une action à exécuter dans le prochain contexte API disponible.</summary>
        public void Post(Action<UIApplication> action)
        {
            _pendingAction = action ?? throw new ArgumentNullException(nameof(action));
            _externalEvent.Raise();
        }

        void IExternalEventHandler.Execute(UIApplication application)
        {
            var action = _pendingAction;
            _pendingAction = null;
            action?.Invoke(application);
        }

        string IExternalEventHandler.GetName() => "Egis System Filter - Action Revit";
    }
}
