import { test, expect, login, portfolioId, riskId, agentId, question, answer } from './fixtures'

test('public mobile navigation and production 404', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/home/lab')
  await expect(page.getByText('404', { exact: true })).toBeVisible()
  const menu = page.locator('button[aria-controls="kimi-main-nav"]')
  await menu.click()
  await expect(menu).toHaveAttribute('aria-expanded', 'true')
  await page.locator('#kimi-main-nav').getByRole('button', { name: 'Home', exact: true }).click()
  await expect(page).toHaveURL('/')
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  await expect(page.locator('#kimi-main-nav')).not.toContainText('Portfolio')
})

test('failed login stays logged out and protected routes redirect', async ({ page, api }) => {
  api.rejectLogin = true
  await page.goto('/portfolios')
  await expect(page).toHaveURL(/\/login/)
  await page.locator('input[type=email]').fill('invalid@example.invalid')
  await page.locator('input[type=password]').fill('invalid-password')
  await page.locator('button[type=submit]').click()
  await expect(page.locator('.prestige-error')).toBeVisible()
  expect(await page.evaluate(() => sessionStorage.getItem('auth_token'))).toBeNull()
})

test('login, create portfolio, add TSMC transaction and preserve it on reload', async ({ page, api }) => {
  await login(page)
  await page.getByRole('button', { name: '+ 建立組合', exact: true }).click()
  await page.getByPlaceholder('組合名稱', { exact: true }).fill('CI 台積電投組')
  await page.getByRole('button', { name: '建立', exact: true }).click()
  await page.getByRole('heading', { name: 'CI 台積電投組' }).click()
  await expect(page).toHaveURL(`/portfolios/${portfolioId}`)
  await expect(page.getByText('此投資組合尚無持倉。', { exact: false })).toBeVisible()
  expect(api.requests.filter(r => r.path === `/portfolios/${portfolioId}/risk`)).toHaveLength(0)
  await page.getByRole('button', { name: '+ ADD STOCK', exact: true }).click()
  await page.getByPlaceholder('搜尋股票代號或名稱，例如 TSM / AAPL / 2330').fill('2330')
  await page.getByRole('button', { name: 'SEARCH', exact: true }).click()
  await expect(page.getByText('已選擇：2330 / TWSE / 台積電')).toBeVisible()
  await page.getByPlaceholder('例如 10', { exact: true }).fill('10')
  await page.getByPlaceholder('例如 150.5', { exact: true }).fill('100')
  await page.getByRole('button', { name: 'ADD', exact: true }).click()
  await expect.poll(() => api.hasHolding).toBe(true)
  const transaction = api.requests.find(r => r.method === 'POST' && r.path.endsWith('/transactions'))!
  expect(transaction.body).toMatchObject({ securityId: 'security-2330', quantity: 10, price: 100, transactionType: 'Buy' })
  expect(transaction.headers['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
  expect(transaction.headers.authorization).toBe('Bearer ci-browser-token')
  await page.reload()
  await expect(page.getByText('台積電', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('此投資組合尚無持倉。', { exact: false })).toHaveCount(0)
})

test('risk detail uses saved result and saves that exact run without recalculation', async ({ page, api }) => {
  api.hasPortfolio = api.hasHolding = true
  await login(page)
  await page.goto(`/risk-runs/${portfolioId}`)
  await expect(page.locator('.overview-metrics').getByText('歷史年化波動率', { exact: true })).toBeVisible()
  await expect(page.getByText('30.00%', { exact: true }).first()).toBeVisible()
  await page.getByRole('button', { name: '儲存風險報告', exact: true }).click()
  await expect(page.getByText(/快照已建立/)).toBeVisible()
  expect(api.snapshotSaved).toBe(true)
  expect(api.requests.filter(r => r.method === 'POST').map(r => r.path)).toContain(`/portfolios/${portfolioId}/risk/reports/from-calculation/${riskId}`)
  expect(api.requests.filter(r => r.method === 'POST' && r.path.endsWith('/calculations'))).toHaveLength(0)
})

test('TSMC research polls to final answer and shows citations, nodes and tool records', async ({ page, api }) => {
  await login(page)
  await page.goto('/research')
  await page.getByRole('button', { name: '+ 提出研究問題' }).click()
  await page.getByPlaceholder('股票代號（如 2330）').fill('2330')
  await page.getByPlaceholder('研究問題', { exact: true }).fill(question)
  await page.getByRole('button', { name: '送出研究問題' }).click()
  await expect(page).toHaveURL(`/agent-runs/${agentId}`)
  await expect(page.getByText('研究進行中，完成品質檢查後顯示最終答案。')).toBeVisible()
  await expect(page.getByText(answer, { exact: true })).toBeVisible()
  await page.getByText(/台積電 CI 財報 fixture/).click()
  await expect(page.getByText('固定測試證據，不是實際財報結論。')).toBeVisible()
  await page.getByRole('button', { name: '展開除錯資訊 ▼' }).click()
  await expect(page.getByText('研究迴圈已完成', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '節點', exact: true }).click()
  await expect(page.getByText('ResearchAgentLoop', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '工具', exact: true }).click()
  await expect(page.getByText('search_local', { exact: true })).toBeVisible()
  expect(api.requests.find(r => r.path === '/research/investigations')?.body).toEqual({ ticker: '2330', question, sourcePolicy: 'Auto' })
  await page.reload()
  await expect(page.getByText(answer, { exact: true })).toBeVisible()
})

test('research API error is visible and does not create a phantom run', async ({ page, api }) => {
  api.failResearch = true
  await login(page)
  await page.goto('/research')
  await page.getByRole('button', { name: '+ 提出研究問題' }).click()
  await page.getByPlaceholder('股票代號（如 2330）').fill('2330')
  await page.getByPlaceholder('研究問題', { exact: true }).fill(question)
  await page.getByRole('button', { name: '送出研究問題' }).click()
  await expect(page.getByText('研究請求失敗，請稍後再試。')).toBeVisible()
  await expect(page).toHaveURL('/research')
  expect(api.requests.some(r => r.path.startsWith('/agent-runs/'))).toBe(false)
})

test('successful execution with rejected answer displays quality rejection', async ({ page, api }) => {
  api.rejectedAnswer = true
  api.researchPolls = 1
  await login(page)
  await page.goto(`/agent-runs/${agentId}`)
  await expect(page.getByText('證據不足，無法回答。', { exact: true })).toBeVisible()
  await expect(page.getByText('品質檢查拒絕：模型答案未通過，已改為拒答或系統風險說明。')).toBeVisible()
  await expect(page.getByText('未能追溯：999%')).toBeVisible()
})

for (const prompt of ['如何優化我的投資組合波動？', question]) {
  test(`Ask Agent SSE and polling: ${prompt}`, async ({ page, api }) => {
    await login(page)
    await page.goto('/ask-agent')
    await page.locator('textarea').fill(prompt)
    await page.locator('button.prestige-btn-solid').click()
    await expect(page.locator('.run-card')).toBeVisible()
    await expect(page.getByText('未驗證草稿不得顯示')).toHaveCount(0)
    await expect(page.locator('.final-answer')).toContainText('CI 固定回答')
    expect(api.chatQuestion).toBe(prompt)
    expect(api.requests.find(r => r.method === 'POST' && r.path.endsWith('/messages'))?.body.requestId).toMatch(/^[0-9a-f-]{36}$/)
    await page.locator(`.run-card a[href="/agent-runs/${agentId}"]`).click()
    await expect(page).toHaveURL(`/agent-runs/${agentId}`)
    if (prompt.includes('投資組合')) {
      const report = page.locator('.interpretation')
      await expect(report).toBeVisible()
      await expect(report).toContainText('第 80 段')
      expect(await report.evaluate(element => {
        for (let node: Element | null = element; node; node = node.parentElement) {
          if (getComputedStyle(node).opacity === '0') return false
        }
        return element.getBoundingClientRect().height > 1000
      })).toBe(true)
    }
  })
}
