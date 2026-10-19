using System.Linq.Expressions;
using Framework.Core.Domain;

namespace Framework.Core.Data;

/// <summary>
/// Dépôt générique pour toute entité persistable. Aucune écriture n'est
/// sauvegardée ici : c'est l'Unit of Work qui décide du moment du SaveChanges.
/// </summary>
public interface IGenericRepository<TEntity> where TEntity : class, IEntity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    IQueryable<TEntity> Query();

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}
