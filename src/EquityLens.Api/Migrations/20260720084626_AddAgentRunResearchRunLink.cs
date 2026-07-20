using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRunResearchRunLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "research_run_id",
                table: "agent_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE agent_run AS agent
                SET research_run_id = research.id
                FROM research_run AS research
                WHERE agent.research_run_id IS NULL
                  AND agent.input_json ->> 'researchRunId' = research.id::text;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_research_run_id",
                table: "agent_run",
                column: "research_run_id");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_run_research_run_research_run_id",
                table: "agent_run",
                column: "research_run_id",
                principalTable: "research_run",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_run_research_run_research_run_id",
                table: "agent_run");

            migrationBuilder.DropIndex(
                name: "IX_agent_run_research_run_id",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "research_run_id",
                table: "agent_run");
        }
    }
}
