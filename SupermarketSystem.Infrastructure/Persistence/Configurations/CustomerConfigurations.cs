using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FullName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        // POS lookup by phone — not assumed unique (Architecture Review §13/§14).
        builder.HasIndex(c => c.Phone);
        // Drives the derived purchase-history query (CustomerPurchaseHistory
        // was removed as a stored entity — this index is what keeps that
        // query fast at volume). Also referenced from SaleInvoices(CustomerId, CreatedAtUtc).
        builder.HasIndex(c => c.CreatedAtUtc);

        builder.HasMany(c => c.Notes).WithOne().HasForeignKey(n => n.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Notes).HasField("_notes").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> builder)
    {
        builder.ToTable("CustomerNotes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Text).IsRequired().HasMaxLength(2000);
    }
}
