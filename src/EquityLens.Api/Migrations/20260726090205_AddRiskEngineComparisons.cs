using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskEngineComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_engine_comparison",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    risk_backtest_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    primary_algorithm_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    candidate_algorithm_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    primary_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    candidate_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    passed_tolerance = table.Column<bool>(type: "boolean", nullable: true),
                    comparison_json = table.Column<string>(type: "jsonb", nullable: true),
                    candidate_result_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_engine_comparison", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_engine_comparison_risk_backtest_run_risk_backtest_run_~",
                        column: x => x.risk_backtest_run_id,
                        principalTable: "risk_backtest_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_risk_engine_comparison_risk_backtest_run_id",
                table: "risk_engine_comparison",
                column: "risk_backtest_run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_risk_engine_comparison_status_created_at_utc",
                table: "risk_engine_comparison",
                columns: new[] { "status", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_engine_comparison");
        }
    }
}
