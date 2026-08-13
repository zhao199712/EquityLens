using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioTransactionConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "portfolio_transaction",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "portfolio_transaction",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_transaction_portfolio_id_idempotency_key",
                table: "portfolio_transaction",
                columns: new[] { "portfolio_id", "idempotency_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_transaction_fee_non_negative",
                table: "portfolio_transaction",
                sql: "fee >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_transaction_price_non_negative",
                table: "portfolio_transaction",
                sql: "price >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_transaction_quantity_positive",
                table: "portfolio_transaction",
                sql: "quantity > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_transaction_transaction_type",
                table: "portfolio_transaction",
                sql: "transaction_type IN ('BUY', 'SELL')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_portfolio_transaction_portfolio_id_idempotency_key",
                table: "portfolio_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_transaction_fee_non_negative",
                table: "portfolio_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_transaction_price_non_negative",
                table: "portfolio_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_transaction_quantity_positive",
                table: "portfolio_transaction");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_transaction_transaction_type",
                table: "portfolio_transaction");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "portfolio_transaction");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "portfolio_transaction");
        }
    }
}
