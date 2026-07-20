using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticPlanFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlackboardSnapshotJson",
                table: "agent_run_node",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "agent_run_node",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "agent_run_node",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "agent_run_node",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableBlackboardSnapshots",
                table: "agent_run",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalEstimatedCostUsd",
                table: "agent_run",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TotalInputTokens",
                table: "agent_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalOutputTokens",
                table: "agent_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlackboardSnapshotJson",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "EnableBlackboardSnapshots",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "TotalEstimatedCostUsd",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "TotalInputTokens",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "TotalOutputTokens",
                table: "agent_run");
        }
    }
}
