using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <summary>
    /// بيانات بس، بلا Schema (29/9/2026): دفعة انشرت بالكرتونة كانت تنحفظ تكلفتها بسعر الكرتونة، وكل الكود بيقرأ
    /// ProductBatch.UnitCost كتكلفة حبة. هون بترجع تكلفة الحبة (سعر الكرتونة ÷ معاملها) - بس للدفعات اللي تكلفتها لسه
    /// نفس سعر سطر الشراء اللي أنشأها (يعني ما انعدّلت). مبيعات قديمة ما بتتغيّر (UnitCostSnapshot محفوظ وقتها).
    /// </summary>
    public partial class FixBatchUnitCostToBaseUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE b
SET b.UnitCost = ROUND(pii.UnitCost / pu.ConversionFactorToBase, 4)
FROM ProductBatches b
JOIN PurchaseInvoiceItems pii ON pii.ProductBatchId = b.Id
JOIN ProductUnits pu ON pu.Id = pii.ProductUnitId
WHERE pu.ConversionFactorToBase > 1
  AND b.UnitCost = pii.UnitCost;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // تصحيح بيانات - ما في رجوع للقيمة الغلط.
        }
    }
}
