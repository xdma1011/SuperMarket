using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Catalog;

/// <summary>
/// مرجع موحَّد لوحدات القياس (كيلو، لتر، حبة، متر...) - كانت مفقودة
/// كليًا، كل وحدة بمنتج (ProductUnit.UnitName) كانت نص حر يُكتب من جديد
/// بكل مرة، بلا أي قائمة مرجعية ولا اتساق بالتسمية بين المنتجات. هذا
/// الكيان بس مرجع/اقتراح لتعبئة ProductUnit.UnitName - عمدًا بلا FK من
/// ProductUnit إليه (ProductUnit.UnitName يضل نص حر بالـDomain، تفاديًا
/// لتعقيد ترحيل بيانات موجودة أصلًا)؛ الواجهة هي اللي بتوجّه الإدخال
/// نحو هالقائمة (قائمة اختيار بدل حقل نص حر).
/// </summary>
public class UnitOfMeasure : AuditableEntity
{
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    private UnitOfMeasure() { } // EF Core

    public UnitOfMeasure(string name)
    {
        Name = name;
        IsActive = true;
    }

    public void Rename(string name) => Name = name;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
