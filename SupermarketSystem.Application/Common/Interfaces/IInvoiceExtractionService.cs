using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>ProviderName: أي مزوّد فعليًا نجح — معلومة تُعرض للمستخدم ("استُخرجت عبر Gemini")، مش مخفية.</summary>
public sealed record InvoiceExtractionOutcome(string ProviderName, InvoiceExtractionResult Extraction);

public interface IInvoiceExtractionService
{
    Task<Result<InvoiceExtractionOutcome>> ExtractAsync(byte[] imageBytes, string mimeType, CancellationToken cancellationToken);
}
