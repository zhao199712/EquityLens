using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestorConference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "investor_conference",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    event_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    summary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "zh-TW"),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    original_file_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    parse_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Pending"),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investor_conference", x => x.id);
                    table.ForeignKey(
                        name: "FK_investor_conference_document_document_id",
                        column: x => x.document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_investor_conference_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_investor_conference_uploaded_file_uploaded_file_id",
                        column: x => x.uploaded_file_id,
                        principalTable: "uploaded_file",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_investor_conference_document_id",
                table: "investor_conference",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_investor_conference_security_id_event_date",
                table: "investor_conference",
                columns: new[] { "security_id", "event_date" });

            migrationBuilder.CreateIndex(
                name: "IX_investor_conference_uploaded_file_id",
                table: "investor_conference",
                column: "uploaded_file_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investor_conference");
        }
    }
}
