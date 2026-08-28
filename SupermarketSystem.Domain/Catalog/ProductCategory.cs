using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Catalog;

/// <summary>
/// Aggregate root, independent of Product (referenced by FK, not owned) —
/// categories have their own admin-managed lifecycle. Self-referencing for
/// a simple category/subcategory hierarchy.
/// </summary>
public class ProductCategory : AuditableEntity, ISoftDeletable, IHasRowVersion
{
    public string Name { get; private set; } = null!;
    public Guid? ParentCategoryId { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private ProductCategory() { } // EF Core

    public ProductCategory(string name, Guid? parentCategoryId = null)
    {
        Name = name;
        ParentCategoryId = parentCategoryId;
    }

    public void Rename(string name) => Name = name;
    public void MarkDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;
}
