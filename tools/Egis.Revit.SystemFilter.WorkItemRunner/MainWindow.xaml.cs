using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Egis.Revit.SystemFilter.WorkItemRunner.ViewModels;

namespace Egis.Revit.SystemFilter.WorkItemRunner.App;

/// <summary>
/// Vue principale : tout est lié au MainViewModel, sauf les colonnes du tableau des pièces, générées d'après les clés
/// de <c>systems[]</c> du résultat affiché (<see cref="Services.ResultView.SystemColumns"/>).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged previous)
        {
            previous.PropertyChanged -= OnViewModelChanged;
        }

        if (e.NewValue is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnViewModelChanged;
        }

        RebuildSystemColumns();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.Result))
        {
            RebuildSystemColumns();
        }
    }

    private void RebuildSystemColumns()
    {
        SystemsGrid.ItemsSource = null;
        SystemsGrid.Columns.Clear();
        if (DataContext is not MainViewModel { Result: { } result })
        {
            return;
        }

        for (var index = 0; index < result.SystemColumns.Count; index++)
        {
            // L'en-tête est le chemin exact de la clé dans le JSON (TextBlock : le « _ » n'est pas une touche d'accès).
            SystemsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = result.SystemColumns[index] },
                Binding = new Binding($"[{index}]"),
                Width = DataGridLength.Auto,
            });
        }

        SystemsGrid.ItemsSource = result.SystemValues;
    }
}
