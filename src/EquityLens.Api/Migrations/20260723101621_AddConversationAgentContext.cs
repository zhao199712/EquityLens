using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAgentContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "context_json",
                table: "chat_session",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<long>(
                name: "context_version",
                table: "chat_session",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "agent_run_id",
                table: "chat_message",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "conversation_turn_id",
                table: "chat_message",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "message_type",
                table: "chat_message",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Text");

            migrationBuilder.CreateTable(
                name: "conversation_turn",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    chat_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    standalone_query = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    input_context_version = table.Column<long>(type: "bigint", nullable: false),
                    output_context_version = table.Column<long>(type: "bigint", nullable: true),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prompt_template_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_version = table.Column<int>(type: "integer", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_turn", x => x.id);
                    table.ForeignKey(
                        name: "FK_conversation_turn_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_conversation_turn_chat_session_chat_session_id",
                        column: x => x.chat_session_id,
                        principalTable: "chat_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_agent_run_id",
                table: "chat_message",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_conversation_turn_id",
                table: "chat_message",
                column: "conversation_turn_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_turn_agent_run_id",
                table: "conversation_turn",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_turn_chat_session_id_request_id",
                table: "conversation_turn",
                columns: new[] { "chat_session_id", "request_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_chat_message_agent_run_agent_run_id",
                table: "chat_message",
                column: "agent_run_id",
                principalTable: "agent_run",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_chat_message_conversation_turn_conversation_turn_id",
                table: "chat_message",
                column: "conversation_turn_id",
                principalTable: "conversation_turn",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chat_message_agent_run_agent_run_id",
                table: "chat_message");

            migrationBuilder.DropForeignKey(
                name: "FK_chat_message_conversation_turn_conversation_turn_id",
                table: "chat_message");

            migrationBuilder.DropTable(
                name: "conversation_turn");

            migrationBuilder.DropIndex(
                name: "IX_chat_message_agent_run_id",
                table: "chat_message");

            migrationBuilder.DropIndex(
                name: "IX_chat_message_conversation_turn_id",
                table: "chat_message");

            migrationBuilder.DropColumn(
                name: "context_json",
                table: "chat_session");

            migrationBuilder.DropColumn(
                name: "context_version",
                table: "chat_session");

            migrationBuilder.DropColumn(
                name: "agent_run_id",
                table: "chat_message");

            migrationBuilder.DropColumn(
                name: "conversation_turn_id",
                table: "chat_message");

            migrationBuilder.DropColumn(
                name: "message_type",
                table: "chat_message");
        }
    }
}
