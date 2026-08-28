using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Common.Services;

/// <summary>
/// بيعيش بطبقة Application، لا Infrastructure — رغم إنه بيتعامل مع
/// "مزوّدين خارجيين"، هو نفسه ما بيعمل ولا استدعاء HTTP مباشر؛ كل اتصال
/// خارجي مغلَّف أصلًا خلف IInvoiceOcrProvider (اللي تطبيقاته الفعلية هي
/// اللي بـInfrastructure). هذا الصنف منطق تنسيق بحت: "جرّب هدول بالترتيب،
/// ارجع أول نجاح" — قرار بزنس، لا تفصيل نقل بيانات.
///
/// الترتيب مش مُدار هون بأي منطق صريح — هو ببساطة ترتيب عناصر
/// IEnumerable&lt;IInvoiceOcrProvider&gt; كما حقنها DI (Gemini، ثم Flash،
/// ثم Claude — بالضبط ترتيب التسجيل بـInfrastructure/DependencyInjection.cs).
/// لو تغيّر ترتيب التسجيل هناك، هذا الصنف بيتبعه تلقائيًا بلا أي تعديل هون.
/// </summary>
public sealed class FallbackInvoiceOcrService : IInvoiceExtractionService
{
    private readonly IEnumerable<IInvoiceOcrProvider> _providers;
    private readonly ILogger<FallbackInvoiceOcrService> _logger;

    public FallbackInvoiceOcrService(IEnumerable<IInvoiceOcrProvider> providers, ILogger<FallbackInvoiceOcrService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<Result<InvoiceExtractionOutcome>> ExtractAsync(
        byte[] imageBytes, string mimeType, CancellationToken cancellationToken)
    {
        var failureReasons = new List<string>();

        foreach (var provider in _providers)
        {
            var result = await provider.ExtractAsync(imageBytes, mimeType, cancellationToken);

            if (result.IsSuccess)
            {
                if (failureReasons.Count > 0)
                {
                    _logger.LogWarning(
                        "استخراج الفاتورة نجح عبر {Provider} بعد فشل: {PreviousFailures}",
                        provider.ProviderName, string.Join(" | ", failureReasons));
                }

                return Result.Success(new InvoiceExtractionOutcome(provider.ProviderName, result.Value));
            }

            failureReasons.Add($"{provider.ProviderName}: {result.Error!.Message}");
        }

        _logger.LogError("فشلت كل مزوّدي قراءة الفاتورة: {Failures}", string.Join(" | ", failureReasons));

        return Result.Failure<InvoiceExtractionOutcome>(Error.BusinessRule(
            "InvoiceOcr.AllProvidersFailed",
            $"تعذّرت قراءة الفاتورة آليًا بكل الطرق المتاحة. التفاصيل: {string.Join(" | ", failureReasons)}"));
    }
}
