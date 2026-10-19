using System.Linq.Expressions;
using Framework.Core.Data;
using Framework.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Framework.Data.Repositories;

/// <summary>
/// Dépôt EF Core branché sur un DbSet. Aucune sauvegarde ici : l'Unit of Work
/// décide du moment du SaveChanges, la requête reste exprimable via Query().
/// </summary>
public class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : class, IEntity
{
    protected readonly DbContext Context;
    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    public GenericRepository(DbContext context)
    {
        Context = context;
    }

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => await Set.ToListAsync(cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await Set.Where(predicate).ToListAsync(cancellationToken);

    public virtual IQueryable<TEntity> Query() => Set.AsNoTracking();

    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await Set.AddAsync(entity, cancellationToken);

    public virtual void Update(TEntity entity) => Set.Update(entity);

    public virtual void Remove(TEntity entity) => Set.Remove(entity);
}
