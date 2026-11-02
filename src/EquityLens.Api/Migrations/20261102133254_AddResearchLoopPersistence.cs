using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchLoopPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "agent_llm_turn_id",
                table: "agent_tool_call",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guard_decision",
                table: "agent_tool_call",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guard_reason",
                table: "agent_tool_call",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_call_key",
                table: "agent_tool_call",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_llm_turn",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    prompt_template_id = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<int>(type: "integer", nullable: false),
                    rendered_prompt_hash = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    thought_summary = table.Column<string>(type: "text", nullable: false),
                    action_json = table.Column<string>(type: "jsonb", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_llm_turn", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_llm_turn_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_llm_turn_agent_run_node_agent_run_node_id",
                        column: x => x.agent_run_node_id,
                        principalTable: "agent_run_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_loop_checkpoint",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn = table.Column<int>(type: "integer", nullable: false),
                    lease_version = table.Column<long>(type: "bigint", nullable: false),
                    state_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_loop_checkpoint", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_loop_checkpoint_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_loop_checkpoint_agent_run_node_agent_run_node_id",
                        column: x => x.agent_run_node_id,
                        principalTable: "agent_run_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_tool_call_agent_llm_turn_id_tool_call_key",
                table: "agent_tool_call",
                columns: new[] { "agent_llm_turn_id", "tool_call_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_llm_turn_agent_run_id",
                table: "agent_llm_turn",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_llm_turn_agent_run_node_id_turn",
                table: "agent_llm_turn",
                columns: new[] { "agent_run_node_id", "turn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_loop_checkpoint_agent_run_id",
                table: "agent_loop_checkpoint",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_loop_checkpoint_agent_run_node_id",
                table: "agent_loop_checkpoint",
                column: "agent_run_node_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_agent_tool_call_agent_llm_turn_agent_llm_turn_id",
                table: "agent_tool_call",
                column: "agent_llm_turn_id",
                principalTable: "agent_llm_turn",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_tool_call_agent_llm_turn_agent_llm_turn_id",
                table: "agent_tool_call");

            migrationBuilder.DropTable(
                name: "agent_llm_turn");

            migrationBuilder.DropTable(
                name: "agent_loop_checkpoint");

            migrationBuilder.DropIndex(
                name: "IX_agent_tool_call_agent_llm_turn_id_tool_call_key",
                table: "agent_tool_call");

            migrationBuilder.DropColumn(
                name: "agent_llm_turn_id",
                table: "agent_tool_call");

            migrationBuilder.DropColumn(
                name: "guard_decision",
                table: "agent_tool_call");

            migrationBuilder.DropColumn(
                name: "guard_reason",
                table: "agent_tool_call");

            migrationBuilder.DropColumn(
                name: "tool_call_key",
                table: "agent_tool_call");
        }
    }
}
