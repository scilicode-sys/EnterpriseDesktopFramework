using Framework.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Framework.Data.Configurations;

public class CompteEntityConfiguration : IEntityTypeConfiguration<CompteEntity>
{
    public void Configure(EntityTypeBuilder<CompteEntity> builder)
    {
        builder.ToTable("Comptes");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Username)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(c => c.Username)
            .IsUnique()
            .HasDatabaseName("IX_Comptes_Username");

        builder.Property(c => c.NomComplet)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(c => c.Role)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(c => c.PasswordHash)
            .HasMaxLength(256)
            .IsRequired();
    }
}
