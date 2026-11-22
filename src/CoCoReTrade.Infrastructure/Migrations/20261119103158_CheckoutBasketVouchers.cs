using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoCoReTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutBasketVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutBatchId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Orders",
                type: "decimal(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountCode",
                table: "Orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckoutBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutBatches_AspNetUsers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutVouchers",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Percent = table.Column<int>(type: "int", nullable: false),
                    MinimumAmount = table.Column<decimal>(type: "decimal(18,0)", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutVouchers", x => x.Code);
                });

            migrationBuilder.InsertData(
                table: "CheckoutVouchers",
                columns: new[] { "Code", "Enabled", "EndsAtUtc", "MinimumAmount", "Percent" },
                values: new object[,]
                {
                    { "GIAM10", true, new DateTime(2026, 12, 30, 17, 0, 0, 0, DateTimeKind.Utc), 500000m, 10 },
                    { "GIAM5", true, new DateTime(2026, 12, 30, 17, 0, 0, 0, DateTimeKind.Utc), 300000m, 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CheckoutBatchId",
                table: "Orders",
                column: "CheckoutBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutBatches_BuyerId_IdempotencyKey",
                table: "CheckoutBatches",
                columns: new[] { "BuyerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_CheckoutBatches_CheckoutBatchId",
                table: "Orders",
                column: "CheckoutBatchId",
                principalTable: "CheckoutBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_CheckoutBatches_CheckoutBatchId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "CheckoutBatches");

            migrationBuilder.DropTable(
                name: "CheckoutVouchers");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CheckoutBatchId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutBatchId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DiscountCode",
                table: "Orders");
        }
    }
}
