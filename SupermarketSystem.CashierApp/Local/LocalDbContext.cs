using Microsoft.EntityFrameworkCore;

namespace SupermarketSystem.CashierApp.Local;

public sealed class LocalDbContext : DbContext
{
    private readonly string _dbPath;

    public LocalDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public DbSet<LocalProduct> Products => Set<LocalProduct>();
    public DbSet<LocalProductUnit> ProductUnits => Set<LocalProductUnit>();
    public DbSet<LocalProductBarcode> ProductBarcodes => Set<LocalProductBarcode>();
    public DbSet<LocalProductBatch> ProductBatches => Set<LocalProductBatch>();
    public DbSet<PendingSale> PendingSales => Set<PendingSale>();
    public DbSet<LocalPaymentMethod> PaymentMethods => Set<LocalPaymentMethod>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalProduct>().HasKey(p => p.ProductId);

        modelBuilder.Entity<LocalProductUnit>().HasKey(u => u.UnitId);
        modelBuilder.Entity<LocalProductUnit>().HasIndex(u => u.ProductId);

        modelBuilder.Entity<LocalProductBarcode>().HasKey(b => b.Id);
        modelBuilder.Entity<LocalProductBarcode>().HasIndex(b => b.BarcodeValue).IsUnique();

        modelBuilder.Entity<LocalProductBatch>().HasKey(b => b.BatchId);
        modelBuilder.Entity<LocalProductBatch>().HasIndex(b => b.ProductId);

        modelBuilder.Entity<PendingSale>().HasKey(s => s.Id);
        modelBuilder.Entity<PendingSale>().HasIndex(s => s.ClientRequestId).IsUnique();

        modelBuilder.Entity<LocalPaymentMethod>().HasKey(m => m.Id);

        modelBuilder.Entity<SyncState>().HasKey(s => s.Id);
    }
}
