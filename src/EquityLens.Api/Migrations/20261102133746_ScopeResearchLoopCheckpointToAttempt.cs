using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScopeResearchLoopCheckpointToAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_loop_checkpoint_agent_run_node_id",
                table: "agent_loop_checkpoint");

            migrationBuilder.DropIndex(
                name: "IX_agent_llm_turn_agent_run_node_id_turn",
                table: "agent_llm_turn");

            migrationBuilder.AddColumn<int>(
                name: "execution_attempt",
                table: "agent_loop_checkpoint",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "execution_attempt",
                table: "agent_llm_turn",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_agent_loop_checkpoint_agent_run_node_id_execution_attempt",
                table: "agent_loop_checkpoint",
                columns: new[] { "agent_run_node_id", "execution_attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_llm_turn_agent_run_node_id_execution_attempt_turn",
                table: "agent_llm_turn",
                columns: new[] { "agent_run_node_id", "execution_attempt", "turn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_loop_checkpoint_agent_run_node_id_execution_attempt",
                table: "agent_loop_checkpoint");

            migrationBuilder.DropIndex(
                name: "IX_agent_llm_turn_agent_run_node_id_execution_attempt_turn",
                table: "agent_llm_turn");

            migrationBuilder.DropColumn(
                name: "execution_attempt",
                table: "agent_loop_checkpoint");

            migrationBuilder.DropColumn(
                name: "execution_attempt",
                table: "agent_llm_turn");

            migrationBuilder.CreateIndex(
                name: "IX_agent_loop_checkpoint_agent_run_node_id",
                table: "agent_loop_checkpoint",
                column: "agent_run_node_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_llm_turn_agent_run_node_id_turn",
                table: "agent_llm_turn",
                columns: new[] { "agent_run_node_id", "turn" },
                unique: true);
        }
    }
}
