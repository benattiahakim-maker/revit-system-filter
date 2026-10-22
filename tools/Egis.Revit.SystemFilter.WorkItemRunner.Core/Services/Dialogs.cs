namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>
/// Plan d'une écriture APS ou d'une soumission de WorkItem (consommatrice de crédits), affiché avant confirmation.
/// Il ne contient ni secret ni URL signée.
/// </summary>
public sealed record OperationPlan(string Title, IReadOnlyList<string> Steps, bool IsDestructive = false)
{
    public string Text => string.Join(Environment.NewLine, Steps);
}

/// <summary>Demande à l'utilisateur de confirmer un plan. Aucune écriture n'a lieu sans réponse positive.</summary>
public interface IConfirmationService
{
    Task<bool> ConfirmAsync(OperationPlan plan);
}

/// <summary>Sélection de fichiers (implémentée par la vue).</summary>
public interface IFileDialogService
{
    string? OpenFile(string title, string filter, string? initialDirectory);

    string? SaveFile(string title, string filter, string defaultFileName);
}
