using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRunTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agent_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "Pending"),
                    input_json = table.Column<string>(type: "jsonb", nullable: false),
                    output_json = table.Column<string>(type: "jsonb", nullable: true),
                    blackboard_json = table.Column<string>(type: "jsonb", nullable: false),
                    workflow_definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_run", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_run_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_run_node",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    node_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "Pending"),
                    input_json = table.Column<string>(type: "jsonb", nullable: true),
                    output_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_run_node", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_run_node_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feedback_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Requested"),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    responded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_feedback_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_feedback_agent_run_node_agent_run_node_id",
                        column: x => x.agent_run_node_id,
                        principalTable: "agent_run_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "agent_run_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_run_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_run_event_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_run_event_agent_run_node_agent_run_node_id",
                        column: x => x.agent_run_node_id,
                        principalTable: "agent_run_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "agent_tool_call",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Running"),
                    arguments_json = table.Column<string>(type: "jsonb", nullable: false),
                    result_preview = table.Column<string>(type: "text", nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tool_call", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_tool_call_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_tool_call_agent_run_node_agent_run_node_id",
                        column: x => x.agent_run_node_id,
                        principalTable: "agent_run_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_feedback_agent_run_id",
                table: "agent_feedback",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_feedback_agent_run_node_id",
                table: "agent_feedback",
                column: "agent_run_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_user_id",
                table: "agent_run",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_workflow_type_status",
                table: "agent_run",
                columns: new[] { "workflow_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_event_agent_run_id",
                table: "agent_run_event",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_event_agent_run_id_agent_run_node_id",
                table: "agent_run_event",
                columns: new[] { "agent_run_id", "agent_run_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_event_agent_run_node_id",
                table: "agent_run_event",
                column: "agent_run_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_node_agent_run_id",
                table: "agent_run_node",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_tool_call_agent_run_id",
                table: "agent_tool_call",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_tool_call_agent_run_node_id",
                table: "agent_tool_call",
                column: "agent_run_node_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_feedback");

            migrationBuilder.DropTable(
                name: "agent_run_event");

            migrationBuilder.DropTable(
                name: "agent_tool_call");

            migrationBuilder.DropTable(
                name: "agent_run_node");

            migrationBuilder.DropTable(
                name: "agent_run");
        }
    }
}
