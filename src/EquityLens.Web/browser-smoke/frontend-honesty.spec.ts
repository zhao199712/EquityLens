import { test, expect, login, researchRun, agentId } from './fixtures'

test('Settings only changes display language, immediately and after reload', async ({ page, api }, testInfo) => {
  await login(page)
  await page.goto('/settings')
  await expect(page.locator('#display-language')).toBeVisible()
  await expect(page.locator('main.settings-view')).not.toContainText(/GPT|Temperature|Critic|Yahoo|Save Settings|Reset Defaults/)
  await expect(page.locator('main.settings-view button')).toHaveCount(0)
  const before = api.requests.length
  await page.getByLabel('Display Language', { exact: true }).selectOption('zh-TW')
  await expect(page.locator('main h1')).toHaveText('設定')
  expect(api.requests.slice(before).filter(request => request.method !== 'GET')).toHaveLength(0)
  await page.reload()
  await expect(page.locator('main h1')).toHaveText('設定')
  await expect(page.getByLabel('顯示語言', { exact: true })).toHaveValue('zh-TW')
  await page.screenshot({ path: testInfo.outputPath('settings.png'), fullPage: true })
})

test('current workflows stay concise while retired history is queryable and read-only', async ({ page }, testInfo) => {
  const queries: string[] = []
  const original = researchRun('Failed')
  const historical = { ...original, run: { ...original.run, workflowType: 'ResearchInvestigation', errorMessage: '歷史執行紀錄，保留供查閱。' } }
  await page.route('**/api/agent-runs**', async route => {
    const url = new URL(route.request().url())
    if (url.pathname === '/api/agent-runs') {
      queries.push(url.searchParams.get('workflowType') ?? '')
      return route.fulfill({ json: [historical.run] })
    }
    if (url.pathname === `/api/agent-runs/${agentId}`) return route.fulfill({ json: historical })
    return route.fallback()
  })
  await login(page)
  await page.goto('/agent-runs')
  const filter = page.getByRole('combobox', { name: 'Workflow', exact: true })
  await expect(filter.locator('option')).toHaveCount(4)
  await expect(filter.locator('optgroup')).toHaveCount(1)
  await expect(page.locator('.run-row')).toContainText('已退役 · 唯讀')
  await page.getByLabel('顯示歷史 workflow（已退役，唯讀）', { exact: true }).check()
  await expect(filter.locator('optgroup').nth(1)).toHaveAttribute('label', '歷史 workflow（已退役，唯讀）')
  await expect(filter.locator('option')).toHaveCount(10)
  await filter.selectOption('ResearchInvestigation')
  await expect.poll(() => queries.at(-1)).toBe('ResearchInvestigation')
  await expect(page.locator('.run-row')).toBeVisible()
  await expect(page.locator('.kimi-reveal')).toHaveCSS('opacity', '1')
  await page.screenshot({ path: testInfo.outputPath('agent-runs-filter.png'), fullPage: true })
  await page.locator('.run-row').click()
  await expect(page).toHaveURL(`/agent-runs/${agentId}`)
  await expect(page.getByText('已退役 · 唯讀', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '重試', exact: true })).toHaveCount(0)
  await expect(page.getByText('歷史執行紀錄，保留供查閱。', { exact: true })).toBeVisible()
})

test('Ask Agent creates a session only after its first message and receives a reply', async ({ page, api }) => {
  await login(page)
  await page.goto('/ask-agent')
  await expect(page.locator('textarea')).toBeVisible()
  await expect.poll(() => api.requests.filter(r => r.method === 'GET' && r.path === '/chat/sessions').length).toBe(1)
  expect(api.requests.filter(r => r.method === 'POST' && r.path === '/chat/sessions')).toHaveLength(0)
  await page.getByRole('button', { name: '＋ 新對話', exact: true }).click()
  expect(api.requests.filter(r => r.method === 'POST' && r.path === '/chat/sessions')).toHaveLength(0)
  await page.locator('textarea').fill('我的投資組合如何降低波動？')
  await page.locator('button.prestige-btn-solid').click()
  await expect.poll(() => api.requests.filter(r => r.method === 'POST' && r.path === '/chat/sessions').length).toBe(1)
  await expect(page.getByText('CI 固定回答：請比較減碼國巨轉現金的情境，並確認波動、VaR 與 ES。', { exact: true })).toBeVisible()
})
