import { test, expect, login, researchRun, agentId } from './fixtures'

test('Settings persists language and the real risk preference', async ({ page, api }, testInfo) => {
  await login(page)
  await page.goto('/settings')
  await expect(page.locator('#display-language')).toBeVisible()
  await expect(page.locator('main.settings-view')).not.toContainText(/GPT|Temperature|Critic|Yahoo|Save Settings|Reset Defaults/)
  await expect(page.locator('main.settings-view button')).toHaveCount(2)
  const before = api.requests.length
  await page.getByLabel('Display Language', { exact: true }).selectOption('zh-TW')
  await expect(page.locator('main h1')).toHaveText('設定')
  expect(api.requests.slice(before).filter(request => request.method !== 'GET')).toHaveLength(0)
  await page.reload()
  await expect(page.locator('main h1')).toHaveText('設定')
  await expect(page.getByLabel('顯示語言', { exact: true })).toHaveValue('zh-TW')
  await expect(page.getByTestId('saved-risk-preference')).toContainText('預設')
  await page.getByLabel('風險偏好', { exact: true }).selectOption('Conservative')
  await page.getByRole('button', { name: '儲存風險偏好', exact: true }).click()
  await expect(page.getByRole('status')).toHaveText('風險偏好已儲存。')
  expect(api.requests.some(r => r.method === 'PUT' && r.path === '/settings/risk-preference' && r.body.value === 'Conservative')).toBe(true)
  await page.reload()
  await expect(page.getByTestId('saved-risk-preference')).toContainText('保守')
  await expect(page.getByTestId('saved-risk-preference')).not.toContainText('預設')
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

for (const role of ['User', 'Admin']) {
  test(`Agent Run detail tabs for ${role} ignore retired UI fields`, async ({ page }, testInfo) => {
    await login(page)
    await page.route('**/api/auth/me', route => route.fulfill({ json: { user: {
      id: 'ci-user', email: 'browser@example.invalid', displayName: `CI ${role}`, role,
    } } }))
    const detail = researchRun('Succeeded')
    detail.blackboardJson = JSON.stringify({
      ...JSON.parse(detail.blackboardJson),
      routingContext: { leadSkill: 'retired-skill', leadSkillDisplayName: '舊技能名稱' },
      capabilityRequestAssessment: { decision: 'Request', reason: '舊能力評估' },
      capabilityRequests: [{ capabilityId: 'retired-capability', status: 'Pending' }],
    })
    await page.route(`**/api/agent-runs/${agentId}`, route => route.fulfill({ json: detail }))
    await page.goto(`/agent-runs/${agentId}`)
    await page.getByRole('button', { name: '展開除錯資訊 ▼', exact: true }).click()
    const tabs = page.locator('.tab-bar')
    const expected = ['時間線', '節點', '工具', 'Feedback']
    if (role === 'Admin') expected.push('Blackboard', 'Workflow')
    await expect(tabs.locator('button')).toHaveText(expected)
    await expect(page.locator('[data-testid="routing-stage"]')).toHaveCount(0)
    await expect(page.locator('body')).not.toContainText(/舊技能名稱|舊能力評估|retired-capability|retired-skill/)
    await tabs.scrollIntoViewIfNeeded()
    await page.locator('.debug-panel').screenshot({ path: testInfo.outputPath(`agent-run-tabs-${role.toLowerCase()}.png`) })
    if (role === 'Admin') {
      await tabs.getByRole('button', { name: 'Blackboard', exact: true }).click()
      await expect(page.locator('.tab-pad')).toContainText('Blackboard')
      await tabs.getByRole('button', { name: 'Workflow', exact: true }).click()
      await expect(page.locator('.tab-pad')).toContainText('ResearchAgentLoop')
    }
  })
}


test('Risk assessment saves source and sublevels, reloads, and deletes answers', async ({ page }, testInfo) => {
  let profile = { value:'Balanced', label:'穩健', isDefault:true, statement:'預設', source:'Default', capacityLevel:null as string|null, willingnessLevel:null as string|null, assessedAt:null as string|null }
  const defaults = { ...profile }
  let submissions = 0
  await page.route('**/api/settings/risk-preference**', async route => {
    const req=route.request()
    if (req.method()==='POST') {
      expect(req.postDataJSON()).toEqual({ c1:1,c2:3,c3:3,w1:3,w2:3,w3:0 })
      submissions++
      profile={ ...profile,value:'Conservative',label:'保守',isDefault:false,source:'Questionnaire',capacityLevel:'Conservative',willingnessLevel:'Aggressive',assessedAt:'2026-11-04T00:00:00Z' }
    }
    if (req.method()==='DELETE') profile={...defaults}
    return route.fulfill({json:profile})
  })
  await login(page); await page.goto('/settings')
  await page.locator('#display-language').selectOption('zh-TW')
  await expect(page.getByTestId('saved-risk-preference')).toContainText('尚未評估')
  await page.screenshot({path:testInfo.outputPath('settings-unassessed.png'),fullPage:true})
  await page.getByTestId('assess-risk').click()
  await expect(page.getByTestId('submit-questionnaire')).toBeDisabled()
  for (const [key,value] of Object.entries({c1:1,c2:3,c3:3,w1:3,w2:3,w3:0})) await page.locator(`#risk-${key}`).selectOption(String(value))
  await page.getByTestId('submit-questionnaire').click()
  await expect(page.getByTestId('risk-sublevels')).toContainText('承受能力： 保守')
  expect(submissions).toBe(1)
  await page.reload()
  await expect(page.getByTestId('risk-sublevels')).toContainText('承受意願： 積極')
  await expect(page.locator('main.settings-view')).toContainText('問卷評估')
  await page.screenshot({path:testInfo.outputPath('settings-assessed.png'),fullPage:true})
  expect(await page.evaluate(() => JSON.stringify(localStorage))).not.toContain('c1')
  await page.getByTestId('delete-risk').click()
  await expect(page.getByTestId('saved-risk-preference')).toContainText('尚未評估')
  await expect(page.getByTestId('risk-sublevels')).toHaveCount(0)
})
