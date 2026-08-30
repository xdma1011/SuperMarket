using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// يحوّل AuditLogs لجدول SQL Server Temporal (system-versioned) — أي
    /// UPDATE/DELETE مباشر على الجدول (حتى عبر SSMS، خارج التطبيق كليًا)
    /// بينسخ النسخة القديمة تلقائيًا لجدول AuditLogsHistory قبل التنفيذ.
    /// هذا يمنع تلاعبًا بصمت بسجل المراجعة نفسه من حد عنده وصول SQL مباشر
    /// — لا يمنعه من التعديل، لكن يخلّي أي تعديل مكتشَفًا دايمًا.
    ///
    /// ValidFrom/ValidTo/PERIOD ليست خصائص مُعرَّفة على AuditLog (Domain)
    /// ولا مُعدة بـAuditLogConfiguration — تُدار بالكامل من SQL Server
    /// نفسه، EF Core لا يعرف بوجودها ولا يحتاج يعرف. لهذا السبب موديل EF
    /// لا يتغيّر بهذه الـMigration (AppDbContextModelSnapshot يبقى كما هو).
    /// </summary>
    public partial class EnableAuditLogTemporalTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE [AuditLogs] ADD
    [ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL DEFAULT SYSUTCDATETIME(),
    [ValidTo] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL DEFAULT CONVERT(DATETIME2, '9999-12-31 23:59:59.9999999'),
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]);
");

            migrationBuilder.Sql(@"
ALTER TABLE [AuditLogs]
SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[AuditLogsHistory]));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // لازم نطفي System Versioning أول — SQL Server يرفض حذف عمود
            // GENERATED ALWAYS أو الـPERIOD وهو لسه شغّال.
            migrationBuilder.Sql(@"
ALTER TABLE [AuditLogs]
SET (SYSTEM_VERSIONING = OFF);
");

            migrationBuilder.Sql(@"
ALTER TABLE [AuditLogs] DROP PERIOD FOR SYSTEM_TIME;
");

            migrationBuilder.Sql(@"
ALTER TABLE [AuditLogs] DROP COLUMN [ValidFrom], [ValidTo];
");

            // جدول التاريخ (AuditLogsHistory) يبقى مقصودًا حتى بعد الـDown —
            // فيه سجلات تدقيق حقيقية، حذفه تلقائيًا يخالف قاعدة "ممنوع حذف
            // بيانات فعلية". لو التراجع الكامل مطلوب فعلًا، يُحذف يدويًا
            // بعد مراجعة صريحة.
        }
    }
}
