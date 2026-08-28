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
}
