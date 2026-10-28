using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.Domain.Partners;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// وحدة الشركاء (28/9/2026) - كل شي تحت Partners.Manage (Master Admin)، إلا سحب الشريك من الكاشير بتحقق هويته
/// (Sales.Create - الكاشير داخل بحسابه) - برّا المجموعة، راجع §3.4.
/// </summary>
public static class PartnersEndpoints
{
    public static IEndpointRouteBuilder MapPartnersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/partners")
            .WithTags("Partners")
            .RequirePermission(PermissionCodes.PartnersManage);

        group.MapGet("/", async (Guid? branchId, GetPartnersHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetPartnersQuery(branchId), cancellationToken)))
            .WithName("GetPartners")
            .WithSummary("الشركاء برصيد كل واحد (أنصبة − سحوبات − بضاعة بسعر التكلفة) ورأس ماله.")
            .Produces<IReadOnlyList<PartnerDto>>(StatusCodes.Status200OK);

        group.MapPost("/", async (CreatePartnerCommand command, CreatePartnerHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                return result.ToHttpResult(id => Results.Created($"/api/v1/partners/{id}", new { partnerId = id }));
            })
            .WithName("CreatePartner")
            .WithSummary("شريك جديد بفرع: رأس مال (نصيبه من رأس ماله) أو مضارب (نسبة ثابتة).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{partnerId:guid}", async (Guid partnerId, UpdatePartnerRequest request, UpdatePartnerHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new UpdatePartnerCommand(partnerId, request.FullName, request.UserId, request.SpeculativeProfitPercent, request.Notes), cancellationToken);
                return result.ToHttpResult();
            })
            .WithName("UpdatePartner")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{partnerId:guid}/active", async (Guid partnerId, SetPartnerActiveRequest request, SetPartnerActiveHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new SetPartnerActiveCommand(partnerId, request.IsActive), cancellationToken)).ToHttpResult())
            .WithName("SetPartnerActive");

        group.MapPut("/{partnerId:guid}/telegram-phone", async (Guid partnerId, SetPartnerTelegramPhoneRequest request, SetPartnerTelegramPhoneHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new SetPartnerTelegramPhoneCommand(partnerId, request.TelegramPhone), cancellationToken)).ToHttpResult())
            .WithName("SetPartnerTelegramPhone")
            .WithSummary("رقم تلغرام الشريك - لكود التحقق (OTP) وقت سحبه من الكاشير. فاضي = بلا.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{partnerId:guid}/cashier-barcode", async (Guid partnerId, IssuePartnerBarcodeHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(partnerId, cancellationToken)).ToHttpResult(r => Results.Ok(r)))
            .WithName("IssuePartnerCashierBarcode")
            .WithSummary("إصدار باركود شخصي جديد للشريك (بيلغي القديم) - الأرقام بترجع مرة وحدة بس للطباعة.")
            .Produces<IssuePartnerBarcodeResponse>(StatusCodes.Status200OK);

        group.MapDelete("/{partnerId:guid}/cashier-barcode", async (Guid partnerId, RevokePartnerBarcodeHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(partnerId, cancellationToken)).ToHttpResult())
            .WithName("RevokePartnerCashierBarcode")
            .WithSummary("إلغاء باركود الشريك (كرت ضايع).");

        group.MapGet("/{partnerId:guid}/ledger", async (Guid partnerId, GetPartnerLedgerHandler handler, CancellationToken cancellationToken) =>
            {
                var ledger = await handler.HandleAsync(partnerId, cancellationToken);
                return ledger is null ? Results.NotFound() : Results.Ok(ledger);
            })
            .WithName("GetPartnerLedger")
            .WithSummary("كشف حساب شريك: أنصبة، سحوبات، بضاعة بسعر التكلفة - برصيد متراكم.")
            .Produces<PartnerLedgerDto>(StatusCodes.Status200OK);

        group.MapGet("/{partnerId:guid}/reinvest-preview", async (Guid partnerId, decimal amount, GetReinvestPreviewHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new GetReinvestPreviewQuery(partnerId, amount), cancellationToken)).ToHttpResult(r => Results.Ok(r)))
            .WithName("GetPartnerReinvestPreview")
            .WithSummary("معاينة إعادة استثمار ربح الشريك برأس ماله: الرصيد ورأس المال ونسبته من الأرباح قبل وبعد - بلا أي كتابة.")
            .Produces<ReinvestPreviewDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{partnerId:guid}/reinvest", async (Guid partnerId, ReinvestPartnerProfitRequest request, ReinvestPartnerProfitHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new ReinvestPartnerProfitCommand(partnerId, request.Amount, request.Password, request.Notes, request.ClientRequestId), cancellationToken);
                return result.ToHttpResult(r => r.WasReplay ? Results.Ok(r) : Results.Created($"/api/v1/partners/{partnerId}/ledger", r));
            })
            .WithName("ReinvestPartnerProfit")
            .WithSummary("إعادة استثمار جزء من رصيد الشريك برأس ماله (بلا حركة نقد) - بتطلب كلمة سر المستخدم الحالي، وبتزيد نسبته من أرباح الشهور الجاية.")
            .Produces<ReinvestPartnerProfitResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{partnerId:guid}/reinvestments", async (Guid partnerId, GetPartnerReinvestmentsHandler handler, CancellationToken cancellationToken) =>
            {
                var list = await handler.HandleAsync(partnerId, cancellationToken);
                return list is null ? Results.NotFound() : Results.Ok(list);
            })
            .WithName("GetPartnerReinvestments")
            .WithSummary("إعادات استثمار الشريك (الأحدث أول) وحالة كل وحدة: منعكسة أو لا.")
            .Produces<IReadOnlyList<PartnerReinvestmentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/reinvestments/{reinvestmentId:guid}/reverse-preview", async (Guid reinvestmentId, GetReverseReinvestmentPreviewHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(reinvestmentId, cancellationToken)).ToHttpResult(r => Results.Ok(r)))
            .WithName("GetReverseReinvestmentPreview")
            .WithSummary("معاينة التراجع عن إعادة استثمار: الرصيد ورأس المال ونسبته قبل وبعد - بلا أي كتابة.")
            .Produces<ReverseReinvestmentPreviewDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/reinvestments/{reinvestmentId:guid}/reverse", async (Guid reinvestmentId, ReverseReinvestmentRequest request, ReverseReinvestmentHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new ReverseReinvestmentCommand(reinvestmentId, request.Password, request.Reason, request.ClientRequestId), cancellationToken);
                return result.ToHttpResult(r => r.WasReplay ? Results.Ok(r) : Results.Created($"/api/v1/partners/{r.PartnerId}/ledger", r));
            })
            .WithName("ReverseReinvestment")
            .WithSummary("التراجع عن إعادة استثمار (كاملة فقط، مرة وحدة): بيرجّع المبلغ من رأس مال الشريك لرصيده - بتطلب كلمة سر المستخدم الحالي.")
            .Produces<ReverseReinvestmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/withdrawals", async (Guid? branchId, Guid? partnerId, GetPartnerWithdrawalsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetPartnerWithdrawalsQuery(branchId, partnerId), cancellationToken)))
            .WithName("GetPartnerWithdrawals")
            .Produces<IReadOnlyList<PartnerWithdrawalDto>>(StatusCodes.Status200OK);

        group.MapPost("/withdrawals/{withdrawalId:guid}/mark-reviewed", async (Guid withdrawalId, MarkPartnerWithdrawalReviewedHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(withdrawalId, cancellationToken)).ToHttpResult())
            .WithName("MarkPartnerWithdrawalReviewed")
            .WithSummary("تمت مراجعة سحب شريك فوق رصيده (28/10/2026).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/withdrawals", async (RecordPartnerWithdrawalCommand command, RecordPartnerWithdrawalHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                return result.ToHttpResult(r => r.WasReplay ? Results.Ok(r) : Results.Created($"/api/v1/partners/withdrawals/{r.WithdrawalId}", r));
            })
            .WithName("RecordPartnerWithdrawal")
            .WithSummary("سحب شريك: من الصندوق (بينقص المتوقع بالتقفيل) أو من جيب صاحب المحل (بيزيد مستحقه). مسموح أكتر من الرصيد بس بيتعلّم للمراجعة + تنبيه خطير.")
            .Produces<PartnerWithdrawalResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/statements", async (Guid? branchId, GetPartnerStatementsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetPartnerStatementsQuery(branchId), cancellationToken)))
            .WithName("GetPartnerStatements")
            .Produces<IReadOnlyList<PartnerStatementSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/statements/{statementId:guid}", async (Guid statementId, GetPartnerStatementByIdHandler handler, CancellationToken cancellationToken) =>
            {
                var statement = await handler.HandleAsync(statementId, cancellationToken);
                return statement is null ? Results.NotFound() : Results.Ok(statement);
            })
            .WithName("GetPartnerStatement")
            .Produces<PartnerStatementDto>(StatusCodes.Status200OK);

        group.MapPost("/statements", async (GeneratePartnerStatementCommand command, GeneratePartnerStatementHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(command, cancellationToken)).ToHttpResult())
            .WithName("GeneratePartnerStatement")
            .WithSummary("إصدار (أو إعادة إصدار) كشف شهر لفرع - بيحسب ربح الشهر ويوزّعه على الشركاء.")
            .Produces<PartnerStatementDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/owner-receivables", async (Guid? branchId, GetOwnerReceivablesHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetOwnerReceivablesQuery(branchId), cancellationToken)))
            .WithName("GetOwnerReceivables")
            .WithSummary("مستحق لصاحب المحل (دفع لشركاء من جيبه) - الرصيد والحركات.")
            .Produces<IReadOnlyList<OwnerReceivableDto>>(StatusCodes.Status200OK);

        group.MapPost("/owner-receivables/repayments", async (RecordOwnerRepaymentCommand command, RecordOwnerRepaymentHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(command, cancellationToken)).ToHttpResult(balance => Results.Ok(new { remainingBalance = balance })))
            .WithName("RecordOwnerRepayment")
            .WithSummary("صاحب المحل استرجع مستحقه (من الصندوق أو برّا) - يدوي صريح، ما بيتجاوز المستحق.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/v1/partner-withdrawals/verified", async (
                RecordVerifiedPartnerWithdrawalCommand command, RecordVerifiedPartnerWithdrawalHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                return result.ToHttpResult(r => r.WasReplay ? Results.Ok(r) : Results.Created($"/api/v1/partners/withdrawals/{r.WithdrawalId}", r));
            })
            .WithName("RecordVerifiedPartnerWithdrawal")
            .WithTags("Partners")
            .RequirePermission(PermissionCodes.SalesCreate)
            .WithSummary("سحب شريك من شاشة الكاشير: يوزر وكلمة سر الشريك، أو كود تلغرام، أو باركوده الشخصي (الكاشير داخل بحسابه)، والسحب من الصندوق.")
            .Produces<PartnerWithdrawalResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // تحقق الشريك بالكاشير بطرق إضافية (29/9/2026) - الكاشير داخل بحسابه (Sales.Create)، برّا المجموعة (§3.4).
        app.MapGet("/api/v1/partner-withdrawals/verification-options", async (
                Guid branchId, GetPartnerVerificationOptionsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(branchId, cancellationToken)))
            .WithName("GetPartnerVerificationOptions")
            .WithTags("Partners")
            .RequirePermission(PermissionCodes.SalesCreate)
            .WithSummary("طرق تحقق الشريك المفعّلة بالكاشير، والشركاء اللي بيقدروا يستلموا كود تلغرام.")
            .Produces<PartnerVerificationOptionsDto>(StatusCodes.Status200OK);

        app.MapPost("/api/v1/partner-withdrawals/otp", async (
                RequestPartnerOtpCommand command, RequestPartnerOtpHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(command, cancellationToken)).ToHttpResult(r => Results.Ok(r)))
            .WithName("RequestPartnerWithdrawalOtp")
            .WithTags("Partners")
            .RequirePermission(PermissionCodes.SalesCreate)
            .WithSummary("كود تلغرام للشريك لسحب مبلغ محدد من الكاشير - صالح 5 دقايق ولنفس المبلغ بس.")
            .Produces<RequestPartnerOtpResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record SetPartnerTelegramPhoneRequest(string? TelegramPhone);

    public sealed record UpdatePartnerRequest(string FullName, Guid? UserId, decimal? SpeculativeProfitPercent, string? Notes);

    public sealed record SetPartnerActiveRequest(bool IsActive);

    public sealed record ReverseReinvestmentRequest(string? Password, string? Reason, Guid ClientRequestId);

    public sealed record ReinvestPartnerProfitRequest(decimal Amount, string? Password, string? Notes, Guid ClientRequestId);
}
