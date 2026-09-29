using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Employees;
using SupermarketSystem.Domain.Employees;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// الموظفين والرواتب والسلف (29/9/2026) - Finance.Manage (نفس صفحة المالية: الرواتب مصاريف). راجع EmployeeHandlers.cs.
/// </summary>
public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/employees")
            .WithTags("Employees")
            .RequirePermission(PermissionCodes.FinanceManage);

        group.MapGet("/", async (Guid? branchId, bool? includeInactive, GetEmployeesHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetEmployeesQuery(branchId, includeInactive ?? false), cancellationToken)))
            .WithName("GetEmployees")
            .WithSummary("الموظفين بالراتب الشهري والسلف المتبقية ومجموع الرواتب وآخر شهر انصرف.")
            .Produces<IReadOnlyList<EmployeeDto>>(StatusCodes.Status200OK);

        group.MapPost("/", async (CreateEmployeeCommand command, CreateEmployeeHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                return result.ToHttpResult(id => Results.Created($"/api/v1/employees/{id}", new { employeeId = id }));
            })
            .WithName("CreateEmployee")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{employeeId:guid}", async (Guid employeeId, UpdateEmployeeRequest request, UpdateEmployeeHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new UpdateEmployeeCommand(
                    employeeId, request.FullName, request.Phone, request.MonthlySalary, request.Notes, request.IsActive), cancellationToken)).ToHttpResult())
            .WithName("UpdateEmployee")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/payments", async (Guid? branchId, Guid? employeeId, int? year, GetEmployeePaymentsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(new GetEmployeePaymentsQuery(branchId, employeeId, year), cancellationToken)))
            .WithName("GetEmployeePayments")
            .WithSummary("سجل الرواتب والسلف (لموظف أو لفرع، وسنة اختيارية).")
            .Produces<IReadOnlyList<EmployeePaymentDto>>(StatusCodes.Status200OK);

        group.MapPost("/{employeeId:guid}/payments", async (Guid employeeId, RecordEmployeePaymentRequest request, RecordEmployeePaymentHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(new RecordEmployeePaymentCommand(
                    employeeId, request.Type, request.Amount, request.AdvanceDeducted, request.PeriodYear, request.PeriodMonth,
                    request.PaidFromDrawer, request.OccurredAtUtc, request.Notes, request.ClientRequestId), cancellationToken);
                return result.ToHttpResult(r => r.WasReplay ? Results.Ok(r) : Results.Created($"/api/v1/employees/payments/{r.PaymentId}", r));
            })
            .WithName("RecordEmployeePayment")
            .WithSummary("صرف راتب (بينسجّل مصروف رواتب بكامل الراتب، وبيخصم سلفة لو بدك) أو سلفة (مش مصروف). من الصندوق = بينقص المتوقع بالتقفيل.")
            .Produces<RecordEmployeePaymentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record UpdateEmployeeRequest(string FullName, string? Phone, decimal MonthlySalary, string? Notes, bool IsActive);

    public sealed record RecordEmployeePaymentRequest(
        EmployeePaymentType Type, decimal Amount, decimal AdvanceDeducted, int? PeriodYear, int? PeriodMonth,
        bool PaidFromDrawer, DateTime? OccurredAtUtc, string? Notes, Guid ClientRequestId);
}
