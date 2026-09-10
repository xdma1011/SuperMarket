using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name).IsRequired().HasMaxLength(50);
        builder.Property(u => u.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(u => u.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(u => u.Name).IsUnique();
    }
}
