using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioCashFlowsAndDividends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_dividend_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ex_dividend_date = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    cash_amount_per_share = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_dividend_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_cash_dividend_event_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_cash_flow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_dividend_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flow_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_user_adjusted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_cash_flow", x => x.id);
                    table.ForeignKey(
                        name: "FK_portfolio_cash_flow_cash_dividend_event_cash_dividend_event~",
                        column: x => x.cash_dividend_event_id,
                        principalTable: "cash_dividend_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_portfolio_cash_flow_portfolio_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_portfolio_cash_flow_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_dividend_event_security_id",
                table: "cash_dividend_event",
                column: "security_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_dividend_event_source_source_key",
                table: "cash_dividend_event",
                columns: new[] { "source", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_cash_flow_cash_dividend_event_id",
                table: "portfolio_cash_flow",
                column: "cash_dividend_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_cash_flow_portfolio_id_cash_dividend_event_id",
                table: "portfolio_cash_flow",
                columns: new[] { "portfolio_id", "cash_dividend_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_cash_flow_portfolio_id_effective_date",
                table: "portfolio_cash_flow",
                columns: new[] { "portfolio_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_cash_flow_security_id",
                table: "portfolio_cash_flow",
                column: "security_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portfolio_cash_flow");

            migrationBuilder.DropTable(
                name: "cash_dividend_event");
        }
    }
}
