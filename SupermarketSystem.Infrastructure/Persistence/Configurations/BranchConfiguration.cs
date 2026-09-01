using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fixed ids for the one bootstrap branch/user/role-assignment/branch-assignment
/// seeded so a brand-new database has a working admin login without any
/// manual SQL — see BranchConfiguration, UserConfiguration,
/// UserRoleConfiguration and UserBranchConfiguration. PasswordHash is
/// deliberately NOT seeded here (see UserConfiguration remarks) — no
/// literal password hash belongs in version control.
/// </summary>
public static class BootstrapSeedIds
{
    public static readonly Guid MainBranchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

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

        // Seed: the one bootstrap branch — see BootstrapSeedIds. A fresh
        // database needs at least one branch for the bootstrap admin user
        // (below, in UserConfiguration) to be assigned to; without it
        // there is nothing to log into.
        builder.HasData(new
        {
            Id = BootstrapSeedIds.MainBranchId,
            Name = "Main",
            Code = "MAIN",
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
