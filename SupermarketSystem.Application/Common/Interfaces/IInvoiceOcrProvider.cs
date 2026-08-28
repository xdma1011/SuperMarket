using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>سطر مستخرج من الفاتورة - بيانات خام كما ظهرت بالصورة، بلا أي ربط بمنتج فعلي بالنظام (المطابقة تصير من طرف المستخدم لاحقًا، ليست هنا).</summary>
public sealed record InvoiceExtractionItem(
    string RawProductName,
    decimal Quantity,
    string? UnitOfMeasure,
    decimal? UnitCost,
    decimal? LineTotal);

public sealed record InvoiceExtractionResult(
    string? SupplierName,
    string? SupplierInvoiceReference,
    DateOnly? InvoiceDate,
    string? Currency,
    IReadOnlyList<InvoiceExtractionItem> Items,
    decimal? InvoiceTotal,
    string ExtractionConfidence,
    IReadOnlyList<string> Warnings);

/// <summary>
/// مزوّد واحد لاستخراج بيانات فاتورة من صورة. كل مزوّد فعلي (Gemini،
/// Gemini Flash، Claude) بيطبّق هذا الـinterface بشكل منفصل — يسمح
/// بـfallback service (الخطوة الجاية) يجرّبهم بالترتيب المحدَّد، بلا ما
/// يعرف تفاصيل أي مزوّد تحديدًا.
///
/// Result فاشل هون بيغطي حالتين مختلفتين بنفس المعنى العملي: "هذا المزوّد
/// ما قدر يفيدنا الآن" — سواء السبب رفض API (مفتاح مفقود، حصة منتهية،
/// شبكة) أو استجابة النموذج نفسها كانت JSON غير صالح رغم التعليمات
/// الصارمة بالبرومبت. الـfallback service (الخطوة الجاية) ما بيفرّق بين
/// الحالتين، بس بينتقل للمزوّد التالي بالحالتين.
/// </summary>
public interface IInvoiceOcrProvider
{
    /// <summary>اسم واضح للتشخيص/التسجيل — يقول أي مزوّد فعليًا نجح أو فشل لو صار fallback.</summary>
    string ProviderName { get; }

    Task<Result<InvoiceExtractionResult>> ExtractAsync(byte[] imageBytes, string mimeType, CancellationToken cancellationToken);
}
