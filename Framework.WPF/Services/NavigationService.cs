using Framework.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.WPF.Services;

/// <summary>
/// Navigation par ViewModel : la pile garde les TYPES, l'instance est résolue
/// dans la DI à chaque visite — un ViewModel transitoire repart propre, ses
/// données sont relues, jamais cachées dans un singleton.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly Stack<Type> _pile = new();

    public Type? CurrentViewModelType { get; private set; }

    /// <summary>Instance résolue du ViewModel courant — c'est elle que la vue affiche.</summary>
    public object? ViewModelCourant { get; private set; }

    public bool CanGoBack => _pile.Count > 0;

    public event EventHandler<Type?>? CurrentViewModelChanged;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public void NavigateTo<TViewModel>() where TViewModel : class
        => NavigateTo(typeof(TViewModel));

    public void NavigateTo(Type viewModelType)
    {
        if (CurrentViewModelType == viewModelType)
        {
            return;
        }

        if (CurrentViewModelType is not null)
        {
            _pile.Push(CurrentViewModelType);
        }

        publier(viewModelType);
    }

    public void GoBack()
    {
        if (_pile.Count == 0)
        {
            return;
        }

        publier(_pile.Pop());
    }

    public void Reset(Type viewModelType)
    {
        _pile.Clear();
        publier(viewModelType);
    }

    private void publier(Type viewModelType)
    {
        CurrentViewModelType = viewModelType;
        ViewModelCourant = _services.GetRequiredService(viewModelType);
        CurrentViewModelChanged?.Invoke(this, viewModelType);
    }
}
