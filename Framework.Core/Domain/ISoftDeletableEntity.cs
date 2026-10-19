namespace Framework.Core.Domain;

/// <summary>
/// Entité effaçable sans perte de données : la suppression logique remplace
/// le DELETE physique (intercepté par l'Unit of Work).
/// </summary>
public interface ISoftDeletableEntity
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}
