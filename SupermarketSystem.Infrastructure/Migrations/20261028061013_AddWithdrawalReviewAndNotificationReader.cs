using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalReviewAndNotificationReader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BalanceBefore",
                table: "PartnerWithdrawals",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "PartnerWithdrawals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "PartnerWithdrawals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "PartnerWithdrawals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReadByUserId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerWithdrawals_ReviewedByUserId",
                table: "PartnerWithdrawals",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ReadByUserId",
                table: "Notifications",
                column: "ReadByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_ReadByUserId",
                table: "Notifications",
                column: "ReadByUserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PartnerWithdrawals_Users_ReviewedByUserId",
                table: "PartnerWithdrawals",
                column: "ReviewedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Users_ReadByUserId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_PartnerWithdrawals_Users_ReviewedByUserId",
                table: "PartnerWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_PartnerWithdrawals_ReviewedByUserId",
                table: "PartnerWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ReadByUserId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "BalanceBefore",
                table: "PartnerWithdrawals");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "PartnerWithdrawals");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "PartnerWithdrawals");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "PartnerWithdrawals");

            migrationBuilder.DropColumn(
                name: "ReadByUserId",
                table: "Notifications");
        }
    }
}
