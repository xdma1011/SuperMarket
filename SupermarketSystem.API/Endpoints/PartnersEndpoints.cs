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

        group.MapGet("/{partnerId:guid}/ledger", async (Guid partnerId, GetPartnerLedgerHandler handler, CancellationToken cancellationToken) =>
            {
                var ledger = await handler.HandleAsync(partnerId, cancellationToken);
                return ledger is null ? Results.NotFound() : Results.Ok(ledger);
            })
            .WithName("GetPartnerLedger")
            .WithSummary("كشف حساب شريك: أنصبة، سحوبات، بضاعة بسعر التكلفة - برصيد متراكم.")
            .Produces<PartnerLedgerDto>(StatusCodes.Status200OK);

        group.MapGet("/withdrawals", async (Guid? branchId, Guid? partnerId, GetPartnerWithdrawalsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetPartnerWithdrawalsQuery(branchId, partnerId), cancellationToken)))
            .WithName("GetPartnerWithdrawals")
            .Produces<IReadOnlyList<PartnerWithdrawalDto>>(StatusCodes.Status200OK);

        group.MapPost("/withdrawals", async (RecordPartnerWithdrawalCommand command, RecordPartnerWithdrawalHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                return result.ToHttpResult(r => r.WasReplay ? Results.Ok(r) : Results.Created($"/api/v1/partners/withdrawals/{r.WithdrawalId}", r));
            })
            .WithName("RecordPartnerWithdrawal")
            .WithSummary("سحب شريك: من الصندوق (بينقص المتوقع بالتقفيل) أو من جيب صاحب المحل (بيزيد مستحقه). مسموح أكتر من الرصيد.")
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
            .WithSummary("سحب شريك من شاشة الكاشير: يوزر وكلمة سر الشريك نفسه (الكاشير داخل بحسابه)، والسحب من الصندوق.")
            .Produces<PartnerWithdrawalResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    public sealed record UpdatePartnerRequest(string FullName, Guid? UserId, decimal? SpeculativeProfitPercent, string? Notes);

    public sealed record SetPartnerActiveRequest(bool IsActive);
}
