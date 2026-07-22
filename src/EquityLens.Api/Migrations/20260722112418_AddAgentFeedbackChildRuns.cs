using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentFeedbackChildRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_research_run_id",
                table: "research_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "revision_feedback_id",
                table: "research_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_agent_run_id",
                table: "agent_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                table: "agent_feedback",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("UPDATE agent_feedback SET client_request_id = gen_random_uuid() WHERE client_request_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "client_request_id",
                table: "agent_feedback",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "follow_up_agent_run_id",
                table: "agent_feedback",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_run_parent_research_run_id",
                table: "research_run",
                column: "parent_research_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_run_revision_feedback_id",
                table: "research_run",
                column: "revision_feedback_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_parent_agent_run_id",
                table: "agent_run",
                column: "parent_agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_feedback_agent_run_id_client_request_id",
                table: "agent_feedback",
                columns: new[] { "agent_run_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_feedback_follow_up_agent_run_id",
                table: "agent_feedback",
                column: "follow_up_agent_run_id");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_feedback_agent_run_follow_up_agent_run_id",
                table: "agent_feedback",
                column: "follow_up_agent_run_id",
                principalTable: "agent_run",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_agent_run_agent_run_parent_agent_run_id",
                table: "agent_run",
                column: "parent_agent_run_id",
                principalTable: "agent_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_research_run_research_run_parent_research_run_id",
                table: "research_run",
                column: "parent_research_run_id",
                principalTable: "research_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_feedback_agent_run_follow_up_agent_run_id",
                table: "agent_feedback");

            migrationBuilder.DropForeignKey(
                name: "FK_agent_run_agent_run_parent_agent_run_id",
                table: "agent_run");

            migrationBuilder.DropForeignKey(
                name: "FK_research_run_research_run_parent_research_run_id",
                table: "research_run");

            migrationBuilder.DropIndex(
                name: "IX_research_run_parent_research_run_id",
                table: "research_run");

            migrationBuilder.DropIndex(
                name: "IX_research_run_revision_feedback_id",
                table: "research_run");

            migrationBuilder.DropIndex(
                name: "IX_agent_run_parent_agent_run_id",
                table: "agent_run");

            migrationBuilder.DropIndex(
                name: "IX_agent_feedback_agent_run_id_client_request_id",
                table: "agent_feedback");

            migrationBuilder.DropIndex(
                name: "IX_agent_feedback_follow_up_agent_run_id",
                table: "agent_feedback");

            migrationBuilder.DropColumn(
                name: "parent_research_run_id",
                table: "research_run");

            migrationBuilder.DropColumn(
                name: "revision_feedback_id",
                table: "research_run");

            migrationBuilder.DropColumn(
                name: "parent_agent_run_id",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                table: "agent_feedback");

            migrationBuilder.DropColumn(
                name: "follow_up_agent_run_id",
                table: "agent_feedback");
        }
    }
}
