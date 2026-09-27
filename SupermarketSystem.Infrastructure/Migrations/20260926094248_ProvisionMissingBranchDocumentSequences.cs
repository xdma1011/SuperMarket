using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <summary>
    /// بيانات بس، بلا أي تغيير Schema. أي فرع ناقصه عدّاد أرقام لنوع مستند
    /// بياخده هون بقيمة 0 - فرع انعمل بـbootstrap-admin (كان ما بينشئ
    /// العدّادات، انصلح) أو انضاف يدويًا بـSSMS. بدونها أي فاتورة
    /// (بيع/شراء/إرجاع/جرد/نقل) على هالفرع بترجع 500:
    /// "No document sequence exists for branch ...".
    ///
    /// إضافة فقط (NOT EXISTS): عدّاد موجود ما بينلمس، فأرقام الفواتير
    /// الحالية ما بتتكرر. أنواع المستندات مكتوبة حرفيًا (1..5) لا من الـenum -
    /// الـMigration لازم تضل ثابتة لو انضاف نوع جديد لاحقًا.
    /// </summary>
    public partial class ProvisionMissingBranchDocumentSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO [BranchDocumentSequences] ([Id], [BranchId], [DocumentType], [CurrentValue])
                SELECT NEWID(), b.[Id], t.[DocumentType], 0
                FROM [Branches] AS b
                CROSS JOIN (VALUES (1), (2), (3), (4), (5)) AS t([DocumentType])
                WHERE NOT EXISTS (
                    SELECT 1 FROM [BranchDocumentSequences] AS s
                    WHERE s.[BranchId] = b.[Id] AND s.[DocumentType] = t.[DocumentType]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // عمدًا بلا تراجع: حذف العدّادات بعد ما انستخدمت بيرجّع أرقام
            // الفواتير من الصفر (تكرار أرقام)، وما في طريقة نميّز الصفوف اللي
            // انضافت هون عن اللي كانت موجودة.
        }
    }
}
