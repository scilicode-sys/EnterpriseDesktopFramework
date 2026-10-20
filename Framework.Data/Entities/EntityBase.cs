using Framework.Core.Domain;

namespace Framework.Data.Entities;

/// <summary>
/// Base pratique pour les entités d'un projet dérivé : GUID, audit horodaté et
/// signé, suppression logique sont déjà là. Les conventions du contexte les
/// reconnaît, l'interceptor d'audit les remplit.
/// </summary>
public abstract class EntityBase : IEntity, IAuditableEntity, ISoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }
}
