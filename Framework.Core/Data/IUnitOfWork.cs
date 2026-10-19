using Framework.Core.Domain;

namespace Framework.Core.Data;

/// <summary>
/// Frontière unique d'écriture pour un ensemble de dépôts : un contexte,
/// une transaction, un SaveChanges.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class, IEntity;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exécute <paramref name="action"/> dans une transaction PostgreSQL,
    /// sous la stratégie de résilience EF (compatible EnableRetryOnFailure).
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}
