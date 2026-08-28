using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class BranchDocumentSequenceConfiguration : IEntityTypeConfiguration<BranchDocumentSequence>
{
    public void Configure(EntityTypeBuilder<BranchDocumentSequence> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.DocumentType).HasConversion<int>().IsRequired();
        builder.Property(s => s.CurrentValue).HasColumnType("bigint").IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        // The row's natural key. This is the row the atomic
        // `UPDATE ... OUTPUT INSERTED.CurrentValue` reservation locks —
        // terminals at the same branch serialize on it for a few
        // milliseconds; different branches never contend (Architecture
        // Review §4). Tiny table, exact-match lookup only, so this unique
        // index is the only one it needs.
        builder.HasIndex(s => new { s.BranchId, s.DocumentType }).IsUnique();

        // Guards against a counter ever going backwards via a bad manual
        // data fix — cheap, and this table is the single point of failure
        // for invoice-number uniqueness.
        builder.ToTable("BranchDocumentSequences", t => t.HasCheckConstraint(
            "CK_BranchDocumentSequences_CurrentValue_NonNegative",
            "[CurrentValue] >= 0"));
    }
}
