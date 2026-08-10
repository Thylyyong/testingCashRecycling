using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SelfCheckoutKiosk.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialKioskPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicenseConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HardwareId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxKiosks = table.Column<int>(type: "INTEGER", nullable: false),
                    CashModuleEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AiModuleEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErpSyncEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SignedToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ean13 = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UsdPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    TransactionGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TotalUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    TenderedUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    ChangeUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    ChangeKhr = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExchangeRateKhrPerUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncStatus = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.TransactionGuid);
                });

            migrationBuilder.CreateTable(
                name: "TransactionLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransactionGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<long>(type: "INTEGER", nullable: false),
                    DescriptionSnapshot = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UnitPriceUsdSnapshot = table.Column<decimal>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    LineTotalUsd = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionLines_Transactions_TransactionGuid",
                        column: x => x.TransactionGuid,
                        principalTable: "Transactions",
                        principalColumn: "TransactionGuid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseConfigurations_HardwareId",
                table: "LicenseConfigurations",
                column: "HardwareId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Ean13",
                table: "Products",
                column: "Ean13",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_ProductId",
                table: "TransactionLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_TransactionGuid",
                table: "TransactionLines",
                column: "TransactionGuid");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CreatedAtUtc",
                table: "Transactions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SyncStatus",
                table: "Transactions",
                column: "SyncStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseConfigurations");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "TransactionLines");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
