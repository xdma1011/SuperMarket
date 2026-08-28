namespace SupermarketSystem.Domain.Common;

/// <summary>
/// The document types that need branch-scoped sequential numbering.
/// Add new members here as new numbered document types are introduced —
/// never repurpose an existing member's meaning.
/// </summary>
public enum DocumentType
{
    SaleInvoice = 1,
    PurchaseInvoice = 2,
    ReturnInvoice = 3,
    Stocktake = 4
}

/// <summary>
/// One row per (Branch, DocumentType). CurrentValue is reserved via a single
/// atomic `UPDATE ... SET CurrentValue = CurrentValue + 1 OUTPUT INSERTED.CurrentValue
/// WHERE BranchId = @b AND DocumentType = @t` statement (see
/// IDocumentNumberGenerator / its Infrastructure implementation) — the row
/// lock taken by that statement is what makes concurrent reservations for the
/// same branch safe without SERIALIZABLE isolation. This entity is
/// deliberately inert: it carries no behavior beyond being the row that gets
/// locked and incremented; the reservation logic lives in Infrastructure
/// because it's expressed as raw SQL, not a LINQ/SaveChanges operation.
///
/// Per Architecture Review §4: a rollback after reserving a number "burns"
/// it (a gap, never reused) — expected and accepted, identical to how a SQL
/// Server IDENTITY/SEQUENCE behaves.
/// </summary>
public class BranchDocumentSequence : Entity, IBranchOwned, IHasRowVersion
{
    public Guid BranchId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public long CurrentValue { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private BranchDocumentSequence() { } // EF Core

    public BranchDocumentSequence(Guid branchId, DocumentType documentType)
    {
        BranchId = branchId;
        DocumentType = documentType;
        CurrentValue = 0;
    }
}
