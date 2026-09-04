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
        builder.Property(c => c.IsBlocked).IsRequired();
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

public class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> builder)
    {
        builder.ToTable("Complaints");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.IsResolved).IsRequired();
        builder.Property(c => c.ResolvedAtUtc).HasColumnType("datetime2");
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(c => new { c.IsResolved, c.CreatedAtUtc });

        builder.HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupermarketSystem.Domain.Ordering.Order>().WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
