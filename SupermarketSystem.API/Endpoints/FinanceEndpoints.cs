using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Finance.CreateCapitalTransaction;
using SupermarketSystem.Application.Finance.CreateExpense;
using SupermarketSystem.Application.Finance.ExpenseTypes;
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
                request.PeriodYear, request.PeriodMonth, request.Notes, request.ExpenseTypeId, request.PaidFromDrawer);

            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/finance/expenses/{response.ExpenseId}", response));
        })
        .WithName("CreateExpense")
        .WithSummary("يسجّل مصروف تشغيلي لفرع بنوع بيعرّفه صاحب المحل (expenseTypeId، أو category القديم) - PaidFromDrawer=true بينقص المتوقع بتقفيل الصندوق. سجل تاريخي، بلا تعديل/حذف لاحق.")
        .Produces<CreateExpenseResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/expenses", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId, int? periodYear, int? periodMonth, ExpenseCategory? category, Guid? expenseTypeId,
            GetExpensesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(
                new GetExpensesQuery(paging, branchId, periodYear, periodMonth, category, expenseTypeId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetExpenses")
        .WithSummary("قائمة المصاريف التشغيلية المسجَّلة، مع فلترة بالفرع/الفترة/التصنيف.")
        .Produces<PagedResult<ExpenseListItemDto>>(StatusCodes.Status200OK);

        // أنواع المصاريف (29/9/2026) - صاحب المحل بيعرّفها. بلا حذف (إيقاف بدلها).
        group.MapGet("/expense-types", async (bool? includeInactive, GetExpenseTypesHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetExpenseTypesQuery(includeInactive ?? false), cancellationToken)))
            .WithName("GetExpenseTypes")
            .WithSummary("أنواع المصاريف مرتّبة (الموقوفة بس مع includeInactive=true)، مع عدد المصاريف على كل نوع.")
            .Produces<IReadOnlyList<ExpenseTypeDto>>(StatusCodes.Status200OK);

        group.MapPost("/expense-types", async (CreateExpenseTypeCommand command, CreateExpenseTypeHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                return result.ToHttpResult(dto => Results.Created($"/api/v1/finance/expense-types/{dto.Id}", dto));
            })
            .WithName("CreateExpenseType")
            .Produces<ExpenseTypeDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/expense-types/{id:guid}", async (Guid id, UpdateExpenseTypeRequest request, UpdateExpenseTypeHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new UpdateExpenseTypeCommand(id, request.Name, request.IsActive), cancellationToken)).ToHttpResult())
            .WithName("UpdateExpenseType")
            .WithSummary("تغيير اسم نوع أو إيقافه/تفعيله (\"رواتب\" و\"أخرى\" ما بينوقفوا).")
            .Produces<ExpenseTypeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/expense-types/{id:guid}/move", async (Guid id, MoveExpenseTypeRequest request, MoveExpenseTypeHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new MoveExpenseTypeCommand(id, request.Up), cancellationToken)).ToHttpResult())
            .WithName("MoveExpenseType")
            .WithSummary("ترتيب الأنواع بأسهم ↑↓.");

        group.MapPost("/capital-transactions", async (
            CreateCapitalTransactionRequest request,
            CreateCapitalTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateCapitalTransactionCommand(
                request.BranchId, request.Type, request.Amount, request.OccurredAtUtc, request.Notes, request.PartnerId);

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
        Guid BranchId, ExpenseCategory? Category, decimal Amount, DateTime PaymentDateUtc,
        int PeriodYear, int PeriodMonth, string? Notes, Guid? ExpenseTypeId = null, bool PaidFromDrawer = false);

    public sealed record UpdateExpenseTypeRequest(string Name, bool IsActive);

    public sealed record MoveExpenseTypeRequest(bool Up);

    public sealed record CreateCapitalTransactionRequest(
        Guid BranchId, CapitalTransactionType Type, decimal Amount, DateTime OccurredAtUtc, string? Notes, Guid? PartnerId = null);
}
