using System.Text.Json;
using System.Text.Json.Serialization;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Infrastructure.Services;

internal static class InvoiceOcrResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Result<InvoiceExtractionResult> Parse(string rawText, string providerName)
    {
        // بعض النماذج بترجّع الـJSON ملفوف بـmarkdown code fence رغم
        // التعليمات الصريحة بالبرومبت بعدم فعل هذا — تنظيف دفاعي، لا اعتماد
        // كامل على التزام النموذج.
        var cleaned = rawText.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
            {
                cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
            }
        }

        RawExtractionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RawExtractionDto>(cleaned, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Result.Failure<InvoiceExtractionResult>(Error.BusinessRule(
                "InvoiceOcr.InvalidJsonResponse",
                $"استجابة {providerName} لم تكن JSON صالحًا: {ex.Message}"));
        }

        if (dto is null)
        {
            return Result.Failure<InvoiceExtractionResult>(Error.BusinessRule(
                "InvoiceOcr.EmptyResponse", $"استجابة {providerName} كانت فارغة."));
        }

        DateOnly? invoiceDate = null;
        if (!string.IsNullOrWhiteSpace(dto.InvoiceDate) && DateOnly.TryParse(dto.InvoiceDate, out var parsedDate))
        {
            invoiceDate = parsedDate;
        }

        var items = (dto.Items ?? new List<RawItemDto>())
            .Select(i => new InvoiceExtractionItem(
                i.RawProductName ?? "(غير محدَّد)", i.Quantity, i.UnitOfMeasure, i.UnitCost, i.LineTotal))
            .ToList();

        var result = new InvoiceExtractionResult(
            dto.SupplierName,
            dto.SupplierInvoiceReference,
            invoiceDate,
            dto.Currency,
            items,
            dto.InvoiceTotal,
            dto.ExtractionConfidence ?? "low",
            dto.Warnings ?? new List<string>());

        return Result.Success(result);
    }

    // يطابق شكل الـJSON المطلوب بالبرومبت بالضبط (InvoiceOcrPrompt.Text) —
    // أي تعديل على أحدهم لازم ينعكس على التاني.
    private sealed class RawExtractionDto
    {
        [JsonPropertyName("supplierName")] public string? SupplierName { get; set; }
        [JsonPropertyName("supplierInvoiceReference")] public string? SupplierInvoiceReference { get; set; }
        [JsonPropertyName("invoiceDate")] public string? InvoiceDate { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("items")] public List<RawItemDto>? Items { get; set; }
        [JsonPropertyName("invoiceTotal")] public decimal? InvoiceTotal { get; set; }
        [JsonPropertyName("extractionConfidence")] public string? ExtractionConfidence { get; set; }
        [JsonPropertyName("warnings")] public List<string>? Warnings { get; set; }
    }

    private sealed class RawItemDto
    {
        [JsonPropertyName("rawProductName")] public string? RawProductName { get; set; }
        [JsonPropertyName("quantity")] public decimal Quantity { get; set; }
        [JsonPropertyName("unitOfMeasure")] public string? UnitOfMeasure { get; set; }
        [JsonPropertyName("unitCost")] public decimal? UnitCost { get; set; }
        [JsonPropertyName("lineTotal")] public decimal? LineTotal { get; set; }
    }
}
