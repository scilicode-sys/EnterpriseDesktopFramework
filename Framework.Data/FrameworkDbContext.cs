using Framework.Core.Domain;
using Framework.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Data;

/// <summary>
/// Contexte de base du framework : conventions génériques (clé = Id, audit
/// horodatée à chaque SaveChanges, suppression logique transparente) et
/// découverte des IEntityTypeConfiguration de l'assembly du contexte.
/// Un projet dérivé hérite de cette classe et y ajoute ses DbSet.
/// </summary>
public class FrameworkDbContext : DbContext
{
    public DbSet<CompteEntity> Comptes => Set<CompteEntity>();

    /// <summary>Table de démonstration du CRUD : à supprimer avec l'écran « Exemples ».</summary>
    public DbSet<ExempleEntity> Exemples => Set<ExempleEntity>();

    public FrameworkDbContext(DbContextOptions<FrameworkDbContext> options)
        : base(options)
    {
    }

    protected FrameworkDbContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuration découverte dans l'assembly du contexte : un projet
        // dérivé qui met ses entités dans un autre assembly doit rejouer
        // ApplyConfigurationsFromAssembly de son côté dans son override.
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        AppliquerFiltresSuppressionLogique(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            throw new InvalidOperationException(
                "FrameworkDbContext non configuré : passez les options via AddDbContextFactory (DI) ou le constructeur.");
        }
    }

    private static void AppliquerFiltresSuppressionLogique(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletableEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var param = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var isDeleted = System.Linq.Expressions.Expression.Property(
                param, nameof(ISoftDeletableEntity.IsDeleted));
            var filter = System.Linq.Expressions.Expression.Lambda(
                System.Linq.Expressions.Expression.Not(isDeleted), param);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AppliquerConventions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        AppliquerConventions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Horodatage d'audit et conversion des DELETE en suppression logique.</summary>
    private void AppliquerConventions()
    {
        var maintenant = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity is IAuditableEntity ajoutee:
                    ajoutee.CreatedAt = maintenant;
                    break;

                case EntityState.Modified when entry.Entity is IAuditableEntity modifiee:
                    modifiee.UpdatedAt = maintenant;
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDeletableEntity supprimee:
                    entry.State = EntityState.Modified;
                    supprimee.IsDeleted = true;
                    supprimee.DeletedAt = maintenant;
                    break;
            }
        }
    }
}
