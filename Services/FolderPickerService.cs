using System.IO;
using Microsoft.Win32;

namespace EveCompanionApp.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = "Select your EVE Gamelogs folder" };
        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
