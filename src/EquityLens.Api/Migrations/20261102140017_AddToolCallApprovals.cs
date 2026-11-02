using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddToolCallApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "agent_tool_call_id",
                table: "agent_approval_request",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "arguments_hash",
                table: "agent_approval_request",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "arguments_json",
                table: "agent_approval_request",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "agent_approval_request",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "lease_version_at_request",
                table: "agent_approval_request",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "agent_approval_request",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Node");

            migrationBuilder.AddColumn<string>(
                name: "tool_name",
                table: "agent_approval_request",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_approval_request_agent_tool_call_id",
                table: "agent_approval_request",
                column: "agent_tool_call_id");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_approval_request_agent_tool_call_agent_tool_call_id",
                table: "agent_approval_request",
                column: "agent_tool_call_id",
                principalTable: "agent_tool_call",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_approval_request_agent_tool_call_agent_tool_call_id",
                table: "agent_approval_request");

            migrationBuilder.DropIndex(
                name: "IX_agent_approval_request_agent_tool_call_id",
                table: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "agent_tool_call_id",
                table: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "arguments_hash",
                table: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "arguments_json",
                table: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "lease_version_at_request",
                table: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "agent_approval_request");

            migrationBuilder.DropColumn(
                name: "tool_name",
                table: "agent_approval_request");
        }
    }
}
