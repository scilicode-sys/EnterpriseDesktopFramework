namespace Framework.Core.Domain;

/// <summary>Entité dont le cycle de vie est horodaté automatiquement par l'Unit of Work.</summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}
