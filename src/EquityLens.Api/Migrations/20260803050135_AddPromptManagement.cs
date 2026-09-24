using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "prompt_snapshot_id",
                table: "agent_tool_call",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rendered_prompt_hash",
                table: "agent_tool_call",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_run_prompt_snapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_template_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_version_number = table.Column<int>(type: "integer", nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    user_prompt = table.Column<string>(type: "text", nullable: true),
                    required_variables_json = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_run_prompt_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_run_prompt_snapshot_agent_run_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prompt_audit_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromptVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromptBindingId = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_template",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_version",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    user_prompt = table.Column<string>(type: "text", nullable: true),
                    required_variables_json = table.Column<string>(type: "jsonb", nullable: false),
                    response_format = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    change_summary = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_prompt_version_prompt_template_prompt_template_id",
                        column: x => x.prompt_template_id,
                        principalTable: "prompt_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prompt_binding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_binding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_prompt_binding_prompt_template_prompt_template_id",
                        column: x => x.prompt_template_id,
                        principalTable: "prompt_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prompt_binding_prompt_version_prompt_version_id",
                        column: x => x.prompt_version_id,
                        principalTable: "prompt_version",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_tool_call_prompt_snapshot_id",
                table: "agent_tool_call",
                column: "prompt_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_prompt_snapshot_agent_run_id_usage_key",
                table: "agent_run_prompt_snapshot",
                columns: new[] { "agent_run_id", "usage_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_audit_log_PromptTemplateId_created_at_utc",
                table: "prompt_audit_log",
                columns: new[] { "PromptTemplateId", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_prompt_audit_log_request_id",
                table: "prompt_audit_log",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "IX_prompt_binding_prompt_template_id",
                table: "prompt_binding",
                column: "prompt_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_prompt_binding_prompt_version_id",
                table: "prompt_binding",
                column: "prompt_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_prompt_binding_usage_key",
                table: "prompt_binding",
                column: "usage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_template_key",
                table: "prompt_template",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_version_prompt_template_id_version_number",
                table: "prompt_version",
                columns: new[] { "prompt_template_id", "version_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_agent_tool_call_agent_run_prompt_snapshot_prompt_snapshot_id",
                table: "agent_tool_call",
                column: "prompt_snapshot_id",
                principalTable: "agent_run_prompt_snapshot",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_tool_call_agent_run_prompt_snapshot_prompt_snapshot_id",
                table: "agent_tool_call");

            migrationBuilder.DropTable(
                name: "agent_run_prompt_snapshot");

            migrationBuilder.DropTable(
                name: "prompt_audit_log");

            migrationBuilder.DropTable(
                name: "prompt_binding");

            migrationBuilder.DropTable(
                name: "prompt_version");

            migrationBuilder.DropTable(
                name: "prompt_template");

            migrationBuilder.DropIndex(
                name: "IX_agent_tool_call_prompt_snapshot_id",
                table: "agent_tool_call");

            migrationBuilder.DropColumn(
                name: "prompt_snapshot_id",
                table: "agent_tool_call");

            migrationBuilder.DropColumn(
                name: "rendered_prompt_hash",
                table: "agent_tool_call");
        }
    }
}
