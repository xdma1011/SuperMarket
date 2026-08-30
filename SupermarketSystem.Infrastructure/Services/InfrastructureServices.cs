using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// PLACEHOLDER for Phase C only. Authentication is explicitly out of scope
/// (Architecture Review §34 / Phase C brief), but AppDbContext and the audit
/// interceptor both need ICurrentUserContext to construct, so something has
/// to satisfy it now.
///
/// IsCrossBranchAccessAllowed = true means the branch global query filter is
/// effectively inert until real authentication is wired up in a later phase.
/// That is the correct behaviour for a system with no users yet (migrations,
/// seeding and design-time tooling must see all data), but it MUST be
/// replaced before any real deployment — this class is the single place that
/// change happens.
/// </summary>
public sealed class PlaceholderCurrentUserContext : ICurrentUserContext
{
    public Guid? UserId => null;
    public Guid? BranchId => null;
    public bool IsCrossBranchAccessAllowed => true;
    public string? IpAddress => null;
    public Guid? CorrelationId => null;
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// The atomic branch-scoped numbering implementation (Architecture Review §4).
///
/// The reservation is a single statement. The UPDATE takes a row lock on
/// exactly one (BranchId, DocumentType) row for the duration of the
/// statement, so two POS terminals at the same branch serialize on it for a
/// few milliseconds and cannot receive the same number. Terminals at
/// different branches lock different rows and never contend. No SERIALIZABLE
/// isolation, no MAX+1 read-then-write race, no global IDENTITY.
///
/// Call this INSIDE the same transaction as the document being created, so
/// the reservation and the insert commit or roll back together.
///
/// Accepted trade-off (documented, not a defect): if the enclosing
/// transaction rolls back after reserving, the number is burned — a gap,
/// never reused. Identical to SQL Server IDENTITY/SEQUENCE behaviour.
/// </summary>
public sealed class DocumentNumberGenerator : IDocumentNumberGenerator
{
    private readonly AppDbContext _context;

    public DocumentNumberGenerator(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string> GetNextNumberAsync(Guid branchId, DocumentType documentType, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE [BranchDocumentSequences]
            SET [CurrentValue] = [CurrentValue] + 1
            OUTPUT INSERTED.[CurrentValue]
            WHERE [BranchId] = @branchId AND [DocumentType] = @documentType;
            """;

        var connection = _context.Database.GetDbConnection();
        await _context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new SqlParameter("@branchId", branchId));
            command.Parameters.Add(new SqlParameter("@documentType", (int)documentType));

            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result is null || result == DBNull.Value)
            {
                // No sequence row for this (branch, documentType). Creating
                // one lazily here would reintroduce a race between concurrent
                // first-use requests, so this is surfaced as an error instead:
                // sequence rows are provisioned when a branch is created.
                throw new InvalidOperationException(
                    $"No document sequence exists for branch {branchId} and document type {documentType}. " +
                    "Sequence rows must be provisioned when the branch is created.");
            }

            var sequenceValue = Convert.ToInt64(result);
            return Format(documentType, sequenceValue);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Display format only — the prefix/padding is a branding decision, not
    /// a data-integrity one, and was left open in the Architecture Review.
    /// Changing it does not affect the underlying counter mechanism. Note
    /// that the branch code is NOT included here (that would require a join
    /// on the hot path); uniqueness is guaranteed by the
    /// (BranchId, InvoiceNumber) unique constraint, not by the string.
    /// </summary>
    private static string Format(DocumentType documentType, long sequenceValue)
    {
        var prefix = documentType switch
        {
            DocumentType.SaleInvoice => "SI",
            DocumentType.PurchaseInvoice => "PI",
            DocumentType.ReturnInvoice => "RI",
            _ => "DOC"
        };

        return $"{prefix}-{sequenceValue:D6}";
    }
}
