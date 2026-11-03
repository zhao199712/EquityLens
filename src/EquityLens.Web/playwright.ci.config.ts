import { defineConfig, devices } from '@playwright/test'

// Separate from the legacy, live-backend E2E config. Never reuse a local app/API.
export default defineConfig({
  testDir: './browser-smoke',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:5186',
    ...devices['Desktop Chrome'],
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: 'npm run build && npm run preview -- --host 127.0.0.1 --port 5186 --strictPort',
    env: { VITE_API_BASE_URL: '/api' },
    url: 'http://127.0.0.1:5186',
    reuseExistingServer: false,
    timeout: 120_000,
  },
})
