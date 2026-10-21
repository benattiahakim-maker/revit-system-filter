using System.Windows;
using Egis.Revit.SystemFilter.DaManager.Services;
using Microsoft.Win32;

namespace Egis.Revit.SystemFilter.DaManager.App;

/// <summary>Affiche le plan dans une fenêtre modale ; seule une confirmation explicite autorise l'écriture.</summary>
public sealed class WpfConfirmationService(Window owner) : IConfirmationService
{
    public Task<bool> ConfirmAsync(OperationPlan plan)
    {
        var dialog = new ConfirmationWindow(plan) { Owner = owner };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}

public sealed class WpfFileDialogService : IFileDialogService
{
    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName, OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
