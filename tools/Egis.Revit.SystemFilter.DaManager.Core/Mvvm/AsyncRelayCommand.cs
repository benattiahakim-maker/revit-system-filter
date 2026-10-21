using System.Windows.Input;

namespace Egis.Revit.SystemFilter.DaManager.Mvvm;

/// <summary>
/// Commande asynchrone : désactivée pendant son exécution et tant que <c>canExecute</c> est faux. Les exceptions sont
/// confiées au ViewModel (<c>execute</c> doit les traiter) ; <see cref="ExecuteAsync"/> sert aux tests.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
