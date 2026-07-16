using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioCashFlowIsSystemDerived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_system_derived",
                table: "portfolio_cash_flow",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE portfolio_cash_flow
                SET is_system_derived = true
                WHERE note IN ('系統推導：買入資金', '系統推導入金', '系統建立：既有交易初始入金');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_system_derived",
                table: "portfolio_cash_flow");
        }
    }
}
