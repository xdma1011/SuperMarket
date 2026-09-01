using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Code).IsRequired().HasMaxLength(20);
        builder.Property(b => b.PhoneNumber).HasMaxLength(30);
        builder.Property(b => b.RowVersion).IsRowVersion();
        builder.Property(b => b.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(b => b.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(b => b.Code).IsUnique();

        // Address is an EF owned type (value object, no independent identity).
        builder.OwnsOne(b => b.Address, a =>
        {
            a.Property(x => x.Street).HasMaxLength(300).HasColumnName("Address_Street");
            a.Property(x => x.City).HasMaxLength(100).HasColumnName("Address_City");
            a.Property(x => x.PostalCode).HasMaxLength(20).HasColumnName("Address_PostalCode");
            a.Property(x => x.Country).HasMaxLength(100).HasColumnName("Address_Country");
        });
    }
}
