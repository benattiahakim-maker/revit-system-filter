using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;
using Egis.Revit.SystemFilter.WorkItemRunner.ViewModels;

namespace Egis.Revit.SystemFilter.WorkItemRunner.App;

/// <summary>
/// Vue principale : tout est lié au MainViewModel, sauf les colonnes de l'arbre, générées d'après
/// <see cref="Services.InventoryView.Columns"/> du résultat affiché.
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

        RebuildColumns();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.Result))
        {
            RebuildColumns();
        }
    }

    private void RebuildColumns()
    {
        TreeGrid.ItemsSource = null;
        TreeGrid.Columns.Clear();
        if (DataContext is not MainViewModel { Result: { } result })
        {
            return;
        }

        // Colonne de l'arbre : retrait et flèche de dépliage.
        TreeGrid.Columns.Add(new DataGridTemplateColumn
        {
            CellTemplate = (DataTemplate)FindResource("TreeToggleCell"),
            Width = DataGridLength.Auto,
        });
        for (var index = 0; index < result.Columns.Count; index++)
        {
            TreeGrid.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = result.Columns[index] },
                Binding = new Binding($"[{index}]"),
                Width = index == 0 ? new DataGridLength(260) : DataGridLength.Auto,
            });
        }

        TreeGrid.ItemsSource = TableRow.Visible(result.Tree);
    }

    private void OnToggleRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TableRow row })
        {
            Toggle(row);
        }
    }

    private void OnTreeGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TreeGrid.SelectedItem is TableRow { HasChildren: true } row)
        {
            Toggle(row);
        }
    }

    private void Toggle(TableRow row)
    {
        if (DataContext is MainViewModel { Result: { } result })
        {
            row.IsExpanded = !row.IsExpanded;
            TreeGrid.ItemsSource = TableRow.Visible(result.Tree);
            TreeGrid.SelectedItem = row;
        }
    }
}
