using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitOfMeasureSortOrderAndDefaultUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "UnitsOfMeasure",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // وحدات جاهزة شائعة بسوبرماركت أردني، بالترتيب الأكثر استخدامًا.
            // إضافية بالكامل: وحدة موجودة أصلًا بنفس الاسم ما بتنكرّر، بس
            // بتاخد رقم ترتيبها من هالقائمة. أي وحدة تانية ضافها صاحب المحل
            // بتنحط بعدها (أبجديًا). مش HasData عمدًا - HasData بيفشل لو
            // الاسم موجود أصلًا (فهرس فريد) وبيمنع تعديلها من الواجهة لاحقًا.
            migrationBuilder.Sql(@"
DECLARE @Defaults TABLE (Name nvarchar(50) NOT NULL, SortOrder int NOT NULL);
INSERT INTO @Defaults (Name, SortOrder) VALUES
    (N'حبة', 1), (N'كيلو', 2), (N'غرام', 3), (N'لتر', 4), (N'مل', 5),
    (N'علبة', 6), (N'كرتونة', 7), (N'باكيت', 8), (N'كيس', 9), (N'قنينة', 10),
    (N'ربطة', 11), (N'درزن', 12), (N'طبق', 13), (N'شدّة', 14), (N'جالون', 15),
    (N'رول', 16), (N'متر', 17), (N'سم', 18);

UPDATE u SET u.SortOrder = d.SortOrder
FROM UnitsOfMeasure u
INNER JOIN @Defaults d ON d.Name = u.Name;

INSERT INTO UnitsOfMeasure (Id, Name, IsActive, SortOrder, CreatedAtUtc)
SELECT NEWID(), d.Name, 1, d.SortOrder, SYSUTCDATETIME()
FROM @Defaults d
WHERE NOT EXISTS (SELECT 1 FROM UnitsOfMeasure u WHERE u.Name = d.Name);

;WITH Others AS (
    SELECT u.SortOrder, ROW_NUMBER() OVER (ORDER BY u.Name) AS rn
    FROM UnitsOfMeasure u
    WHERE NOT EXISTS (SELECT 1 FROM @Defaults d WHERE d.Name = u.Name)
)
UPDATE Others SET SortOrder = (SELECT MAX(SortOrder) FROM @Defaults) + rn;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // الوحدات الجاهزة المُضافة بتضل عمدًا (ما في طريقة نميّزها عن وحدة
            // ضافها صاحب المحل بنفس الاسم، وممكن تكون مستخدمة بمنتجات).
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "UnitsOfMeasure");
        }
    }
}
