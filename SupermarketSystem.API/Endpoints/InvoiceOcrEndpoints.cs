using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.API.Endpoints;

public static class InvoiceOcrEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceOcrEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/purchase-invoices/extract-from-image", async (
            IFormFile file,
            IImageStorageService imageStorage,
            IInvoiceExtractionService extractionService,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.Problem(
                    title: "Image.Empty", detail: "لم يتم رفع أي ملف.", statusCode: StatusCodes.Status400BadRequest);
            }

            byte[] imageBytes;
            await using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream, cancellationToken);
                imageBytes = memoryStream.ToArray();
            }

            var storageResult = await imageStorage.SaveAsWebPAsync(imageBytes, cancellationToken);
            if (storageResult.IsFailure)
            {
                return storageResult.ToHttpResult();
            }

            var extractionResult = await extractionService.ExtractAsync(imageBytes, file.ContentType, cancellationToken);

            return Results.Ok(new ExtractInvoiceFromImageResponse(
                storageResult.Value,
                extractionResult.IsSuccess ? extractionResult.Value.ProviderName : null,
                extractionResult.IsSuccess ? extractionResult.Value.Extraction : null,
                extractionResult.IsFailure ? extractionResult.Error!.Message : null));
        })
        .WithName("ExtractInvoiceFromImage")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreate)
        .WithSummary("يرفع صورة فاتورة شراء، يحفظها كـWebP، ويحاول قراءتها آليًا (Gemini → Gemini Flash → Claude). الصورة تُحفظ حتى لو فشل الاستخراج.")
        .DisableAntiforgery()
        .Produces<ExtractInvoiceFromImageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    public sealed record ExtractInvoiceFromImageResponse(
        string ImageReference,
        string? ProviderName,
        InvoiceExtractionResult? Extraction,
        string? ExtractionError);
}
