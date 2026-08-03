using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentHumanApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "execution_attempt",
                table: "agent_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "requires_human_approval_override",
                table: "agent_node_setting",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_approval_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    node_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    side_effect_level = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    policy_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    execution_attempt = table.Column<int>(type: "integer", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consumed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_approval_request", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_approval_request_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_approval_request_agent_run_node_agent_run_node_id",
                        column: x => x.agent_run_node_id,
                        principalTable: "agent_run_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_approval_request_app_user_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_approval_request_agent_run_id_requested_at_utc",
                table: "agent_approval_request",
                columns: new[] { "agent_run_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_approval_request_agent_run_node_id_execution_attempt_~",
                table: "agent_approval_request",
                columns: new[] { "agent_run_node_id", "execution_attempt", "status" },
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_agent_approval_request_client_request_id",
                table: "agent_approval_request",
                column: "client_request_id",
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_approval_request_decided_by_user_id",
                table: "agent_approval_request",
                column: "decided_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "execution_attempt",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "requires_human_approval_override",
                table: "agent_node_setting");
        }
    }
}
