using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoCoReTrade.Infrastructure.Migrations;

public partial class MergeBagCategoryIntoAccessories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE [dbo].[Products]
            SET [Category] = N'Phụ kiện'
            WHERE LTRIM(RTRIM([Category])) IN (N'Túi', N'Túi xách');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This data-only merge preserves products. A merged category cannot safely
        // be split again without an explicit list of the original product IDs.
    }
}
