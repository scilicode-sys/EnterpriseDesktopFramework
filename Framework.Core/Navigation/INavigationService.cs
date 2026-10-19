namespace Framework.Core.Navigation;

/// <summary>
/// Navigation par ViewModel : la couche présentation résout le View via un
/// DataTemplate implicite. Aucune référence WPF ici.
/// </summary>
public interface INavigationService
{
    Type? CurrentViewModelType { get; }

    bool CanGoBack { get; }

    void NavigateTo<TViewModel>() where TViewModel : class;

    void NavigateTo(Type viewModelType);

    void GoBack();

    /// <summary>Vide la pile et repart sur <paramref name="viewModelType"/>.</summary>
    void Reset(Type viewModelType);

    event EventHandler<Type?>? CurrentViewModelChanged;
}
