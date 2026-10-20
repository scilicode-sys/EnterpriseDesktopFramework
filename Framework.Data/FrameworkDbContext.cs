using Framework.Core.Domain;
using Framework.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Data;

/// <summary>
/// Contexte de base du framework : conventions génériques de modèle (clé = Id,
/// colonnes d'audit bornées, suppression logique transparente) et découverte
/// des IEntityTypeConfiguration de l'assembly du contexte. L'écriture de l'audit
/// n'est pas ici : elle appartient à <see cref="AuditSaveChangesInterceptor"/>.
/// Un projet dérivé hérite de cette classe et y ajoute ses DbSet.
/// </summary>
public class FrameworkDbContext : DbContext
{
    /// <summary>Taille d'un identifiant de connexion tracé : la colonne de « qui ».</summary>
    public const int TailleColonneAuteur = 64;

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
        BornersColonnesAuteur(modelBuilder);
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

    /// <summary>
    /// Les colonnes d'auteur existent par la convention, pas par la déclaration :
    /// une entité d'un produit dérivé qui hérite d'EntityBase les reçoit déjà
    /// bornées, sans avoir à répéter trois lignes de configuration.
    /// </summary>
    private static void BornersColonnesAuteur(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var entityBuilder = modelBuilder.Entity(entityType.ClrType);

            foreach (var propriete in new[]
                     {
                         nameof(IAuditableEntity.CreatedBy),
                         nameof(IAuditableEntity.UpdatedBy),
                         nameof(ISoftDeletableEntity.DeletedBy),
                     })
            {
                if (entityType.FindProperty(propriete) is not null)
                {
                    entityBuilder.Property(propriete).HasMaxLength(TailleColonneAuteur);
                }
            }
        }
    }
}
