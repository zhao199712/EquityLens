using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_filing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    filing_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    fiscal_quarter = table.Column<int>(type: "integer", nullable: true),
                    period_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "en"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    parse_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Pending"),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_filing", x => x.id);
                    table.ForeignKey(
                        name: "FK_financial_filing_app_user_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_financial_filing_document_document_id",
                        column: x => x.document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_financial_filing_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_financial_filing_uploaded_file_uploaded_file_id",
                        column: x => x.uploaded_file_id,
                        principalTable: "uploaded_file",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_filing_document_id",
                table: "financial_filing",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_filing_security_id_fiscal_year_fiscal_quarter_fil~",
                table: "financial_filing",
                columns: new[] { "security_id", "fiscal_year", "fiscal_quarter", "filing_type" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_filing_uploaded_by_user_id",
                table: "financial_filing",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_filing_uploaded_file_id",
                table: "financial_filing",
                column: "uploaded_file_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_filing");
        }
    }
}
