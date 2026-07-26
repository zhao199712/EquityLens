using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class PromoteVtGarchRiskEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fallback_depth",
                table: "risk_backtest_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "fallback_reason",
                table: "risk_backtest_run",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "input_hash",
                table: "risk_backtest_run",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requested_model",
                table: "risk_backtest_run",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "selected_model",
                table: "risk_backtest_run",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "risk_calculation_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    progress_percent = table.Column<int>(type: "integer", nullable: false),
                    requested_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    selected_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    algorithm_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    input_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    fit_health_json = table.Column<string>(type: "jsonb", nullable: true),
                    data_factor_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    fallback_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    fallback_depth = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_calculation_run", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_calculation_run_portfolio_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_risk_calculation_run_input_hash",
                table: "risk_calculation_run",
                column: "input_hash");

            migrationBuilder.CreateIndex(
                name: "IX_risk_calculation_run_portfolio_id_created_at_utc",
                table: "risk_calculation_run",
                columns: new[] { "portfolio_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_calculation_run");

            migrationBuilder.DropColumn(
                name: "fallback_depth",
                table: "risk_backtest_run");

            migrationBuilder.DropColumn(
                name: "fallback_reason",
                table: "risk_backtest_run");

            migrationBuilder.DropColumn(
                name: "input_hash",
                table: "risk_backtest_run");

            migrationBuilder.DropColumn(
                name: "requested_model",
                table: "risk_backtest_run");

            migrationBuilder.DropColumn(
                name: "selected_model",
                table: "risk_backtest_run");
        }
    }
}
