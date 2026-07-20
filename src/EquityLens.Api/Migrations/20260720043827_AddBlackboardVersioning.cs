using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBlackboardVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorCategory",
                table: "agent_run_node",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "agent_run_node",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ErrorRetryable",
                table: "agent_run_node",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputBlackboardVersion",
                table: "agent_run_node",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputBlackboardVersion",
                table: "agent_run_node",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ProducedBlackboardKeys",
                table: "agent_run_node",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorCategory",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "ErrorRetryable",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "InputBlackboardVersion",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "OutputBlackboardVersion",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "ProducedBlackboardKeys",
                table: "agent_run_node");
        }
    }
}
