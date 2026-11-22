using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoCoReTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DailyOrderCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderCode",
                table: "Orders",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OrderCodeCounters",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCodeCounters", x => x.Day);
                });

            // Preserve all order IDs and monetary snapshots; number existing orders
            // chronologically within their Vietnam calendar day before enforcing uniqueness.
            migrationBuilder.Sql("""
                ;WITH Numbered AS (
                    SELECT Id, CONVERT(date,DATEADD(hour,7,CreatedAtUtc)) AS LocalDay,
                        ROW_NUMBER() OVER (PARTITION BY CONVERT(date,DATEADD(hour,7,CreatedAtUtc))
                            ORDER BY CreatedAtUtc,Id) AS Number
                    FROM dbo.Orders
                )
                UPDATE target SET OrderCode='ORD-'+CONVERT(char(8),source.LocalDay,112)+'-'+
                    CASE WHEN source.Number<1000 THEN RIGHT('000'+CONVERT(varchar(20),source.Number),3)
                         ELSE CONVERT(varchar(20),source.Number) END
                FROM dbo.Orders target JOIN Numbered source ON source.Id=target.Id;

                INSERT dbo.OrderCodeCounters ([Day],LastNumber)
                SELECT CONVERT(date,DATEADD(hour,7,CreatedAtUtc)),COUNT_BIG(*)
                FROM dbo.Orders GROUP BY CONVERT(date,DATEADD(hour,7,CreatedAtUtc));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderCode",
                table: "Orders",
                column: "OrderCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderCodeCounters");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderCode",
                table: "Orders");
        }
    }
}
