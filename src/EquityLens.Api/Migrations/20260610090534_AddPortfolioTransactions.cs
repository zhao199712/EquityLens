using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_portfolio_holding_portfolio_id",
                table: "portfolio_holding");

            migrationBuilder.CreateTable(
                name: "portfolio_transaction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,6)", nullable: false, defaultValue: 0m),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_transaction", x => x.id);
                    table.ForeignKey(
                        name: "FK_portfolio_transaction_portfolio_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_portfolio_transaction_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_holding_portfolio_id_security_id",
                table: "portfolio_holding",
                columns: new[] { "portfolio_id", "security_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_transaction_portfolio_id_security_id_transaction_~",
                table: "portfolio_transaction",
                columns: new[] { "portfolio_id", "security_id", "transaction_date", "transaction_type" });

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_transaction_security_id",
                table: "portfolio_transaction",
                column: "security_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portfolio_transaction");

            migrationBuilder.DropIndex(
                name: "IX_portfolio_holding_portfolio_id_security_id",
                table: "portfolio_holding");

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_holding_portfolio_id",
                table: "portfolio_holding",
                column: "portfolio_id");
        }
    }
}
