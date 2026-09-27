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

    /// <summary>
    /// ترتيب ظهور الوحدة بقائمة الاختيار (الأصغر أول) - بيتحكم فيه صاحب
    /// المحل من صفحة وحدات القياس، عشان الوحدات الأكثر استخدامًا (حبة،
    /// كيلو...) تطلع فوق بدل الترتيب الأبجدي.
    /// </summary>
    public int SortOrder { get; private set; }

    private UnitOfMeasure() { } // EF Core

    public UnitOfMeasure(string name, int sortOrder)
    {
        Name = name;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public void Rename(string name) => Name = name;

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
