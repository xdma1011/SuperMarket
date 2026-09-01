using System.Text.Json;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

/// <summary>
/// سطر واحد بمسودة فاتورة — إما كما استخرجه الذكاء الاصطناعي (MatchedProductId
/// null، بانتظار مراجع يطابقه يدويًا)، أو بعد ما راجعه/عدّله المستخدم.
/// NewBatchExpiryDate نص "yyyy-MM-dd" لا DateOnly مباشرة - يبقى الشكل
/// نفسه لما يتحوّل لـJSON ويرجع، بلا مفاجآت تنسيق ثقافة (culture) مختلفة.
/// </summary>
public sealed record PurchaseInvoiceDraftItemDto(
    string RawProductName,
    decimal Quantity,
    string? UnitOfMeasure,
    decimal? UnitCost,
    decimal? LineTotal,
    Guid? MatchedProductId,
    string? MatchedProductName,
    Guid? MatchedProductUnitId,
    bool IsBatchTracked,
    string? NewBatchNumber,
    string? NewBatchExpiryDate);

/// <summary>
/// PurchaseInvoiceDraft.ItemsJson مخزَّن كنص خام بالـDomain (لا يعرف شكل
/// DraftItemDto - هذا تفصيل Application). هذا الصنف نقطة التحويل الوحيدة،
/// لتفادي JsonSerializer.Serialize/Deserialize مكرَّرة بكل handler.
/// </summary>
public static class PurchaseInvoiceDraftItemsSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<PurchaseInvoiceDraftItemDto> items) =>
        JsonSerializer.Serialize(items, Options);

    public static List<PurchaseInvoiceDraftItemDto> Deserialize(string itemsJson) =>
        JsonSerializer.Deserialize<List<PurchaseInvoiceDraftItemDto>>(itemsJson, Options) ?? new List<PurchaseInvoiceDraftItemDto>();

    public static string SerializeWarnings(IReadOnlyList<string> warnings) =>
        JsonSerializer.Serialize(warnings, Options);

    public static List<string> DeserializeWarnings(string? warningsJson) =>
        string.IsNullOrWhiteSpace(warningsJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(warningsJson, Options) ?? new List<string>();
}
