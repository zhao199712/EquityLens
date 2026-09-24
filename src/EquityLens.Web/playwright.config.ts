import { defineConfig, devices } from '@playwright/test'

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
      command: 'E2E_TEST_MODE=1 AgentRunQueue__StreamKey=equitylens:e2e:approval-gate:v1 AgentRunQueue__ConsumerGroupName=e2e-approval-gate-workers dotnet run --no-launch-profile --project EquityLens.Api/EquityLens.Api.csproj --urls http://127.0.0.1:5036',
      cwd: '..', url: `${apiUrl}/healthz`, reuseExistingServer: false, timeout: 120_000,
    },
    { command: 'VITE_API_BASE_URL=http://127.0.0.1:5036/api npm run dev -- --host 127.0.0.1 --port 5174', url: webUrl, reuseExistingServer: false, timeout: 60_000 },
  ],
})
