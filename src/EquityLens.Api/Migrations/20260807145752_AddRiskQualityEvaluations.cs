using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskQualityEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_quality_evaluation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    risk_calculation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_backtest_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    confidence_level = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    observation_count = table.Column<int>(type: "integer", nullable: true),
                    kupiec_p_value = table.Column<decimal>(type: "numeric(12,10)", precision: 12, scale: 10, nullable: true),
                    christoffersen_p_value = table.Column<decimal>(type: "numeric(12,10)", precision: 12, scale: 10, nullable: true),
                    es_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    fit_healthy = table.Column<bool>(type: "boolean", nullable: true),
                    failure_codes_json = table.Column<string>(type: "jsonb", nullable: false),
                    warning_codes_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    evaluated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_quality_evaluation", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_quality_evaluation_risk_backtest_run_risk_backtest_run~",
                        column: x => x.risk_backtest_run_id,
                        principalTable: "risk_backtest_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_risk_quality_evaluation_risk_calculation_run_risk_calculati~",
                        column: x => x.risk_calculation_run_id,
                        principalTable: "risk_calculation_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_risk_quality_evaluation_risk_backtest_run_id",
                table: "risk_quality_evaluation",
                column: "risk_backtest_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_quality_evaluation_risk_calculation_run_id_policy_vers~",
                table: "risk_quality_evaluation",
                columns: new[] { "risk_calculation_run_id", "policy_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_quality_evaluation");
        }
    }
}
