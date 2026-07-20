using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLevel2AgentOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "iteration",
                table: "agent_run_node",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "template_node_key",
                table: "agent_run_node",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "lease_expires_at_utc",
                table: "agent_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "agent_run",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "orchestration_version",
                table: "agent_run",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "agent_run_wake_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agent_run_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    orchestration_version = table.Column<long>(type: "bigint", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_run_wake_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_wake_outbox_published_at_utc_created_at_utc",
                table: "agent_run_wake_outbox",
                columns: new[] { "published_at_utc", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_run_wake_outbox");

            migrationBuilder.DropColumn(
                name: "iteration",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "template_node_key",
                table: "agent_run_node");

            migrationBuilder.DropColumn(
                name: "lease_expires_at_utc",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "agent_run");

            migrationBuilder.DropColumn(
                name: "orchestration_version",
                table: "agent_run");
        }
    }
}
