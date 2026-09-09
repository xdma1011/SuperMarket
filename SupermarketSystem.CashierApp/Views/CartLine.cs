namespace SupermarketSystem.CashierApp.Views;

public sealed class CartLine
{
    public Guid ProductId { get; set; }
    public Guid ProductUnitId { get; set; }
    public Guid? ProductBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;

    /// <summary>
    /// الكمية المطلوبة تجاوزت الرصيد المحلي المخزَّن آخر مزامنة لهذه
    /// الدفعة - سماح مع مراجعة (CLAUDE.md §1.6)، لا منع: السيرفر هو
    /// الحكم الفعلي (AllowNegativeStock + SucceededWentNegative)، هذا
    /// مجرد تنبيه بصري للكاشير إنه احتمال البضاعة وصلت فعليًا ولسه ما
    /// انزامنت محليًا.
    /// </summary>
    public bool NeedsReview { get; set; }
    public string ReviewMark => NeedsReview ? "⚠ مراجعة" : "";
}
