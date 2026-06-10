using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_rate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    target_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_source_currency_target_currency",
                table: "exchange_rate",
                columns: new[] { "source_currency", "target_currency" },
                unique: true);

            // Seed default exchange rates: 1 USD = 30 TWD
            migrationBuilder.InsertData(
                table: "exchange_rate",
                columns: new[] { "id", "source_currency", "target_currency", "rate", "updated_at_utc" },
                values: new object[,]
                {
                    { Guid.NewGuid(), "USD", "TWD", 30.0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { Guid.NewGuid(), "TWD", "USD", 0.033333m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_rate");
        }
    }
}
