namespace EquityLens.Api.Services.Auth;

/// <summary>測試帳號只能在明確啟用的 Testing 環境使用，不提供預設密碼。</summary>
public static class E2ETestAccess
{
    public const string AdminEmail = "playwright-admin-test@example.com";

    public static bool IsReservedEmail(string email) =>
        string.Equals(email.Trim(), AdminEmail, StringComparison.OrdinalIgnoreCase);

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!string.Equals(configuration["E2E_TEST_MODE"], "1", StringComparison.Ordinal)) return false;
        if (!environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("E2E_TEST_MODE is only allowed in the Testing environment.");
        if (string.IsNullOrWhiteSpace(configuration["E2E_ADMIN_PASSWORD"]) || configuration["E2E_ADMIN_PASSWORD"]!.Length < 32)
            throw new InvalidOperationException("E2E_ADMIN_PASSWORD must be explicitly supplied with at least 32 characters.");
        return true;
    }

    public static void RequireEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsEnabled(configuration, environment))
            throw new InvalidOperationException("E2E_TEST_MODE is not enabled.");
    }
}
