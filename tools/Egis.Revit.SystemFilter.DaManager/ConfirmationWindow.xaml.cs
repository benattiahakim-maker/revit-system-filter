using System.Windows;
using Egis.Revit.SystemFilter.DaManager.Services;

namespace Egis.Revit.SystemFilter.DaManager.App;

/// <summary>Plan d'écriture à confirmer ; une suppression exige de saisir « SUPPRIMER ».</summary>
public partial class ConfirmationWindow : Window
{
    public const string DestructiveKeyword = "SUPPRIMER";

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
            ConfirmButton.Content = "Supprimer";
            ConfirmButton.IsEnabled = false;
        }
    }

    private void OnTypedConfirmationChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ConfirmButton.IsEnabled = !_plan.IsDestructive || TypedConfirmation.Text == DestructiveKeyword;

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
