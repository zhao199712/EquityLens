import { defineConfig, devices } from '@playwright/test'
import { randomBytes } from 'node:crypto'

// Config is also loaded in workers; inherit the same per-run password.
process.env.E2E_ADMIN_PASSWORD ??= randomBytes(32).toString('hex')
function requiredEnvironment(name: string): string {
  const value = process.env[name]
  if (!value) throw new Error(`${name} must point to an isolated E2E service.`)
  return value
}

const apiUrl = 'http://127.0.0.1:5036'
const webUrl = 'http://127.0.0.1:5174'

export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  reporter: 'line',
  use: { baseURL: webUrl, trace: 'retain-on-failure', ...devices['Desktop Chrome'] },
  webServer: [
    {
      command: 'dotnet run --no-launch-profile --project EquityLens.Api/EquityLens.Api.csproj --urls http://127.0.0.1:5036',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Testing',
        DOTNET_ENVIRONMENT: 'Testing',
        E2E_TEST_MODE: '1',
        E2E_ADMIN_PASSWORD: process.env.E2E_ADMIN_PASSWORD,
        ConnectionStrings__PostgreSQL: requiredEnvironment('E2E_POSTGRES_CONNECTION_STRING'),
        Redis__ConnectionString: requiredEnvironment('E2E_REDIS_CONNECTION_STRING'),
        AgentRunQueue__StreamKey: 'equitylens:e2e:approval-gate:v1',
        AgentRunQueue__ConsumerGroupName: 'e2e-approval-gate-workers',
      },
      cwd: '..', url: `${apiUrl}/healthz`, reuseExistingServer: false, timeout: 120_000,
    },
    { command: 'VITE_API_BASE_URL=http://127.0.0.1:5036/api npm run dev -- --host 127.0.0.1 --port 5174', url: webUrl, reuseExistingServer: false, timeout: 60_000 },
  ],
})
