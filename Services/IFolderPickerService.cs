namespace EveCompanionApp.Services;

/// <summary>Keeps the folder dialog out of the view model.</summary>
public interface IFolderPickerService
{
    /// <returns>The chosen folder, or null if the user cancelled.</returns>
    string? PickFolder(string? initialDirectory);
}
