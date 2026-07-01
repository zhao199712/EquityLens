using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchRunTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "research_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ticker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    retrieval_mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_policy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    top_k = table.Column<int>(type: "integer", nullable: false),
                    temperature = table.Column<double>(type: "double precision", nullable: false),
                    citation_count = table.Column<int>(type: "integer", nullable: false),
                    latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "research_run_candidate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    search_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    query = table.Column<string>(type: "text", nullable: true),
                    document_chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    relevance_score = table.Column<double>(type: "double precision", nullable: false),
                    adjusted_score = table.Column<double>(type: "double precision", nullable: true),
                    rank_before_rerank = table.Column<int>(type: "integer", nullable: true),
                    rank_after_rerank = table.Column<int>(type: "integer", nullable: true),
                    decision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    discard_reason = table.Column<string>(type: "text", nullable: true),
                    content_preview = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_run_candidate", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_run_candidate_research_run_run_id",
                        column: x => x.run_id,
                        principalTable: "research_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_run_citation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    citation_index = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_chunk_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    quote_text = table.Column<string>(type: "text", nullable: true),
                    relevance_score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_run_citation", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_run_citation_research_run_run_id",
                        column: x => x.run_id,
                        principalTable: "research_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_run_step",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: true),
                    output_json = table.Column<string>(type: "text", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_run_step", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_run_step_research_run_run_id",
                        column: x => x.run_id,
                        principalTable: "research_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_research_run_status_created_at_utc",
                table: "research_run",
                columns: new[] { "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_research_run_ticker_created_at_utc",
                table: "research_run",
                columns: new[] { "ticker", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_research_run_trace_id",
                table: "research_run",
                column: "trace_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_run_user_id_created_at_utc",
                table: "research_run",
                columns: new[] { "user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_research_run_candidate_document_chunk_id",
                table: "research_run_candidate",
                column: "document_chunk_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_run_candidate_run_id",
                table: "research_run_candidate",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_run_citation_document_chunk_id",
                table: "research_run_citation",
                column: "document_chunk_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_run_citation_run_id",
                table: "research_run_citation",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_run_step_run_id",
                table: "research_run_step",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_run_candidate");

            migrationBuilder.DropTable(
                name: "research_run_citation");

            migrationBuilder.DropTable(
                name: "research_run_step");

            migrationBuilder.DropTable(
                name: "research_run");
        }
    }
}
