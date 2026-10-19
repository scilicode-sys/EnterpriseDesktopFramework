using System.Collections.Concurrent;
using Framework.Core.Data;
using Framework.Core.Domain;
using Framework.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Framework.Data;

/// <summary>
/// Unit of Work générique : un contexte privé créé par la fabrique, des dépôts
/// à la demande, une frontière d'écriture unique (SaveChanges ou transaction).
/// Réservé Transient dans la DI : jamais deux écrans ne partagent le même contexte.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly FrameworkDbContext _context;
    private readonly ConcurrentDictionary<Type, object> _repositories = new();
    private bool _disposed;

    public UnitOfWork(IDbContextFactory<FrameworkDbContext> contextFactory)
    {
        _context = contextFactory.CreateDbContext();
    }

    public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class, IEntity
    {
        if (_repositories.TryGetValue(typeof(TEntity), out var existant))
        {
            return (IGenericRepository<TEntity>)existant;
        }

        var depot = new GenericRepository<TEntity>(_context);
        _repositories[typeof(TEntity)] = depot;
        return depot;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        // CreateExecutionStrategy rend la transaction compatible avec
        // EnableRetryOnFailure : sans lui, Npgsql lève une InvalidOperationException.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _context.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _context.DisposeAsync();
        _disposed = true;
    }
}
