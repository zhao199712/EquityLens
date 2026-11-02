namespace EquityLens.Api.Tests.Integration;

/// <summary>
/// 需要真實 PostgreSQL 的測試。未設定 EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING 時在收集階段就標記為略過，
/// 避免 xunit v2 把執行期的 SkipException 回報成失敗，讓沒有資料庫的 CI 誤判為紅燈。
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            Skip = $"Set {ConnectionStringVariable} to run PostgreSQL integration tests.";
    }
}
