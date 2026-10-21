using System.Net.Http;
using System.Windows;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;
using Egis.Revit.SystemFilter.WorkItemRunner.ViewModels;

namespace Egis.Revit.SystemFilter.WorkItemRunner.App;

/// <summary>Composition : options lues dans l'environnement, services WPF, ViewModel.</summary>
public partial class App : System.Windows.Application
{
    private HttpClient? _httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        GlobalErrorHandler.Install(this);
        try
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            var window = new MainWindow();
            var viewModel = new MainViewModel(RunnerOptions.FromEnvironment(), _httpClient, new WpfConfirmationService(window), new WpfFileDialogService());
            window.DataContext = viewModel;
            window.Show();
            viewModel.Initialize(); // historique local seulement : aucun client APS au démarrage
        }
        catch (Exception exception)
        {
            // Démarrage impossible : message (masqué) plutôt qu'une fermeture silencieuse.
            GlobalErrorHandler.Show(exception, "Démarrage de WorkItemRunner");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
