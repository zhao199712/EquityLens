using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedPriceAdjustmentBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "price_adjustment_batch_id",
                table: "security",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "prices_verified_through",
                table: "security",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "price_adjustment_batch_id",
                table: "market_price",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "price_adjustment_batch",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fetched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    coverage_from = table.Column<DateOnly>(type: "date", nullable: false),
                    coverage_to = table.Column<DateOnly>(type: "date", nullable: false),
                    verified_through = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    algorithm_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    series_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_adjustment_batch", x => x.id);
                    table.ForeignKey(
                        name: "FK_price_adjustment_batch_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_security_price_adjustment_batch_id",
                table: "security",
                column: "price_adjustment_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_market_price_price_adjustment_batch_id",
                table: "market_price",
                column: "price_adjustment_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_adjustment_batch_security_id_fetched_at_utc",
                table: "price_adjustment_batch",
                columns: new[] { "security_id", "fetched_at_utc" });

            migrationBuilder.AddForeignKey(
                name: "FK_market_price_price_adjustment_batch_price_adjustment_batch_~",
                table: "market_price",
                column: "price_adjustment_batch_id",
                principalTable: "price_adjustment_batch",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_security_price_adjustment_batch_price_adjustment_batch_id",
                table: "security",
                column: "price_adjustment_batch_id",
                principalTable: "price_adjustment_batch",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_market_price_price_adjustment_batch_price_adjustment_batch_~",
                table: "market_price");

            migrationBuilder.DropForeignKey(
                name: "FK_security_price_adjustment_batch_price_adjustment_batch_id",
                table: "security");

            migrationBuilder.DropTable(
                name: "price_adjustment_batch");

            migrationBuilder.DropIndex(
                name: "IX_security_price_adjustment_batch_id",
                table: "security");

            migrationBuilder.DropIndex(
                name: "IX_market_price_price_adjustment_batch_id",
                table: "market_price");

            migrationBuilder.DropColumn(
                name: "price_adjustment_batch_id",
                table: "security");

            migrationBuilder.DropColumn(
                name: "prices_verified_through",
                table: "security");

            migrationBuilder.DropColumn(
                name: "price_adjustment_batch_id",
                table: "market_price");
        }
    }
}
