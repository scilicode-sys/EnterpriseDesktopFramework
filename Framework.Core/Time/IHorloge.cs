namespace Framework.Core.Time;

/// Horloge injectable : toute logique dépendante du temps passe par elle,
/// pour rester testable en décalant l'heure plutôt qu'en dormant.
public interface IHorloge
{
    DateTime Maintenant { get; }
}
