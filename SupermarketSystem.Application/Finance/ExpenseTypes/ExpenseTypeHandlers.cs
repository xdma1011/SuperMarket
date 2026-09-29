using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Finance;

namespace SupermarketSystem.Application.Finance.ExpenseTypes;

// =====================================================================================
// أنواع المصاريف (29/9/2026، صاحب المشروع: "خليني انا اعرف نوع المصاريف") - تنظيف، صيانة، نقل... على مستوى الشركة.
// بلا حذف: نوع إله مصاريف بيضل بالتقارير القديمة، فبدل الحذف "إيقاف" (ما بيبين بقائمة التسجيل).
// الاسم فريد (بلا حساسية أحرف - collation القاعدة).
// =====================================================================================

public sealed record ExpenseTypeDto(Guid Id, string Name, bool IsActive, int SortOrder, bool IsBuiltIn, int ExpenseCount);

public sealed record GetExpenseTypesQuery(bool IncludeInactive);

public sealed record CreateExpenseTypeCommand(string Name);

public sealed record UpdateExpenseTypeCommand(Guid Id, string Name, bool IsActive);

public sealed record MoveExpenseTypeCommand(Guid Id, bool Up);

internal static class ExpenseTypeRules
{
    public static readonly IReadOnlySet<Guid> BuiltInIds = new HashSet<Guid>
    {
        ExpenseType.RentId, ExpenseType.ElectricityId, ExpenseType.WaterId,
        ExpenseType.SalaryId, ExpenseType.OtherId, ExpenseType.CleaningId
    };

    public static Error? ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return Error.Validation("ExpenseType.NameRequired", "اكتب اسم النوع.");
        }

        return trimmed.Length > ExpenseType.MaxNameLength
            ? Error.Validation("ExpenseType.NameTooLong", $"الاسم أطول من {ExpenseType.MaxNameLength} حرف.")
            : null;
    }

    public static async Task<bool> NameTakenAsync(IApplicationDbContext context, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        return await context.ExpenseTypes.AsNoTracking()
            .AnyAsync(t => t.Name == trimmed && (exceptId == null || t.Id != exceptId), cancellationToken);
    }
}

public sealed class GetExpenseTypesHandler
{
    private readonly IApplicationDbContext _context;

    public GetExpenseTypesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ExpenseTypeDto>> HandleAsync(GetExpenseTypesQuery query, CancellationToken cancellationToken)
    {
        var types = await _context.ExpenseTypes.AsNoTracking()
            .Where(t => query.IncludeInactive || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        // العدد على كل الفروع (بلا فلتر الفرع) - "هل النوع مستخدم" سؤال على مستوى الشركة.
        var counts = await _context.Expenses.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.ExpenseTypeId != null)
            .GroupBy(e => e.ExpenseTypeId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        return types
            .Select(t => new ExpenseTypeDto(t.Id, t.Name, t.IsActive, t.SortOrder, ExpenseTypeRules.BuiltInIds.Contains(t.Id), counts.GetValueOrDefault(t.Id)))
            .ToList();
    }
}

public sealed class CreateExpenseTypeHandler
{
    private readonly IApplicationDbContext _context;

    public CreateExpenseTypeHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ExpenseTypeDto>> HandleAsync(CreateExpenseTypeCommand command, CancellationToken cancellationToken)
    {
        if (ExpenseTypeRules.ValidateName(command.Name) is { } error)
        {
            return Result.Failure<ExpenseTypeDto>(error);
        }

        if (await ExpenseTypeRules.NameTakenAsync(_context, command.Name, null, cancellationToken))
        {
            return Result.Failure<ExpenseTypeDto>(Error.Conflict("ExpenseType.NameTaken", $"في نوع اسمه \"{command.Name.Trim()}\" من قبل."));
        }

        // قبل "أخرى" (99) - الأنواع الجديدة بتنحط بعد الموجودة.
        var maxSort = await _context.ExpenseTypes.AsNoTracking()
            .Where(t => t.Id != ExpenseType.OtherId)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var type = new ExpenseType(command.Name, maxSort + 1);
        _context.ExpenseTypes.Add(type);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new ExpenseTypeDto(type.Id, type.Name, type.IsActive, type.SortOrder, false, 0));
    }
}

public sealed class UpdateExpenseTypeHandler
{
    private readonly IApplicationDbContext _context;

    public UpdateExpenseTypeHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ExpenseTypeDto>> HandleAsync(UpdateExpenseTypeCommand command, CancellationToken cancellationToken)
    {
        if (ExpenseTypeRules.ValidateName(command.Name) is { } error)
        {
            return Result.Failure<ExpenseTypeDto>(error);
        }

        var type = await _context.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (type is null)
        {
            return Result.Failure<ExpenseTypeDto>(Error.NotFound("ExpenseType.NotFound", "النوع مش موجود."));
        }

        if (await ExpenseTypeRules.NameTakenAsync(_context, command.Name, type.Id, cancellationToken))
        {
            return Result.Failure<ExpenseTypeDto>(Error.Conflict("ExpenseType.NameTaken", $"في نوع اسمه \"{command.Name.Trim()}\" من قبل."));
        }

        // "رواتب" بتنستخدم تلقائيًا لرواتب الموظفين، و"أخرى" هي الاحتياط - الإيقاف ممنوع إلهم (تغيير الاسم مسموح).
        if (!command.IsActive && (type.Id == ExpenseType.SalaryId || type.Id == ExpenseType.OtherId))
        {
            return Result.Failure<ExpenseTypeDto>(Error.BusinessRule("ExpenseType.CannotDeactivate",
                "هالنوع ما بينوقف - النظام بيستخدمه (الرواتب بتنسجّل عليه تلقائيًا، و\"أخرى\" للمصاريف بلا نوع)."));
        }

        try
        {
            type.Rename(command.Name);
        }
        catch (DomainException ex)
        {
            return Result.Failure<ExpenseTypeDto>(Error.Validation("ExpenseType.Invalid", ex.Message));
        }

        type.SetActive(command.IsActive);
        await _context.SaveChangesAsync(cancellationToken);

        var count = await _context.Expenses.IgnoreQueryFilters().AsNoTracking().CountAsync(e => e.ExpenseTypeId == type.Id, cancellationToken);
        return Result.Success(new ExpenseTypeDto(type.Id, type.Name, type.IsActive, type.SortOrder, ExpenseTypeRules.BuiltInIds.Contains(type.Id), count));
    }
}

/// <summary>ترتيب القائمة بأسهم ↑↓ (نفس نمط وحدات القياس): تبديل الترتيب مع الجار.</summary>
public sealed class MoveExpenseTypeHandler
{
    private readonly IApplicationDbContext _context;

    public MoveExpenseTypeHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(MoveExpenseTypeCommand command, CancellationToken cancellationToken)
    {
        var types = await _context.ExpenseTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        var index = types.FindIndex(t => t.Id == command.Id);
        if (index < 0)
        {
            return Result.Failure(Error.NotFound("ExpenseType.NotFound", "النوع مش موجود."));
        }

        var neighbour = command.Up ? index - 1 : index + 1;
        if (neighbour < 0 || neighbour >= types.Count)
        {
            return Result.Success();
        }

        // إعادة ترقيم كاملة (1..N) قبل التبديل - بتصلّح أي ترتيب متساوي أو فجوات (مثل "أخرى" = 99).
        (types[index], types[neighbour]) = (types[neighbour], types[index]);
        for (var i = 0; i < types.Count; i++)
        {
            types[i].SetSortOrder(i + 1);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
