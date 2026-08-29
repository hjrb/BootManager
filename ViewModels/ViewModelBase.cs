using CommunityToolkit.Mvvm.ComponentModel;

namespace BootManager.ViewModels;

/// <summary>
/// Base class for all view models.
/// </summary>
/// <remarks>
/// Derives from <c>ObservableObject</c>, which supplies the change notification mechanism the UI uses
/// to react to property updates. It is empty by design and exists as the single place to add
/// behaviour shared by every view model later on.
/// </remarks>
public abstract class ViewModelBase : ObservableObject
{
}
