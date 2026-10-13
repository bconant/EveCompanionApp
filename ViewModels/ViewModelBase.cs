using CommunityToolkit.Mvvm.ComponentModel;

namespace EveCompanionApp.ViewModels;

/// <summary>
/// Common base for all view models. Property change notification comes from
/// <see cref="ObservableObject"/> and the toolkit's source generators.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
