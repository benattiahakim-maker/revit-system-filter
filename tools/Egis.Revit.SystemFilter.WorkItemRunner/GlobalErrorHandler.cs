using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Egis.Revit.SystemFilter.Aps;

namespace Egis.Revit.SystemFilter.WorkItemRunner.App;

/// <summary>
/// Gestionnaires globaux : une exception non gérée affiche une fenêtre d'erreur (texte masqué par SecretRedactor via
/// <see cref="UnexpectedError"/>) au lieu de fermer l'application sans message.
/// </summary>
internal static class GlobalErrorHandler
{
    private const string Title = "WorkItemRunner : erreur inattendue";

    public static void Install(System.Windows.Application application)
    {
        // Thread de l'interface : l'exception est marquée gérée, la fenêtre reste ouverte.
        application.DispatcherUnhandledException += (_, e) =>
        {
            Show(e.Exception, "Interface");
            e.Handled = true;
        };

        // Tâche dont l'exception n'a jamais été observée : marquée observée, signalée.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Show(e.Exception, "Tâche en arrière-plan");
        };

        // Autre thread : le processus ne peut pas être maintenu, mais l'erreur est affichée avant la fermeture.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Show(e.ExceptionObject as Exception, "Erreur fatale");
    }

    public static void Show(Exception? exception, string context)
    {
        var text = UnexpectedError.Describe(exception, context);
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                MessageBox.Show(text, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                dispatcher.Invoke(() => MessageBox.Show(text, Title, MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
        catch (Exception)
        {
            // Affichage impossible (arrêt en cours) : rien de plus à faire.
        }
    }
}
