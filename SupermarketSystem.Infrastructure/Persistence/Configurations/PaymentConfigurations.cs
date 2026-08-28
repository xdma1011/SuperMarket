using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Payments;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    // Fixed seed ids/timestamp: HasData requires stable, deterministic
    // values so the generated migration is reproducible.
    public static readonly Guid CashId = Guid.Parse("4fa2a3fd-dd6d-4207-a330-c6b33af0c8bf");
    public static readonly Guid VisaId = Guid.Parse("637959dc-3e36-44d2-906f-db46992911e5");
    public static readonly Guid CliqId = Guid.Parse("f6c90807-33bd-4018-b7a3-9d0f4f70a553");
    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        builder.HasKey(pm => pm.Id);

        builder.Property(pm => pm.Code).IsRequired().HasMaxLength(20);
        builder.Property(pm => pm.Name).IsRequired().HasMaxLength(100);
        builder.Property(pm => pm.RowVersion).IsRowVersion();
        builder.Property(pm => pm.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(pm => pm.UpdatedAtUtc).HasColumnType("datetime2");

        // This table will have a handful of rows — the unique Code index is
        // the only index it needs (Architecture Review §16.13).
        builder.HasIndex(pm => pm.Code).IsUnique();

        // Seed: Cash / Visa / CliQ, per Architecture Review §16.14. Codes
        // are stable and independent of localized display Name.
        builder.HasData(
            new
            {
                Id = CashId,
                Code = "CASH",
                Name = "Cash",
                AffectsCashDrawer = true,
                RequiresExternalReference = false,
                SortOrder = 1,
                IsSystemDefined = true,
                IsActive = true,
                CreatedAtUtc = SeedTimestamp
            },
            new
            {
                Id = VisaId,
                Code = "VISA",
                Name = "Visa",
                AffectsCashDrawer = false,
                RequiresExternalReference = true,
                SortOrder = 2,
                IsSystemDefined = true,
                IsActive = true,
                CreatedAtUtc = SeedTimestamp
            },
            new
            {
                Id = CliqId,
                Code = "CLIQ",
                Name = "CliQ",
                AffectsCashDrawer = false,
                RequiresExternalReference = true,
                SortOrder = 3,
                IsSystemDefined = true,
                IsActive = true,
                CreatedAtUtc = SeedTimestamp
            });
    }
}
