using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address_Street = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Address_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address_Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerOtpCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerOtpCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseBackups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseBackups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsSystemDefined = table.Column<bool>(type: "bit", nullable: false),
                    AffectsCashDrawer = table.Column<bool>(type: "bit", nullable: false),
                    RequiresExternalReference = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductCategories_ProductCategories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Address_Street = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Address_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address_Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramChatLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChatId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramChatLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitsOfMeasure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BranchDocumentSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    CurrentValue = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchDocumentSequences", x => x.Id);
                    table.CheckConstraint("CK_BranchDocumentSequences_CurrentValue_NonNegative", "[CurrentValue] >= 0");
                    table.ForeignKey(
                        name: "FK_BranchDocumentSequences_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Discounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Discounts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDeviceTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Platform = table.Column<int>(type: "int", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDeviceTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDeviceTokens_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsBatchTracked = table.Column<bool>(type: "bit", nullable: false),
                    SuggestedRetailPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ExpectedShelfLifeDays = table.Column<int>(type: "int", nullable: true),
                    IsComplimentaryAllowed = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_ProductCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SupplierInvoiceReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalPaidAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashClosings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CountedCash = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashClosings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashClosings_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashClosings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashDrawerLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementType = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashDrawerLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashDrawerLogs_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashDrawerLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stocktakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StocktakeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocktakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stocktakes_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stocktakes_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DispatchedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Branches_DestinationBranchId",
                        column: x => x.DestinationBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Branches_SourceBranchId",
                        column: x => x.SourceBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Users_DispatchedByUserId",
                        column: x => x.DispatchedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Users_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SuspendedSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspendedSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuspendedSales_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SuspendedSales_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBranches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserBranches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceIdentifier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsTrusted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLoginLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLoginLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLoginLogs_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserLoginLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppType = table.Column<int>(type: "int", nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceInfo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevocationReason = table.Column<int>(type: "int", nullable: true),
                    LastRefreshedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CustomerPhoneSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountAmountSnapshot = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalPaidAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalReturnedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<int>(type: "int", nullable: true),
                    VoidNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleInvoices_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoices_Discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "Discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SaleInvoices_Users_VoidedByUserId",
                        column: x => x.VoidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBatches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductBranches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellingPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    MinimumStock = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    MaximumStock = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    IsAvailableForSale = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBranches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductNotes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    IsBaseUnit = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductUnits_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RawSupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MatchedSupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierInvoiceReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ExtractedInvoiceTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ExtractionConfidence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WarningsText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResultingPurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaidNowAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PaidNowPaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceDrafts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceDrafts_PaymentMethods_PaidNowPaymentMethodId",
                        column: x => x.PaidNowPaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceDrafts_PurchaseInvoices_ResultingPurchaseInvoiceId",
                        column: x => x.ResultingPurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceDrafts_Suppliers_MatchedSupplierId",
                        column: x => x.MatchedSupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceImages_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoicePayments_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoicePayments_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoicePayments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashClosingDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashClosingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CountedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashClosingDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashClosingDetails_CashClosings_CashClosingId",
                        column: x => x.CashClosingId,
                        principalTable: "CashClosings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CashClosingDetails_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DeliveryNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DeliveryLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: true),
                    DeliveryLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DriverAssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultingSaleInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    RatingComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_SaleInvoices_ResultingSaleInvoiceId",
                        column: x => x.ResultingSaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Users_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OriginalSaleInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalRefundedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInvoices_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInvoices_SaleInvoices_OriginalSaleInvoiceId",
                        column: x => x.OriginalSaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInvoices_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleInvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleInvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleInvoicePayments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoicePayments_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoicePayments_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SaleInvoicePayments_Users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoicePayments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stocks_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stocks_ProductBatches_ProductBatchId",
                        column: x => x.ProductBatchId,
                        principalTable: "ProductBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CountedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CountedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeItems_ProductBatches_ProductBatchId",
                        column: x => x.ProductBatchId,
                        principalTable: "ProductBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeItems_Stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "Stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StocktakeItems_Users_CountedByUserId",
                        column: x => x.CountedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RequestedPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceChangeRequests_ProductBranches_ProductBranchId",
                        column: x => x.ProductBranchId,
                        principalTable: "ProductBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceChangeRequests_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceChangeRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductBarcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BarcodeValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBarcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBarcodes_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_ProductBatches_ProductBatchId",
                        column: x => x.ProductBatchId,
                        principalTable: "ProductBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountSnapshot = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuantityReturned = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleInvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleInvoiceItems_Discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "Discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SaleInvoiceItems_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoiceItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoiceItems_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuantityBase = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    MovementType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_ProductBatches_ProductBatchId",
                        column: x => x.ProductBatchId,
                        principalTable: "ProductBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityBase = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SourceProductBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BatchExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DestinationProductBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_ProductBatches_DestinationProductBatchId",
                        column: x => x.DestinationProductBatchId,
                        principalTable: "ProductBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_ProductBatches_SourceProductBatchId",
                        column: x => x.SourceProductBatchId,
                        principalTable: "ProductBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuspendedSaleItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuspendedSaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspendedSaleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuspendedSaleItems_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SuspendedSaleItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SuspendedSaleItems_SuspendedSales_SuspendedSaleId",
                        column: x => x.SuspendedSaleId,
                        principalTable: "SuspendedSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Complaints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Complaints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Complaints_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Complaints_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerLoyaltyPointsEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLoyaltyPointsEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerLoyaltyPointsEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerLoyaltyPointsEntries_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    EstimatedUnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnInvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInvoicePayments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInvoicePayments_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInvoicePayments_ReturnInvoices_ReturnInvoiceId",
                        column: x => x.ReturnInvoiceId,
                        principalTable: "ReturnInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnInvoicePayments_Users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInvoicePayments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleInvoiceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInvoiceItems_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInvoiceItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInvoiceItems_ReturnInvoices_ReturnInvoiceId",
                        column: x => x.ReturnInvoiceId,
                        principalTable: "ReturnInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnInvoiceItems_SaleInvoiceItems_SaleInvoiceItemId",
                        column: x => x.SaleInvoiceItemId,
                        principalTable: "SaleInvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PaymentMethods",
                columns: new[] { "Id", "AffectsCashDrawer", "Code", "CreatedAtUtc", "CreatedByUserId", "IsActive", "IsSystemDefined", "Name", "RequiresExternalReference", "SortOrder", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("4fa2a3fd-dd6d-4207-a330-c6b33af0c8bf"), true, "CASH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, true, "Cash", false, 1, null, null },
                    { new Guid("637959dc-3e36-44d2-906f-db46992911e5"), false, "VISA", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, true, "Visa", true, 2, null, null },
                    { new Guid("f6c90807-33bd-4018-b7a3-9d0f4f70a553"), false, "CLIQ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, true, "CliQ", true, 3, null, null }
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "Description", "Name", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("2a4f7c1e-9b3d-4e5a-8c6f-1d2e3a4b5c6d"), "Purchasing.CreateDraft", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Upload a purchase invoice image, run AI extraction, and save the result as a draft pending review - does not create or approve a real purchase invoice.", "Create AI-extracted purchase invoice drafts", null, null },
                    { new Guid("3b5e8d2f-ac4e-4f6b-9d70-2e3f4a5b6c7d"), "Customers.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "List customers and block/unblock a customer from placing new orders through the customer app.", "Manage customers", null, null },
                    { new Guid("3d939df0-3319-4dc7-ad83-cd0567607e8a"), "Sales.Void", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Void a completed sale, reversing stock, payments, and drawer entries.", "Void sales", null, null },
                    { new Guid("3f90797a-d3cd-482e-acad-5187542a5326"), "Inventory.ComplimentaryIssue", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Issue stock as complimentary/internal consumption, with no revenue entry.", "Record complimentary issues", null, null },
                    { new Guid("48b7a02d-05b2-426d-b945-2173b29714db"), "Backups.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Trigger, list, download, and delete database backups.", "Manage backups", null, null },
                    { new Guid("4da27a4a-a381-4225-b7da-9ad63fc3c963"), "Catalog.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Create/edit products, categories, units, and barcodes.", "Manage catalog", null, null },
                    { new Guid("526311ff-3ca8-4533-b4f4-5ae6f375c14c"), "Notifications.View", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "View the in-app notification feed.", "View notifications", null, null },
                    { new Guid("56e2faed-431e-464c-a808-5f1bd84046c5"), "Suppliers.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Create/edit suppliers.", "Manage suppliers", null, null },
                    { new Guid("5f14a6c3-3b89-4512-9799-3b25ecdefb40"), "Sales.Create", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Complete a sale at the register.", "Complete sales", null, null },
                    { new Guid("639380b3-12ab-41d6-adec-478169776a53"), "Reports.View", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "View all reporting endpoints.", "View reports", null, null },
                    { new Guid("69c92d8d-cf96-4a66-b1b4-b8149ab8f0ca"), "Returns.Review", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Mark a return as administratively reviewed.", "Mark returns reviewed", null, null },
                    { new Guid("6b1f4a8d-2c77-4e0a-9c5a-1f6e0d3a7b2c"), "System.SettingsManage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "View and change sensitive POS/inventory toggle settings (void/return/discount limits, etc.).", "Manage sensitive system settings", null, null },
                    { new Guid("81776805-df39-4a6a-a395-60df218bf010"), "Purchasing.Create", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Record a purchase invoice, including AI-assisted image extraction.", "Record purchases", null, null },
                    { new Guid("82ebffce-eba3-4dc3-9fea-f9b0ff5d058a"), "Stocktake.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Create stocktakes, record counts, complete counting.", "Manage stocktakes", null, null },
                    { new Guid("92cfde3f-6a23-48a9-ada8-27adb926af76"), "Returns.Process", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Process a customer return.", "Process returns", null, null },
                    { new Guid("959d574d-9b51-46ac-9c13-45ef2ea11a07"), "Catalog.ChangePriceDirect", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Change a product's selling price at a branch immediately, with no approval needed.", "Change selling price directly", null, null },
                    { new Guid("9a5adc62-5085-48a4-a218-2de2045bb24a"), "Users.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Create users and assign roles/branches to them.", "Manage users", null, null },
                    { new Guid("a7fc0954-e9d6-4c47-af8a-4620d9faf6f0"), "CashClosing.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Complete a branch's daily cash closing.", "Manage cash closings", null, null },
                    { new Guid("b35080f8-7b67-4965-a16b-2a9b84cb0827"), "Sessions.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "View active sessions and revoke them administratively.", "Manage sessions", null, null },
                    { new Guid("b758b4b2-c9df-4764-aa98-c60a71aff35b"), "StockTransfer.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Dispatch a stock transfer from one branch and receive it at another.", "Manage stock transfers", null, null },
                    { new Guid("bc142cda-a285-4e26-9f90-64208cc270fa"), "Stocktake.Approve", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Approve a completed stocktake, applying corrections to stock.", "Approve stocktakes", null, null },
                    { new Guid("e1d2c3b4-a5f6-4a7b-8c9d-0e1f2a3b4c5d"), "Orders.Deliver", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "View orders assigned to the current driver and confirm delivery/payment.", "Deliver orders", null, null },
                    { new Guid("ece946d9-376c-48e4-baa1-d076180d6251"), "Catalog.RequestPriceChange", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Submit a request to change a product's selling price at a branch - takes effect only once someone with Catalog.ChangePriceDirect approves it.", "Request selling price change", null, null },
                    { new Guid("f29bb266-8361-4ed2-aee4-4926ebe4f021"), "Branches.Manage", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Create/edit branches.", "Manage branches", null, null },
                    { new Guid("f2f8a36f-f4d1-4f2b-a1ba-18be8c023f34"), "System.CrossBranchAccess", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Bypasses the branch isolation query filter entirely. Grant only to head-office/support roles.", "Cross-branch access", null, null }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "IsActive", "Name", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "كل الصلاحيات — للدعم والصيانة والإدارة الكاملة.", true, "Master Admin", null, null },
                    { new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "إدارة يومية كاملة (مبيعات، مشتريات، كتالوج، جرد، تقارير) بلا النسخ الاحتياطي أو إدارة الجلسات/الفروع/المستخدمين.", true, "مساعد أدمن", null, null },
                    { new Guid("6e8f9a1b-2c3d-4e5f-8a9b-1c2d3e4f5a6b"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "يشوف الطلبات المسندة له للتوصيل بس، ويؤكد التسليم والدفع.", true, "سائق", null, null },
                    { new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "بيع، إلغاء بيع، معالجة إرجاع — بلا أي صلاحية إدارية.", true, "كاشير", null, null }
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value" },
                values: new object[,]
                {
                    { new Guid("032121de-b688-4ee9-ab20-e5a16af6fe4f"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Returns above this value are still completed immediately, but flagged for management review. 0 disables value-based flagging.", "Pos.HighValueReturnThreshold", null, null, "0" },
                    { new Guid("1c3e6a52-8f3a-4c6a-9f6e-2a6f7e9b0c1d"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Local directory where uploaded purchase-invoice images are stored as WebP, relative to the API process's working directory unless an absolute path is given.", "Storage.PurchaseInvoiceImagesDirectory", null, null, "PurchaseInvoiceImages" },
                    { new Guid("445269e2-e8f6-482b-95da-d8a829f9e14e"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow cashiers to process customer returns.", "Pos.AllowReturn", null, null, "true" },
                    { new Guid("50a800fa-ae5d-4f4e-a50b-ff8adb30e7ff"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Telegram chat id notifications are sent to. Empty disables the Telegram channel.", "Notifications.TelegramChatId", null, null, "" },
                    { new Guid("5fddc559-554c-4f20-8923-bfb5c1bb7c6e"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Cash-closing variance (absolute value) above which a notification is sent. 0 disables the alert.", "CashClosing.VarianceAlertThreshold", null, null, "0" },
                    { new Guid("64b0776a-6271-4c87-a1ab-bdfeb3b50361"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Maximum ad-hoc discount as a percentage of the line/invoice total. Set to 0 to disable manual discounts.", "Pos.MaxManualDiscountPercentage", null, null, "10" },
                    { new Guid("67264658-2e77-4241-8778-d5c8d20df993"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow a sale to proceed even when system stock is insufficient. Stock goes negative and is flagged for review rather than blocking the sale.", "Inventory.AllowNegativeStock", null, null, "true" },
                    { new Guid("7a2f4e91-3b6c-4d8a-9e1f-5c7b8a9d0e2f"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Consecutive failed login attempts before temporary lockout. 0 disables lockout entirely.", "Auth.MaxFailedLoginAttempts", null, null, "5" },
                    { new Guid("8b3f5e92-4c7d-4e9b-0f2a-6d8c9b0e1f3a"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Lockout duration in minutes once the failed-attempt threshold is reached.", "Auth.LockoutDurationMinutes", null, null, "15" },
                    { new Guid("99133340-d81f-4202-bebe-294ed26f0c41"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow cashiers to void a completed sale. Voids never wait for approval; they are recorded and flagged for review.", "Pos.AllowVoidSale", null, null, "true" },
                    { new Guid("9e2fa6ca-2101-4c8f-bbf9-fe594f468e7f"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Telegram bot token. Empty disables the Telegram channel (silent, no error).", "Notifications.TelegramBotToken", null, null, "" },
                    { new Guid("a4c9d6e1-3b7f-4a05-9c8e-9f2d5b0a7e34"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Quantity sold within the query period at or above which a product is classified as 'Low' consumption (below Medium). Zero sales is always 'NearZero'.", "ConsumptionLevel.LowThreshold", null, null, "1" },
                    { new Guid("a7bd1207-ab41-4a51-bdd6-a7aed05d0b5d"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Gemini Flash model name for the second attempt.", "Ai.GeminiFlashModelName", null, null, "gemini-flash-latest" },
                    { new Guid("abd250d3-ef22-415c-b9fd-81a5850fc3d7"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow refunding to a payment method other than the original sale's. Off by default: a cash refund against a card sale removes cash that never entered the drawer.", "Pos.AllowCrossMethodRefund", null, null, "false" },
                    { new Guid("b5d0e7f2-4c8a-4b16-9d9f-0a3e6c1b8f45"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Global catalog version counter — incremented atomically on every product/category/unit/price change. The cashier app (offline-first) polls this cheaply to know when to pull a full catalog re-sync.", "Catalog.Version", null, null, "1" },
                    { new Guid("ba427a27-4945-401b-954f-a090612ec2aa"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Gemini model name for the primary attempt. 'latest' aliases are maintained by Google to always point at the current recommended model.", "Ai.GeminiProModelName", null, null, "gemini-pro-latest" },
                    { new Guid("ba5a86fd-e696-4aef-ad52-25257d882014"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Claude API key. Empty disables the last-resort fallback provider (silent, no error).", "Ai.ClaudeApiKey", null, null, "" },
                    { new Guid("c15d8f3a-6e2b-4a91-b7d4-9f0e3c5a8b1d"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Daily quantity threshold (per product, across all branches) before a complimentary issue is auto-flagged for review. Never blocks — allow-with-review only.", "Complimentary.DailyReviewThresholdQuantity", null, null, "10" },
                    { new Guid("c2142398-e389-4c50-8ccf-1773249029d8"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Gemini API key. Empty disables both Gemini providers (silent, no error).", "Ai.GeminiApiKey", null, null, "" },
                    { new Guid("c536de35-d0ed-42ac-b551-b3851118015b"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow ad-hoc discounts keyed in at checkout.", "Pos.AllowManualDiscount", null, null, "true" },
                    { new Guid("d6e1f8a3-5c9b-4d27-8e4a-1b6c9d2e5f78"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Days a pending review item (unreviewed return, or NeedsReview stock movement) can stay unreviewed before PendingReviewEscalationBackgroundService flags it in an escalation notification.", "PendingReview.EscalationThresholdDays", null, null, "3" },
                    { new Guid("e26ab4e9-5c07-4ad6-96d8-0732494eb625"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow reversing a completed payment. The original payment is preserved; a reversal record is added.", "Pos.AllowPaymentReversal", null, null, "true" },
                    { new Guid("e2a7b4c9-1f5d-4e83-9a6c-7d0b3f8e5c12"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Quantity sold within the query period (base unit) at or above which a product is classified as 'High' consumption.", "ConsumptionLevel.HighThreshold", null, null, "50" },
                    { new Guid("e5f99833-a059-4192-92c7-33ed0d662169"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Claude model name for the final fallback attempt.", "Ai.ClaudeModelName", null, null, "claude-sonnet-5" },
                    { new Guid("f3b8c5d0-2a6e-4f94-8b7d-8e1c4a9f6d23"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Quantity sold within the query period at or above which a product is classified as 'Medium' consumption (below High).", "ConsumptionLevel.MediumThreshold", null, null, "15" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Email", "FullName", "IsActive", "IsDeleted", "PasswordHash", "UpdatedAtUtc", "UpdatedByUserId", "Username" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "system@local.invalid", "System", false, false, null, null, null, "system" });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("055aae7f-b0f9-48b4-986a-e94a834ef7aa"), new Guid("526311ff-3ca8-4533-b4f4-5ae6f375c14c"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("079fa3eb-78ef-445e-983d-96fa917beb84"), new Guid("81776805-df39-4a6a-a395-60df218bf010"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("0d5da59d-e108-4d74-9729-498acca8b9ab"), new Guid("9a5adc62-5085-48a4-a218-2de2045bb24a"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("10e2049c-998d-42f6-b326-2519885e9c3f"), new Guid("92cfde3f-6a23-48a9-ada8-27adb926af76"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f"), new Guid("2a4f7c1e-9b3d-4e5a-8c6f-1d2e3a4b5c6d"), new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c") },
                    { new Guid("1c66e7ba-28bf-490d-898b-791a06babd50"), new Guid("b758b4b2-c9df-4764-aa98-c60a71aff35b"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("216674c2-ccea-496f-b537-27f8fe8e78c0"), new Guid("a7fc0954-e9d6-4c47-af8a-4620d9faf6f0"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("34ee2ce3-a5b8-4ff2-bd74-67158ef1a382"), new Guid("69c92d8d-cf96-4a66-b1b4-b8149ab8f0ca"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("3511cbcc-340d-4d91-9f48-71c8b5f0a15c"), new Guid("4da27a4a-a381-4225-b7da-9ad63fc3c963"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("382e7005-6f53-41bd-a32c-ccb564fc73a2"), new Guid("82ebffce-eba3-4dc3-9fea-f9b0ff5d058a"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("41607168-90c0-4f99-8e1b-abb16b2a4585"), new Guid("ece946d9-376c-48e4-baa1-d076180d6251"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("4310626b-7558-43de-aef5-515bb654cb10"), new Guid("3f90797a-d3cd-482e-acad-5187542a5326"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("482a76e3-c0ec-42d5-9220-7787ea4ff504"), new Guid("959d574d-9b51-46ac-9c13-45ef2ea11a07"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("4c6f9e3a-bd5f-4a7c-8e91-3f4a5b6c7d8e"), new Guid("3b5e8d2f-ac4e-4f6b-9d70-2e3f4a5b6c7d"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("5631c8ca-28bb-4525-85b8-5e5221a069a7"), new Guid("b35080f8-7b67-4965-a16b-2a9b84cb0827"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("5d7fa04b-ce6a-4b8d-af02-4a5b6c7d8e9f"), new Guid("3b5e8d2f-ac4e-4f6b-9d70-2e3f4a5b6c7d"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("5f69cc23-c630-4325-b637-118f5117a9a1"), new Guid("a7fc0954-e9d6-4c47-af8a-4620d9faf6f0"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("607b2b4c-4ce1-402f-b958-b5e1ececf160"), new Guid("ece946d9-376c-48e4-baa1-d076180d6251"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("6271e28d-6960-4731-af91-92b6c43b9972"), new Guid("639380b3-12ab-41d6-adec-478169776a53"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("64dfaefb-0314-4cd4-8357-6ee33f20ae91"), new Guid("3d939df0-3319-4dc7-ad83-cd0567607e8a"), new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c") },
                    { new Guid("688b93ca-3c2c-44f8-b99b-e4b55f350920"), new Guid("f2f8a36f-f4d1-4f2b-a1ba-18be8c023f34"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("7e2c9a1b-4f6d-4a3e-8b0c-9d1e2f3a4b5c"), new Guid("6b1f4a8d-2c77-4e0a-9c5a-1f6e0d3a7b2c"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("80f8e95f-c65e-488b-bb00-1eaec2e6a44d"), new Guid("5f14a6c3-3b89-4512-9799-3b25ecdefb40"), new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c") },
                    { new Guid("829bb241-1d64-499f-a090-bb3da34023e3"), new Guid("b758b4b2-c9df-4764-aa98-c60a71aff35b"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("84de41e8-1f8c-4b03-ac20-c596924183db"), new Guid("3d939df0-3319-4dc7-ad83-cd0567607e8a"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("8f3a2c1d-6e4b-4a5c-9d7e-3b1f2a4c5d6e"), new Guid("2a4f7c1e-9b3d-4e5a-8c6f-1d2e3a4b5c6d"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("951cdb12-8fdb-41e6-8960-89b08d22e0e8"), new Guid("f29bb266-8361-4ed2-aee4-4926ebe4f021"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("974345e8-0faf-4823-aba8-9437c83fe0b9"), new Guid("82ebffce-eba3-4dc3-9fea-f9b0ff5d058a"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d"), new Guid("2a4f7c1e-9b3d-4e5a-8c6f-1d2e3a4b5c6d"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("9e1b38a1-05ce-48a9-b500-837a51e0cf8c"), new Guid("959d574d-9b51-46ac-9c13-45ef2ea11a07"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("9f769ab6-d52c-4c4a-9dd0-abb2b4b9263b"), new Guid("92cfde3f-6a23-48a9-ada8-27adb926af76"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("a1196dff-6fd2-4dfd-b3be-7da022bb309d"), new Guid("526311ff-3ca8-4533-b4f4-5ae6f375c14c"), new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c") },
                    { new Guid("b42b4489-814c-4116-8f94-33a13aeff689"), new Guid("56e2faed-431e-464c-a808-5f1bd84046c5"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("b44fe883-0ad6-448d-9603-35a063d0d728"), new Guid("5f14a6c3-3b89-4512-9799-3b25ecdefb40"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("b9f359f1-ffdb-4d7d-9db5-cfa3b3f3e122"), new Guid("81776805-df39-4a6a-a395-60df218bf010"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("bd5bba81-0510-472b-b14f-855764ab6ada"), new Guid("bc142cda-a285-4e26-9f90-64208cc270fa"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("c9d8e7f6-2b3c-4d5e-9f0a-2b3c4d5e6f70"), new Guid("e1d2c3b4-a5f6-4a7b-8c9d-0e1f2a3b4c5d"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("cab8d06b-9fd1-4ab7-afd5-e9dbb8ed2cf7"), new Guid("69c92d8d-cf96-4a66-b1b4-b8149ab8f0ca"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("ce0e0a63-0cea-4b22-a41d-86c02fdc81d8"), new Guid("5f14a6c3-3b89-4512-9799-3b25ecdefb40"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("ced90d30-c920-4a8a-909a-bec91589e7c2"), new Guid("56e2faed-431e-464c-a808-5f1bd84046c5"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("d08e1cd4-4392-47bf-be95-150da97ef0ec"), new Guid("526311ff-3ca8-4533-b4f4-5ae6f375c14c"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("d0e1f2a3-3c4d-4e5f-a0b1-3c4d5e6f7081"), new Guid("e1d2c3b4-a5f6-4a7b-8c9d-0e1f2a3b4c5d"), new Guid("6e8f9a1b-2c3d-4e5f-8a9b-1c2d3e4f5a6b") },
                    { new Guid("dc00f031-eb4c-4951-88a7-d9349a66da88"), new Guid("4da27a4a-a381-4225-b7da-9ad63fc3c963"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("e0887452-d701-4828-848b-61dfcec97f12"), new Guid("3d939df0-3319-4dc7-ad83-cd0567607e8a"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") },
                    { new Guid("e456ce19-d200-4c7b-bc9c-e3b58dbe6778"), new Guid("92cfde3f-6a23-48a9-ada8-27adb926af76"), new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c") },
                    { new Guid("ea9c16ac-f74d-4234-9d53-16a530cea009"), new Guid("bc142cda-a285-4e26-9f90-64208cc270fa"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("ec9db506-e0de-4c11-b61e-6597f2b74942"), new Guid("48b7a02d-05b2-426d-b945-2173b29714db"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("eeafbecf-28f8-49f6-9754-8fc320b608de"), new Guid("639380b3-12ab-41d6-adec-478169776a53"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_BranchId_OccurredAtUtc",
                table: "AuditLogs",
                columns: new[] { "BranchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchDocumentSequences_BranchId_DocumentType",
                table: "BranchDocumentSequences",
                columns: new[] { "BranchId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashClosingDetails_CashClosingId",
                table: "CashClosingDetails",
                column: "CashClosingId");

            migrationBuilder.CreateIndex(
                name: "IX_CashClosingDetails_PaymentMethodId",
                table: "CashClosingDetails",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_BranchId_BusinessDate",
                table: "CashClosings",
                columns: new[] { "BranchId", "BusinessDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_UserId",
                table: "CashClosings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawerLogs_BranchId_OccurredAtUtc",
                table: "CashDrawerLogs",
                columns: new[] { "BranchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawerLogs_ReferenceType_ReferenceId",
                table: "CashDrawerLogs",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawerLogs_UserId",
                table: "CashDrawerLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_CustomerId",
                table: "Complaints",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_IsResolved_CreatedAtUtc",
                table: "Complaints",
                columns: new[] { "IsResolved", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_OrderId",
                table: "Complaints",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeviceTokens_CustomerId",
                table: "CustomerDeviceTokens",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeviceTokens_Token",
                table: "CustomerDeviceTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLoyaltyPointsEntries_CustomerId",
                table: "CustomerLoyaltyPointsEntries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLoyaltyPointsEntries_OrderId",
                table: "CustomerLoyaltyPointsEntries",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotes_CustomerId",
                table: "CustomerNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOtpCodes_Phone_CreatedAtUtc",
                table: "CustomerOtpCodes",
                columns: new[] { "Phone", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedAtUtc",
                table: "Customers",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Phone",
                table: "Customers",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseBackups_CreatedAtUtc",
                table: "DatabaseBackups",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Discounts_BranchId",
                table: "Discounts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_NotificationId",
                table: "NotificationLogs",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Channel_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "Channel", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TargetUserId_Status",
                table: "Notifications",
                columns: new[] { "TargetUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSettings_UserId_NotificationCode",
                table: "NotificationSettings",
                columns: new[] { "UserId", "NotificationCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductUnitId",
                table: "OrderItems",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId_Status_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DecidedByUserId",
                table: "Orders",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DriverId_Status",
                table: "Orders",
                columns: new[] { "DriverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ResultingSaleInvoiceId",
                table: "Orders",
                column: "ResultingSaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_Code",
                table: "PaymentMethods",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeRequests_DecidedByUserId",
                table: "PriceChangeRequests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeRequests_ProductBranchId",
                table: "PriceChangeRequests",
                column: "ProductBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeRequests_RequestedByUserId",
                table: "PriceChangeRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeRequests_Status",
                table: "PriceChangeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_BarcodeValue",
                table: "ProductBarcodes",
                column: "BarcodeValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_ProductId",
                table: "ProductBarcodes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_ProductUnitId",
                table: "ProductBarcodes",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_BranchId_ProductId",
                table: "ProductBatches",
                columns: new[] { "BranchId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_ProductId",
                table: "ProductBatches",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBranches_BranchId_IsAvailableForSale",
                table: "ProductBranches",
                columns: new[] { "BranchId", "IsAvailableForSale" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBranches_ProductId_BranchId",
                table: "ProductBranches",
                columns: new[] { "ProductId", "BranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_ParentCategoryId",
                table: "ProductCategories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductNotes_ProductId",
                table: "ProductNotes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CreatedAtUtc",
                table: "Products",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_ProductId",
                table: "ProductUnits",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceDrafts_BranchId_Status_CreatedAtUtc",
                table: "PurchaseInvoiceDrafts",
                columns: new[] { "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceDrafts_MatchedSupplierId",
                table: "PurchaseInvoiceDrafts",
                column: "MatchedSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceDrafts_PaidNowPaymentMethodId",
                table: "PurchaseInvoiceDrafts",
                column: "PaidNowPaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceDrafts_ResultingPurchaseInvoiceId",
                table: "PurchaseInvoiceDrafts",
                column: "ResultingPurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceImages_PurchaseInvoiceId",
                table: "PurchaseInvoiceImages",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_NeedsReview_ReviewedAtUtc",
                table: "PurchaseInvoiceItems",
                columns: new[] { "NeedsReview", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_ProductBatchId",
                table: "PurchaseInvoiceItems",
                column: "ProductBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_ProductId",
                table: "PurchaseInvoiceItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_ProductUnitId",
                table: "PurchaseInvoiceItems",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_PurchaseInvoiceId",
                table: "PurchaseInvoiceItems",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_ReviewedByUserId",
                table: "PurchaseInvoiceItems",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_BranchId_CreatedAtUtc",
                table: "PurchaseInvoicePayments",
                columns: new[] { "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_ClientRequestId",
                table: "PurchaseInvoicePayments",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_PaymentMethodId_CreatedAtUtc",
                table: "PurchaseInvoicePayments",
                columns: new[] { "PaymentMethodId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_PurchaseInvoiceId",
                table: "PurchaseInvoicePayments",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_UserId",
                table: "PurchaseInvoicePayments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_BranchId_CreatedAtUtc",
                table: "PurchaseInvoices",
                columns: new[] { "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_BranchId_InvoiceNumber",
                table: "PurchaseInvoices",
                columns: new[] { "BranchId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierId",
                table: "PurchaseInvoices",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoiceItems_ProductId",
                table: "ReturnInvoiceItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoiceItems_ProductUnitId",
                table: "ReturnInvoiceItems",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoiceItems_ReturnInvoiceId",
                table: "ReturnInvoiceItems",
                column: "ReturnInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoiceItems_SaleInvoiceItemId",
                table: "ReturnInvoiceItems",
                column: "SaleInvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoicePayments_BranchId_CreatedAtUtc",
                table: "ReturnInvoicePayments",
                columns: new[] { "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoicePayments_ClientRequestId",
                table: "ReturnInvoicePayments",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoicePayments_ExternalReference",
                table: "ReturnInvoicePayments",
                column: "ExternalReference");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoicePayments_PaymentMethodId_CreatedAtUtc",
                table: "ReturnInvoicePayments",
                columns: new[] { "PaymentMethodId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoicePayments_ReturnInvoiceId",
                table: "ReturnInvoicePayments",
                column: "ReturnInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoicePayments_ReversedByUserId",
                table: "ReturnInvoicePayments",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoicePayments_UserId_CreatedAtUtc",
                table: "ReturnInvoicePayments",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoices_BranchId_CreatedAtUtc",
                table: "ReturnInvoices",
                columns: new[] { "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoices_BranchId_InvoiceNumber",
                table: "ReturnInvoices",
                columns: new[] { "BranchId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoices_ClientRequestId",
                table: "ReturnInvoices",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoices_OriginalSaleInvoiceId",
                table: "ReturnInvoices",
                column: "OriginalSaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInvoices_ReviewedByUserId",
                table: "ReturnInvoices",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceItems_DiscountId",
                table: "SaleInvoiceItems",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceItems_ProductId",
                table: "SaleInvoiceItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceItems_ProductUnitId",
                table: "SaleInvoiceItems",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceItems_SaleInvoiceId",
                table: "SaleInvoiceItems",
                column: "SaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoicePayments_BranchId_CreatedAtUtc",
                table: "SaleInvoicePayments",
                columns: new[] { "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoicePayments_ClientRequestId",
                table: "SaleInvoicePayments",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoicePayments_ExternalReference",
                table: "SaleInvoicePayments",
                column: "ExternalReference");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoicePayments_PaymentMethodId_CreatedAtUtc",
                table: "SaleInvoicePayments",
                columns: new[] { "PaymentMethodId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoicePayments_ReversedByUserId",
                table: "SaleInvoicePayments",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoicePayments_SaleInvoiceId",
                table: "SaleInvoicePayments",
                column: "SaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoicePayments_UserId_CreatedAtUtc",
                table: "SaleInvoicePayments",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_BranchId_InvoiceNumber",
                table: "SaleInvoices",
                columns: new[] { "BranchId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_BranchId_Status_CreatedAtUtc",
                table: "SaleInvoices",
                columns: new[] { "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_ClientRequestId",
                table: "SaleInvoices",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_CustomerId",
                table: "SaleInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_DiscountId",
                table: "SaleInvoices",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_VoidedByUserId",
                table: "SaleInvoices",
                column: "VoidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BranchId_ProductId_OccurredAtUtc",
                table: "StockMovements",
                columns: new[] { "BranchId", "ProductId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_NeedsReview",
                table: "StockMovements",
                column: "NeedsReview");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductBatchId",
                table: "StockMovements",
                column: "ProductBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductUnitId",
                table: "StockMovements",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReferenceType_ReferenceId",
                table: "StockMovements",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_UserId",
                table: "StockMovements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_BranchId",
                table: "Stocks",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProductBatchId",
                table: "Stocks",
                column: "ProductBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProductId_BranchId_NoBatch",
                table: "Stocks",
                columns: new[] { "ProductId", "BranchId" },
                unique: true,
                filter: "[ProductBatchId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_ProductId_BranchId_ProductBatchId",
                table: "Stocks",
                columns: new[] { "ProductId", "BranchId", "ProductBatchId" },
                unique: true,
                filter: "[ProductBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeItems_CountedByUserId",
                table: "StocktakeItems",
                column: "CountedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeItems_ProductBatchId",
                table: "StocktakeItems",
                column: "ProductBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeItems_ProductId",
                table: "StocktakeItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeItems_StocktakeId",
                table: "StocktakeItems",
                column: "StocktakeId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_ApprovedByUserId",
                table: "Stocktakes",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_BranchId_Status_CreatedAtUtc",
                table: "Stocktakes",
                columns: new[] { "BranchId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_BranchId_StocktakeNumber",
                table: "Stocktakes",
                columns: new[] { "BranchId", "StocktakeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_DestinationProductBatchId",
                table: "StockTransferItems",
                column: "DestinationProductBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_ProductId",
                table: "StockTransferItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_ProductUnitId",
                table: "StockTransferItems",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_SourceProductBatchId",
                table: "StockTransferItems",
                column: "SourceProductBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_StockTransferId",
                table: "StockTransferItems",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DestinationBranchId_Status",
                table: "StockTransfers",
                columns: new[] { "DestinationBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DispatchedByUserId",
                table: "StockTransfers",
                column: "DispatchedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ReceivedByUserId",
                table: "StockTransfers",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_SourceBranchId_TransferNumber",
                table: "StockTransfers",
                columns: new[] { "SourceBranchId", "TransferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Name",
                table: "Suppliers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSaleItems_ProductId",
                table: "SuspendedSaleItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSaleItems_ProductUnitId",
                table: "SuspendedSaleItems",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSaleItems_SuspendedSaleId",
                table: "SuspendedSaleItems",
                column: "SuspendedSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_BranchId_UserId",
                table: "SuspendedSales",
                columns: new[] { "BranchId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_UserId",
                table: "SuspendedSales",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChatLinks_Phone",
                table: "TelegramChatLinks",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_Name",
                table: "UnitsOfMeasure",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBranches_BranchId",
                table: "UserBranches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBranches_UserId_BranchId",
                table: "UserBranches",
                columns: new[] { "UserId", "BranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_UserId",
                table: "UserDevices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginLogs_BranchId",
                table: "UserLoginLogs",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginLogs_UserId_AttemptedAtUtc",
                table: "UserLoginLogs",
                columns: new[] { "UserId", "AttemptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_BranchId",
                table: "UserRoles",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId_BranchId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_BranchId",
                table: "UserSessions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_RefreshTokenHash",
                table: "UserSessions",
                column: "RefreshTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId_AppType_RevokedAtUtc",
                table: "UserSessions",
                columns: new[] { "UserId", "AppType", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId_Key",
                table: "UserSettings",
                columns: new[] { "UserId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BranchDocumentSequences");

            migrationBuilder.DropTable(
                name: "CashClosingDetails");

            migrationBuilder.DropTable(
                name: "CashDrawerLogs");

            migrationBuilder.DropTable(
                name: "Complaints");

            migrationBuilder.DropTable(
                name: "CustomerDeviceTokens");

            migrationBuilder.DropTable(
                name: "CustomerLoyaltyPointsEntries");

            migrationBuilder.DropTable(
                name: "CustomerNotes");

            migrationBuilder.DropTable(
                name: "CustomerOtpCodes");

            migrationBuilder.DropTable(
                name: "DatabaseBackups");

            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.DropTable(
                name: "NotificationSettings");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "PriceChangeRequests");

            migrationBuilder.DropTable(
                name: "ProductBarcodes");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "ProductNotes");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceDrafts");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceImages");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceItems");

            migrationBuilder.DropTable(
                name: "PurchaseInvoicePayments");

            migrationBuilder.DropTable(
                name: "ReturnInvoiceItems");

            migrationBuilder.DropTable(
                name: "ReturnInvoicePayments");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "SaleInvoicePayments");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "Stocks");

            migrationBuilder.DropTable(
                name: "StocktakeItems");

            migrationBuilder.DropTable(
                name: "StockTransferItems");

            migrationBuilder.DropTable(
                name: "SuspendedSaleItems");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TelegramChatLinks");

            migrationBuilder.DropTable(
                name: "UnitsOfMeasure");

            migrationBuilder.DropTable(
                name: "UserBranches");

            migrationBuilder.DropTable(
                name: "UserDevices");

            migrationBuilder.DropTable(
                name: "UserLoginLogs");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "CashClosings");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "ProductBranches");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");

            migrationBuilder.DropTable(
                name: "SaleInvoiceItems");

            migrationBuilder.DropTable(
                name: "ReturnInvoices");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "Stocktakes");

            migrationBuilder.DropTable(
                name: "ProductBatches");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "SuspendedSales");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "ProductUnits");

            migrationBuilder.DropTable(
                name: "SaleInvoices");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Discounts");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ProductCategories");

            migrationBuilder.DropTable(
                name: "Branches");
        }
    }
}
