using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <summary>الكاشير ما بيشوف ولا تنبيه (1/10/2026، صاحب المشروع: "الكاشير ما يشوف اشي") - بتشيل صف البذر كاشير -> Notifications.View.
    /// ربط بتضيفه إنت يدويًا من صفحة الأدوار لاحقًا ما بيتأثر.</summary>
    public partial class RemoveNotificationsViewFromCashier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("a1196dff-6fd2-4dfd-b3be-7da022bb309d"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "PermissionId", "RoleId" },
                values: new object[] { new Guid("a1196dff-6fd2-4dfd-b3be-7da022bb309d"), new Guid("526311ff-3ca8-4533-b4f4-5ae6f375c14c"), new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c") });
        }
    }
}
