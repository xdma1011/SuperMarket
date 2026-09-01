using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Username).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);

        builder.Property(u => u.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(u => u.UpdatedAtUtc).HasColumnType("datetime2");
        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique();

        // Seed the well-known System user (see User.SystemUserId remarks) —
        // reference/attribution data, permitted under the same rule
        // PaymentMethod's seed relies on. IsActive = false: this row is
        // never meant to be selectable as a real login, only referenced by
        // id as a fallback actor until real authentication exists.
        builder.HasData(new
        {
            Id = User.SystemUserId,
            FullName = "System",
            Username = "system",
            Email = "system@local.invalid",
            IsActive = false,
            IsDeleted = false,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Seed: the one bootstrap admin account (see BootstrapSeedIds) —
        // a fresh database gets a real, active, loggable-in admin user
        // automatically, tied to the Master Admin role and the Main
        // branch (UserRoleConfiguration / UserBranchConfiguration below),
        // instead of everyone having to hand-write the same
        // INSERT/UPDATE SQL after every fresh database.
        //
        // PasswordHash is deliberately left unset (null) here — a literal
        // password hash has no business sitting in version control (this
        // repo is on GitHub), and this migration-generation environment
        // has no way to produce one it could verify actually round-trips
        // through AspNetPasswordHasher correctly. One-time manual step
        // after the database is created: run `tools/HashPassword`, then
        //   UPDATE Users SET PasswordHash = N'<paste>' WHERE Username = N'admin';
        builder.HasData(new
        {
            Id = BootstrapSeedIds.AdminUserId,
            FullName = "Admin",
            Username = "admin",
            Email = "admin@local.invalid",
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.Property(r => r.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(r => r.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(r => r.Name).IsUnique();

        // RolePermission is owned/managed within the Role aggregate
        // (Role.GrantPermission/RevokePermission) -> Cascade.
        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Permissions).HasField("_permissions").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Seed: دور "Master Admin" — كل الصلاحيات مربوطة فيه (راجع
        // RolePermissionConfiguration للربط الفعلي). للدعم/الصيانة، لا
        // bypass بالكود لأي فحص صلاحية — نفس الآلية العادية بالضبط، بس
        // بدور جامع لكل شي. كل عملية يسوّيها مستخدم بهذا الدور تنسجل
        // بالتدقيق العادي، بعكس أي "تجاوز" مخفي بالكود.
        builder.HasData(new
        {
            Id = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            Name = "Master Admin",
            Description = "كل الصلاحيات — للدعم والصيانة والإدارة الكاملة.",
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Seed: دور "كاشير" — شغل البيع اليومي بس (راجع
        // PermissionCodes.CashierDefaults للصلاحيات المربوطة فعليًا).
        builder.HasData(new
        {
            Id = Guid.Parse("f3b401c7-84f6-4a0f-9f17-b689979c5d8c"),
            Name = "كاشير",
            Description = "بيع، إلغاء بيع، معالجة إرجاع — بلا أي صلاحية إدارية.",
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Seed: دور "مساعد أدمن" — كل العمليات اليومية والإدارية العادية،
        // بلا الصلاحيات الأخطر (راجع PermissionCodes.AssistantAdminDefaults).
        builder.HasData(new
        {
            Id = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            Name = "مساعد أدمن",
            Description = "إدارة يومية كاملة (مبيعات، مشتريات، كتالوج، جرد، تقارير) بلا النسخ الاحتياطي أو إدارة الجلسات/الفروع/المستخدمين.",
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(p => p.Code).IsUnique();

        // Seed: كل رموز الصلاحيات المعرَّفة بـPermissionCodes، بمعرّفات ثابتة.
        builder.HasData(new
        {
            Id = Guid.Parse("f2f8a36f-f4d1-4f2b-a1ba-18be8c023f34"),
            Code = "System.CrossBranchAccess",
            Name = "Cross-branch access",
            Description = "Bypasses the branch isolation query filter entirely. Grant only to head-office/support roles.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("5f14a6c3-3b89-4512-9799-3b25ecdefb40"),
            Code = "Sales.Create",
            Name = "Complete sales",
            Description = "Complete a sale at the register.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("3d939df0-3319-4dc7-ad83-cd0567607e8a"),
            Code = "Sales.Void",
            Name = "Void sales",
            Description = "Void a completed sale, reversing stock, payments, and drawer entries.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("92cfde3f-6a23-48a9-ada8-27adb926af76"),
            Code = "Returns.Process",
            Name = "Process returns",
            Description = "Process a customer return.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("69c92d8d-cf96-4a66-b1b4-b8149ab8f0ca"),
            Code = "Returns.Review",
            Name = "Mark returns reviewed",
            Description = "Mark a return as administratively reviewed.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("81776805-df39-4a6a-a395-60df218bf010"),
            Code = "Purchasing.Create",
            Name = "Record purchases",
            Description = "Record a purchase invoice, including AI-assisted image extraction.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("4da27a4a-a381-4225-b7da-9ad63fc3c963"),
            Code = "Catalog.Manage",
            Name = "Manage catalog",
            Description = "Create/edit products, categories, units, and barcodes.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("56e2faed-431e-464c-a808-5f1bd84046c5"),
            Code = "Suppliers.Manage",
            Name = "Manage suppliers",
            Description = "Create/edit suppliers.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("f29bb266-8361-4ed2-aee4-4926ebe4f021"),
            Code = "Branches.Manage",
            Name = "Manage branches",
            Description = "Create/edit branches.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("82ebffce-eba3-4dc3-9fea-f9b0ff5d058a"),
            Code = "Stocktake.Manage",
            Name = "Manage stocktakes",
            Description = "Create stocktakes, record counts, complete counting.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("bc142cda-a285-4e26-9f90-64208cc270fa"),
            Code = "Stocktake.Approve",
            Name = "Approve stocktakes",
            Description = "Approve a completed stocktake, applying corrections to stock.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("a7fc0954-e9d6-4c47-af8a-4620d9faf6f0"),
            Code = "CashClosing.Manage",
            Name = "Manage cash closings",
            Description = "Complete a branch's daily cash closing.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("639380b3-12ab-41d6-adec-478169776a53"),
            Code = "Reports.View",
            Name = "View reports",
            Description = "View all reporting endpoints.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("48b7a02d-05b2-426d-b945-2173b29714db"),
            Code = "Backups.Manage",
            Name = "Manage backups",
            Description = "Trigger, list, download, and delete database backups.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("b35080f8-7b67-4965-a16b-2a9b84cb0827"),
            Code = "Sessions.Manage",
            Name = "Manage sessions",
            Description = "View active sessions and revoke them administratively.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("526311ff-3ca8-4533-b4f4-5ae6f375c14c"),
            Code = "Notifications.View",
            Name = "View notifications",
            Description = "View the in-app notification feed.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("9a5adc62-5085-48a4-a218-2de2045bb24a"),
            Code = "Users.Manage",
            Name = "Manage users",
            Description = "Create users and assign roles/branches to them.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        builder.HasData(new
        {
            Id = Guid.Parse("3f90797a-d3cd-482e-acad-5187542a5326"),
            Code = "Inventory.ComplimentaryIssue",
            Name = "Record complimentary issues",
            Description = "Issue stock as complimentary/internal consumption, with no revenue entry.",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(rp => rp.Id);

        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();

        // Cross-aggregate reference to Permission -> Restrict: cannot delete
        // a Permission still granted to a Role.
        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Seed: ربط دور Master Admin بكل الصلاحيات.
        builder.HasData(new
        {
            Id = Guid.Parse("688b93ca-3c2c-44f8-b99b-e4b55f350920"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("f2f8a36f-f4d1-4f2b-a1ba-18be8c023f34")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("ce0e0a63-0cea-4b22-a41d-86c02fdc81d8"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("5f14a6c3-3b89-4512-9799-3b25ecdefb40")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("84de41e8-1f8c-4b03-ac20-c596924183db"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("3d939df0-3319-4dc7-ad83-cd0567607e8a")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("9f769ab6-d52c-4c4a-9dd0-abb2b4b9263b"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("92cfde3f-6a23-48a9-ada8-27adb926af76")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("34ee2ce3-a5b8-4ff2-bd74-67158ef1a382"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("69c92d8d-cf96-4a66-b1b4-b8149ab8f0ca")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("b9f359f1-ffdb-4d7d-9db5-cfa3b3f3e122"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("81776805-df39-4a6a-a395-60df218bf010")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("3511cbcc-340d-4d91-9f48-71c8b5f0a15c"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("4da27a4a-a381-4225-b7da-9ad63fc3c963")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("ced90d30-c920-4a8a-909a-bec91589e7c2"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("56e2faed-431e-464c-a808-5f1bd84046c5")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("951cdb12-8fdb-41e6-8960-89b08d22e0e8"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("f29bb266-8361-4ed2-aee4-4926ebe4f021")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("974345e8-0faf-4823-aba8-9437c83fe0b9"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("82ebffce-eba3-4dc3-9fea-f9b0ff5d058a")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("ea9c16ac-f74d-4234-9d53-16a530cea009"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("bc142cda-a285-4e26-9f90-64208cc270fa")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("5f69cc23-c630-4325-b637-118f5117a9a1"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("a7fc0954-e9d6-4c47-af8a-4620d9faf6f0")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("eeafbecf-28f8-49f6-9754-8fc320b608de"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("639380b3-12ab-41d6-adec-478169776a53")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("ec9db506-e0de-4c11-b61e-6597f2b74942"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("48b7a02d-05b2-426d-b945-2173b29714db")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("5631c8ca-28bb-4525-85b8-5e5221a069a7"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("b35080f8-7b67-4965-a16b-2a9b84cb0827")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("d08e1cd4-4392-47bf-be95-150da97ef0ec"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("526311ff-3ca8-4533-b4f4-5ae6f375c14c")
        });

        // Master Admin -> الصلاحية الجديدة (Users.Manage)
        builder.HasData(new
        {
            Id = Guid.Parse("0d5da59d-e108-4d74-9729-498acca8b9ab"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("9a5adc62-5085-48a4-a218-2de2045bb24a")
        });

        // Master Admin -> الصلاحية الجديدة (Inventory.ComplimentaryIssue)
        builder.HasData(new
        {
            Id = Guid.Parse("4310626b-7558-43de-aef5-515bb654cb10"),
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"),
            PermissionId = Guid.Parse("3f90797a-d3cd-482e-acad-5187542a5326")
        });

        // Seed: ربط دور كاشير بصلاحياته (PermissionCodes.CashierDefaults).
        builder.HasData(new
        {
            Id = Guid.Parse("80f8e95f-c65e-488b-bb00-1eaec2e6a44d"),
            RoleId = Guid.Parse("f3b401c7-84f6-4a0f-9f17-b689979c5d8c"),
            PermissionId = Guid.Parse("5f14a6c3-3b89-4512-9799-3b25ecdefb40")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("64dfaefb-0314-4cd4-8357-6ee33f20ae91"),
            RoleId = Guid.Parse("f3b401c7-84f6-4a0f-9f17-b689979c5d8c"),
            PermissionId = Guid.Parse("3d939df0-3319-4dc7-ad83-cd0567607e8a")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("e456ce19-d200-4c7b-bc9c-e3b58dbe6778"),
            RoleId = Guid.Parse("f3b401c7-84f6-4a0f-9f17-b689979c5d8c"),
            PermissionId = Guid.Parse("92cfde3f-6a23-48a9-ada8-27adb926af76")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("a1196dff-6fd2-4dfd-b3be-7da022bb309d"),
            RoleId = Guid.Parse("f3b401c7-84f6-4a0f-9f17-b689979c5d8c"),
            PermissionId = Guid.Parse("526311ff-3ca8-4533-b4f4-5ae6f375c14c")
        });

        // Seed: ربط دور مساعد أدمن بصلاحياته (PermissionCodes.AssistantAdminDefaults).
        builder.HasData(new
        {
            Id = Guid.Parse("b44fe883-0ad6-448d-9603-35a063d0d728"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("5f14a6c3-3b89-4512-9799-3b25ecdefb40")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("e0887452-d701-4828-848b-61dfcec97f12"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("3d939df0-3319-4dc7-ad83-cd0567607e8a")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("10e2049c-998d-42f6-b326-2519885e9c3f"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("92cfde3f-6a23-48a9-ada8-27adb926af76")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("cab8d06b-9fd1-4ab7-afd5-e9dbb8ed2cf7"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("69c92d8d-cf96-4a66-b1b4-b8149ab8f0ca")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("079fa3eb-78ef-445e-983d-96fa917beb84"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("81776805-df39-4a6a-a395-60df218bf010")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("dc00f031-eb4c-4951-88a7-d9349a66da88"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("4da27a4a-a381-4225-b7da-9ad63fc3c963")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("b42b4489-814c-4116-8f94-33a13aeff689"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("56e2faed-431e-464c-a808-5f1bd84046c5")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("382e7005-6f53-41bd-a32c-ccb564fc73a2"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("82ebffce-eba3-4dc3-9fea-f9b0ff5d058a")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("bd5bba81-0510-472b-b14f-855764ab6ada"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("bc142cda-a285-4e26-9f90-64208cc270fa")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("216674c2-ccea-496f-b537-27f8fe8e78c0"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("a7fc0954-e9d6-4c47-af8a-4620d9faf6f0")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("6271e28d-6960-4731-af91-92b6c43b9972"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("639380b3-12ab-41d6-adec-478169776a53")
        });
        builder.HasData(new
        {
            Id = Guid.Parse("055aae7f-b0f9-48b4-986a-e94a834ef7aa"),
            RoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642"),
            PermissionId = Guid.Parse("526311ff-3ca8-4533-b4f4-5ae6f375c14c")
        });
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(ur => ur.Id);

        // Non-unique composite index: BranchId is nullable (global vs.
        // branch-scoped assignment), and SQL Server's default unique-index
        // NULL semantics would not reliably prevent duplicate global
        // (BranchId = NULL) assignments, so uniqueness is enforced at the
        // Application layer instead of via a filtered index here.
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId, ur.BranchId });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Seed: grants the bootstrap admin (BootstrapSeedIds.AdminUserId)
        // the Master Admin role at the bootstrap Main branch — see
        // BranchConfiguration/UserConfiguration for the branch/user rows
        // this references.
        builder.HasData(new
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            UserId = BootstrapSeedIds.AdminUserId,
            RoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"), // Master Admin, seeded in RoleConfiguration above
            BranchId = BootstrapSeedIds.MainBranchId
        });
    }
}

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("UserBranches");
        builder.HasKey(ub => ub.Id);

        builder.HasIndex(ub => new { ub.UserId, ub.BranchId }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        // Branch FK configured (Restrict) on the Branches side.

        // Seed: gives the bootstrap admin access to the bootstrap Main
        // branch, as their default — see BranchConfiguration/
        // UserConfiguration for the rows this references. Login's branch
        // resolution (LoginCommand.ResolveBranchAsync) needs a UserBranch
        // row to pick a branch at all.
        builder.HasData(new
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            UserId = BootstrapSeedIds.AdminUserId,
            BranchId = BootstrapSeedIds.MainBranchId,
            IsDefault = true
        });
    }
}

public class UserLoginLogConfiguration : IEntityTypeConfiguration<UserLoginLog>
{
    public void Configure(EntityTypeBuilder<UserLoginLog> builder)
    {
        builder.ToTable("UserLoginLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.AttemptedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(l => l.IpAddress).HasMaxLength(64);

        // Historical/security record -> Restrict, not Cascade, even though
        // Users are soft-deleted in practice and this rarely fires.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.UserId, l.AttemptedAtUtc });
    }
}

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.AppType).HasConversion<int>().IsRequired();
        builder.Property(s => s.RefreshTokenHash).IsRequired().HasMaxLength(200);
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.DeviceInfo).HasMaxLength(500);
        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.ExpiresAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.RevokedAtUtc).HasColumnType("datetime2");
        builder.Property(s => s.LastRefreshedAtUtc).HasColumnType("datetime2");
        builder.Property(s => s.RevocationReason).HasConversion<int>();

        // البحث بالبصمة هو المسار الساخن الوحيد لتجديد التوكن — يُنفَّذ مع
        // كل تجديد لكل مستخدم. بلا فهرس هون، كل تجديد بيعمل مسحًا كاملًا
        // لجدول بينمو باستمرار.
        builder.HasIndex(s => s.RefreshTokenHash);

        // المسار الثاني: "جِب الجلسة الفعّالة لهذا المستخدم بهذا التطبيق"
        // — يُستدعى عند كل تسجيل دخول (لسحب الجلسة السابقة).
        builder.HasIndex(s => new { s.UserId, s.AppType, s.RevokedAtUtc });

        // FK مقيَّد لا Cascade: الجلسات سجل أمني، لا تُمحى بحذف مستخدم
        // (والمستخدمون أصلًا يُحذفون حذفًا ناعمًا لا فعليًا).
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> builder)
    {
        builder.ToTable("UserDevices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeviceIdentifier).IsRequired().HasMaxLength(200);
        builder.Property(d => d.DeviceName).HasMaxLength(200);
        builder.Property(d => d.LastSeenAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(d => d.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(d => d.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.UserId);
    }
}
