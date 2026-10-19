namespace Framework.Core.Domain;

/// <summary>Entité racine persistable, identifiée par un GUID stable.</summary>
public interface IEntity
{
    Guid Id { get; set; }
}
