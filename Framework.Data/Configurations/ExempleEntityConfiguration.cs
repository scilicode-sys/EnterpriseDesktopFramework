using Framework.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Framework.Data.Configurations;

/// <summary>Mapping de l'entité de référence : nom de table exact, tailles bornées, index de liste.</summary>
public class ExempleEntityConfiguration : IEntityTypeConfiguration<ExempleEntity>
{
    public void Configure(EntityTypeBuilder<ExempleEntity> builder)
    {
        builder.ToTable("Exemples");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Libelle)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Categorie)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Montant)
            .HasPrecision(12, 2);

        builder.Property(e => e.Note)
            .HasMaxLength(512);

        // La liste de l'écran CRUD trie par catégorie puis libellé : l'index suit.
        builder.HasIndex(e => new { e.Categorie, e.Libelle })
            .HasDatabaseName("IX_Exemples_Categorie_Libelle");
    }
}
