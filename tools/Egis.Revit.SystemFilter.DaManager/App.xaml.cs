using System.Net.Http;
using System.Windows;
using Egis.Revit.SystemFilter.DaManager.Services;
using Egis.Revit.SystemFilter.DaManager.ViewModels;

namespace Egis.Revit.SystemFilter.DaManager.App;

/// <summary>Composition : options lues dans l'environnement, services WPF, ViewModel.</summary>
public partial class App : Application
{
    private HttpClient? _httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        GlobalErrorHandler.Install(this);
        try
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var options = DaManagerOptions.FromEnvironment();
            var window = new MainWindow();
            window.DataContext = new MainViewModel(options, _httpClient, new WpfConfirmationService(window), new WpfFileDialogService());
            window.Show();
        }
        catch (Exception exception)
        {
            // Démarrage impossible : message (masqué) plutôt qu'une fermeture silencieuse.
            GlobalErrorHandler.Show(exception, "Démarrage de DaManager");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
