using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataAndPriceUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_market_price_security_id_interval_price_time",
                table: "market_price");

            migrationBuilder.AddColumn<string>(
                name: "metadata_source",
                table: "security",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "metadata_updated_at_utc",
                table: "security",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prices_source",
                table: "security",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "prices_synced_at_utc",
                table: "security",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "market_price",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateIndex(
                name: "IX_market_price_security_id_interval_price_time",
                table: "market_price",
                columns: new[] { "security_id", "interval", "price_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_market_price_security_id_interval_price_time",
                table: "market_price");

            migrationBuilder.DropColumn(
                name: "metadata_source",
                table: "security");

            migrationBuilder.DropColumn(
                name: "metadata_updated_at_utc",
                table: "security");

            migrationBuilder.DropColumn(
                name: "prices_source",
                table: "security");

            migrationBuilder.DropColumn(
                name: "prices_synced_at_utc",
                table: "security");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "market_price");

            migrationBuilder.CreateIndex(
                name: "IX_market_price_security_id_interval_price_time",
                table: "market_price",
                columns: new[] { "security_id", "interval", "price_time" });
        }
    }
}
