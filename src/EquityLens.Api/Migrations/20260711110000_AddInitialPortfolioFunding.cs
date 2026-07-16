using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations;

public partial class AddInitialPortfolioFunding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO portfolio_cash_flow (id, portfolio_id, flow_type, amount, currency, effective_date, status, is_user_adjusted, note, created_at_utc)
            SELECT gen_random_uuid(), p.id, 'Deposit', SUM(t.quantity * t.price + t.fee), 'TWD', first_tx.first_date, 'Posted', false, '系統建立：既有交易初始入金', now()
            FROM portfolio p
            JOIN (SELECT portfolio_id, MIN(transaction_date) AS first_date FROM portfolio_transaction GROUP BY portfolio_id) first_tx ON first_tx.portfolio_id = p.id
            JOIN portfolio_transaction t ON t.portfolio_id = p.id AND t.transaction_type = 'BUY'
            WHERE p.base_currency = 'TWD'
              AND NOT EXISTS (SELECT 1 FROM portfolio_cash_flow f WHERE f.portfolio_id = p.id AND f.note = '系統建立：既有交易初始入金')
            GROUP BY p.id, first_tx.first_date;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("DELETE FROM portfolio_cash_flow WHERE note = '系統建立：既有交易初始入金';");
}
