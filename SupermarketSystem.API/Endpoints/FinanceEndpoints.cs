using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Finance.CreateCapitalTransaction;
using SupermarketSystem.Application.Finance.CreateExpense;
using SupermarketSystem.Application.Finance.GetCapitalTransactions;
using SupermarketSystem.Application.Finance.GetExpenses;
using SupermarketSystem.Application.Finance.GetMonthlyProfitStatement;
using SupermarketSystem.Domain.Finance;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// مصاريف تشغيلية، حركات رأس مال، وكشف الربح الشهري - Finance.Manage
/// حصرًا (Master Admin افتراضيًا)، بطلب صاحب المشروع الصريح 15-17/9/2026.
/// راجع تعليقات Expense.cs/CapitalTransaction.cs/GetMonthlyProfitStatementQuery.cs
/// بالـApplication/Domain للتفاصيل الكاملة.
/// </summary>
public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/finance").WithTags("Finance").RequirePermission(PermissionCodes.FinanceManage);

        group.MapPost("/expenses", async (
            CreateExpenseRequest request,
            CreateExpenseHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateExpenseCommand(
                request.BranchId, request.Category, request.Amount, request.PaymentDateUtc,
                request.PeriodYear, request.PeriodMonth, request.Notes);

            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/finance/expenses/{response.ExpenseId}", response));
        })
        .WithName("CreateExpense")
        .WithSummary("يسجّل مصروف تشغيلي (إيجار/كهرباء/ماء/راتب/أخرى) لفرع - سجل تاريخي، بلا تعديل/حذف لاحق.")
        .Produces<CreateExpenseResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/expenses", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId, int? periodYear, int? periodMonth, ExpenseCategory? category,
            GetExpensesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(
                new GetExpensesQuery(paging, branchId, periodYear, periodMonth, category), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetExpenses")
        .WithSummary("قائمة المصاريف التشغيلية المسجَّلة، مع فلترة بالفرع/الفترة/التصنيف.")
        .Produces<PagedResult<ExpenseListItemDto>>(StatusCodes.Status200OK);

        group.MapPost("/capital-transactions", async (
            CreateCapitalTransactionRequest request,
            CreateCapitalTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateCapitalTransactionCommand(
                request.BranchId, request.Type, request.Amount, request.OccurredAtUtc, request.Notes);

            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/finance/capital-transactions/{response.CapitalTransactionId}", response));
        })
        .WithName("CreateCapitalTransaction")
        .WithSummary("سحب/إضافة رأس مال يدوي صريح لفرع - مستقل كليًا عن كشف الربح الشهري، فعل إداري بحت لا يحصل تلقائيًا أبدًا.")
        .Produces<CreateCapitalTransactionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/capital-transactions", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId,
            GetCapitalTransactionsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetCapitalTransactionsQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCapitalTransactions")
        .WithSummary("سجل حركات رأس المال اليدوية (سحب/إضافة) لفرع.")
        .Produces<PagedResult<CapitalTransactionListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/profit-statement", async (
            Guid branchId, int year, int month,
            GetMonthlyProfitStatementHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetMonthlyProfitStatementQuery(branchId, year, month), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetMonthlyProfitStatement")
        .WithSummary("كشف ربح شهر معيّن لفرع معيّن - صافي الإيراد ناقص تكلفة البضاعة المباعة (متوسط مرجّح وقت كل بيع) ناقص مصاريف الفترة. راجع تعليق الـHandler للتعريف الدقيق لكل رقم.")
        .Produces<GetMonthlyProfitStatementResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record CreateExpenseRequest(
        Guid BranchId, ExpenseCategory Category, decimal Amount, DateTime PaymentDateUtc,
        int PeriodYear, int PeriodMonth, string? Notes);

    public sealed record CreateCapitalTransactionRequest(
        Guid BranchId, CapitalTransactionType Type, decimal Amount, DateTime OccurredAtUtc, string? Notes);
}
