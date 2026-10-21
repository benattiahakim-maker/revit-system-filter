using System.Windows;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.App;

/// <summary>Plan d'écriture à confirmer ; une opération destructive exige de saisir « CONFIRMER ».</summary>
public partial class ConfirmationWindow : Window
{
    public const string DestructiveKeyword = "CONFIRMER";

    private readonly OperationPlan _plan;

    public ConfirmationWindow(OperationPlan plan)
    {
        InitializeComponent();
        _plan = plan;
        TitleText.Text = plan.Title;
        PlanText.Text = plan.Text;
        if (plan.IsDestructive)
        {
            WarningText.Visibility = Visibility.Visible;
            TypedConfirmation.Visibility = Visibility.Visible;
            ConfirmButton.Style = (Style)FindResource("DangerButton");
            ConfirmButton.Content = "Confirmer";
            ConfirmButton.IsEnabled = false;
        }
    }

    private void OnTypedConfirmationChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ConfirmButton.IsEnabled = !_plan.IsDestructive || TypedConfirmation.Text == DestructiveKeyword;

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
