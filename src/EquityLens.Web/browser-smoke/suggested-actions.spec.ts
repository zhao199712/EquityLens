import { test, expect, login, agentId } from './fixtures'

const volatility = {
  id: 'portfolio-reduce-volatility', label: '如何降低波動率？',
  prompt: '接著，針對投資組合「高beta」，如何降低波動率？請依可用資料比較可行方向並說明取捨。',
  labelEn: 'How can I reduce volatility?',
  promptEn: 'For portfolio “高beta”, how could volatility be reduced? Compare feasible approaches using available data and explain the trade-offs.',
}
const contributors = {
  id: 'portfolio-risk-contributors', label: '哪些持股貢獻最多風險？',
  prompt: '接著，針對投資組合「高beta」，哪些持股對風險的貢獻最大？',
  labelEn: 'Which holdings contribute the most risk?',
  promptEn: 'For portfolio “高beta”, which holdings contribute the most risk?',
}
const scenario = {
  id: 'scenario-remaining-risks', label: '調整後還有哪些風險？',
  prompt: '接著，針對投資組合「高beta」，請根據已完成的情境比較，說明調整後仍存在的風險與限制。',
  labelEn: 'What risks remain after the adjustment?',
  promptEn: 'For portfolio “高beta”, explain the remaining risks and limitations using the completed scenario comparison.',
}

for (const locale of ['zh-TW', 'en']) {
  test(`suggested question fills a draft only, remains editable and survives IME confirmation (${locale})`, async ({ page }) => {
    await page.addInitScript(value => localStorage.setItem('equitylens-locale', value), locale)
    if (locale === 'zh-TW') await page.setViewportSize({ width: 390, height: 844 })
    await login(page)
    let latest = true
    const posts: Array<{ content: string }> = []
    const card = (status = 'Succeeded') => ({
      agentRunId: agentId, researchRunId: null, workflowType: 'PortfolioDiagnosis', status,
      currentStage: null, currentStageDisplayName: null, completedNodes: 6, totalNodes: 6,
      finalAnswer: '已完成的風險分析', errorMessage: null, qualityIssues: [],
      suggestedActions: status === 'Succeeded' && latest ? [volatility, contributors] : [],
    })
    await page.route('**/api/chat/**', async route => {
      const path = new URL(route.request().url()).pathname
      if (path.endsWith('/sessions')) return route.fulfill({ json: [{ id: 'follow-up-session', title: '高beta分析', messageCount: 2, updatedAtUtc: '2026-11-04T00:00:00Z' }] })
      if (path.endsWith('/messages')) {
        if (route.request().method() === 'POST') {
          const body = route.request().postDataJSON(); posts.push(body); latest = false
          const events = [{ type: 'delta', content: '已重置對話' }, { type: 'done', model: 'mock', promptTokens: 0, completionTokens: 0 }]
          return route.fulfill({ contentType: 'text/event-stream', body: events.map(event => `data: ${JSON.stringify(event)}\n\n`).join('') })
        }
        const messages: Array<{ id: string; role: string; content: string; runCard: ReturnType<typeof card> | null }> = [{ id: 'answer', role: 'assistant', content: '分析完成', runCard: card(latest ? 'Running' : 'Succeeded') }]
        if (!latest) messages.push({ id: 'reset', role: 'assistant', content: '已重置對話', runCard: null })
        return route.fulfill({ json: messages })
      }
      if (path.includes('/run-cards/')) return route.fulfill({ json: card() })
      return route.abort()
    })
    await page.goto('/ask-agent')
    await page.locator('.session-row').click()
    await expect(page.locator('.run-status')).toHaveText(locale === 'en' ? 'Running' : '分析中')
    await expect(page.locator('.suggested-action-btn').first()).toBeVisible()
    const button = page.getByRole('button', { name: locale === 'en' ? volatility.labelEn : volatility.label, exact: true })
    const composer = page.locator('textarea')
    await composer.fill('existing draft')
    await expect(button).toBeDisabled()
    await composer.fill('')
    await button.focus()
    await button.press('Enter')
    await expect(composer).toBeFocused()
    await expect(composer).toHaveValue(locale === 'en' ? volatility.promptEn : volatility.prompt)
    expect(posts).toHaveLength(0)
    await expect(button).toBeDisabled()
    if (locale === 'zh-TW') {
      expect(await page.locator('.bubble').evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true)
      await page.screenshot({ path: '/tmp/equitylens-followups-mobile.png', fullPage: true })
    }
    const edited = (locale === 'en' ? volatility.promptEn : volatility.prompt).replace('高beta', '退休')
    await composer.fill(edited)
    await composer.dispatchEvent('keydown', { key: 'Enter', isComposing: true })
    await composer.dispatchEvent('keydown', { key: 'Enter', keyCode: 229 })
    expect(posts).toHaveLength(0)
    await page.locator('button.prestige-btn-solid').click()
    await expect.poll(() => posts.length).toBe(1)
    expect(posts[0]!.content).toBe(edited)
    await expect(page.locator('.suggested-actions')).toHaveCount(0)
    await page.reload()
    await page.locator('.session-row').click()
    await expect(page.getByText('已重置對話', { exact: true })).toBeVisible()
    await expect(page.locator('.suggested-actions')).toHaveCount(0)
  })
}

test('completed scenario offers explanation questions rather than another optimization', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('equitylens-locale', 'zh-TW'))
  await login(page)
  await page.route('**/api/chat/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/sessions')) return route.fulfill({ json: [{ id: 'scenario-session', title: '情境比較', messageCount: 2, updatedAtUtc: '2026-11-04T00:00:00Z' }] })
    if (path.endsWith('/messages')) return route.fulfill({ json: [{ id: 'answer', role: 'assistant', content: '完成', runCard: {
      agentRunId: agentId, workflowType: 'PortfolioDiagnosis', status: 'Succeeded', finalAnswer: '已完成情境比較',
      completedNodes: 6, totalNodes: 6, suggestedActions: [scenario],
    } }] })
    return route.abort()
  })
  await page.goto('/ask-agent')
  await page.locator('.session-row').click()
  await expect(page.getByRole('button', { name: scenario.label, exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: volatility.label, exact: true })).toHaveCount(0)
})
