import { test as base, expect, type Page } from '@playwright/test'

export const portfolioId = '11111111-1111-4111-8111-111111111111'
export const riskId = '22222222-2222-4222-8222-222222222222'
export const agentId = '33333333-3333-4333-8333-333333333333'
const now = '2026-11-03T01:00:00Z'
const user = { id: 'ci-user', email: 'browser@example.invalid', displayName: 'CI User', role: 'User' }
const auth = { accessToken: 'ci-browser-token', refreshToken: 'ci-refresh', tokenType: 'Bearer', expiresIn: 3600, user }
export const question = '台積電財報有哪些主要風險？'
export const answer = '這是 CI 固定回應：台積電財報應檢視營運與資本支出風險。[1]'
const holding = { id: 'holding-1', securityId: 'security-2330', ticker: '2330', exchange: 'TWSE', securityName: '台積電', quantity: 10, averageCost: 100, costCurrency: 'TWD', note: null, updatedAtUtc: now }
const riskHolding = { ...holding, weight: 1, annualizedVolatility: .3, dataPointCount: 252, industry: '半導體', componentVolatility: .3, componentRiskShare: 1, marginalVolatility: .3, incrementalVolatility: .3 }
const conditionalRisk = { methodVersion: 'ci-fixture', horizonDays: 1, annualizationDays: 252, dailyVolatility: .02, annualizedVolatility: .3, holdings: [riskHolding], industries: [] }
const risk = { portfolioId, from: '2025-11-03', to: '2026-11-02', baseCurrency: 'TWD', holdingCount: 1, pricedHoldingCount: 1, alignedReturnCount: 252, totalMarketValue: 1100, historicalAnnualizedVolatility: .3, maxDrawdown: -.15, sharpeRatio: .5, confidenceLevel: .95, simulations: 1000, volatilityMethod: 'ci-fixture', ewmaLambda: .94, driftAssumption: 'zero', supportedHorizons: [], dataAsOfDate: '2026-11-02', concentrationHhi: 1, largestHoldingWeight: 1, horizons: [], holdings: [riskHolding], industries: [], riskSourceAnnualizedVolatility: .3, dailyLogReturns: [] }
const riskRun = { id: riskId, portfolioId, operation: 'risk', status: 'Completed', progressPercent: 100, requestedModel: 'vt_garch', selectedModel: 'VT-GARCH-t + Joint-Vector FHS', algorithmVersion: 'ci', inputHash: 'fixture', dataFactorVersion: null, fallbackReason: null, fallbackDepth: 0, createdAtUtc: now, startedAtUtc: now, completedAtUtc: now, errorCode: null, errorMessage: null, qualityEvaluation: null, result: { horizons: [], confidenceCurve: [], conditionalRisk, historicalAnnualizedVolatility: .3 }, inputSnapshot: { from: '2025-11-03', to: '2026-11-02' } }

export function researchRun(status = 'Succeeded', rejected = false) {
  const keys = ['validate', 'detectIntent', 'researchAgent', 'qualityGate', 'finalizeResearch']
  const types = ['Validate', 'DetectIntent', 'ResearchAgentLoop', 'QualityGate', 'FinalizeResearch']
  return {
    run: { id: agentId, workflowType: 'ResearchAgent', agentType: 'ResearchAgent', status, createdAtUtc: now, startedAtUtc: now, completedAtUtc: status === 'Succeeded' ? now : null, errorMessage: null, parentAgentRunId: null },
    nodes: keys.map((key, i) => ({ id: `node-${i}`, nodeKey: key, templateNodeKey: key, iteration: 0, nodeType: types[i], displayName: key, description: null, stage: key, status: status === 'Succeeded' ? 'Succeeded' : i < 2 ? 'Succeeded' : 'Running', inputJson: '{}', outputJson: '{}', errorMessage: null, startedAtUtc: now, completedAtUtc: now, durationMs: 5 })),
    events: [{ id: 'event-1', agentRunNodeId: 'node-2', eventType: 'AgentLoopStopped', message: '研究迴圈已完成', payloadJson: '{"reason":"AnswerReady"}', createdAtUtc: now }],
    toolCalls: [{ id: 'tool-1', agentRunNodeId: 'node-2', toolName: 'search_local', status: 'Succeeded', argumentsJson: '{"ticker":"2330"}', resultJson: '{}', resultPreview: '台積電財報證據', startedAtUtc: now, completedAtUtc: now, durationMs: 5, errorMessage: null }],
    feedback: [], approvals: [], llmTurns: [],
    workflowDefinitionJson: JSON.stringify({ nodes: keys.map((id, i) => ({ id, type: types[i] })), edges: keys.slice(1).map((to, i) => ({ from: keys[i], to })) }),
    blackboardJson: JSON.stringify({ researchAgent: { ledgerSize: 1 }, researchRun: { citations: [{ citationIndex: 1, title: '台積電 CI 財報 fixture', pageNumber: 1, quoteText: '固定測試證據，不是實際財報結論。' }] } }),
    outputJson: JSON.stringify({ answer: rejected ? '證據不足，無法回答。' : answer, status: rejected ? 'InsufficientEvidence' : 'Answered', errorCode: rejected ? 'quality_gate_rejected' : null, rejectedNumbers: rejected ? ['999%'] : [], outputGate: { passed: !rejected }, criticReview: { recommendedNextAction: 'AcceptAnswer' } }),
  }
}

export class MockApi {
  requests: Array<{ method: string; path: string; body: any; headers: Record<string, string> }> = []
  unexpected: string[] = []
  errors: string[] = []
  riskPreference: string | null = null
  hasPortfolio = false
  hasHolding = false
  rejectLogin = false
  failResearch = false
  rejectedAnswer = false
  researchPolls = 0
  snapshotSaved = false
  chatQuestion = ''
  portfolioName = 'CI 投資組合'

  portfolio() { return { id: portfolioId, name: this.portfolioName, description: null, baseCurrency: 'TWD', createdAtUtc: now, updatedAtUtc: now, holdings: this.hasHolding ? [holding] : [] } }
  valuation() { return { portfolioId, currency: 'TWD', asOfDate: '2026-11-02', holdings: this.hasHolding ? [{ ...holding, latestPrice: 110, marketValue: 1100, costValue: 1000, unrealizedPnl: 100, unrealizedPnlPercent: .1, weight: 1 }] : [], totalCostValue: this.hasHolding ? 1000 : 0, totalMarketValue: this.hasHolding ? 1100 : 0, totalUnrealizedPnl: this.hasHolding ? 100 : 0, totalUnrealizedPnlPercent: .1, cashBalance: 0, totalAssetValue: this.hasHolding ? 1100 : 0, totalRealizedPnl: 0, todayPnl: null, twr: null, xirr: null } }

  async install(page: Page) {
    page.on('pageerror', error => this.errors.push(error.message))
    await page.route('**/*', async route => {
      const request = route.request()
      const url = new URL(request.url())
      // Only the test preview may be contacted. No production API, model, or CDN traffic.
      if (url.href === 'https://accounts.google.com/gsi/client') {
        // Password login is in scope; OAuth is deliberately a local SDK stub.
        return route.fulfill({ contentType: 'application/javascript', body: 'window.google={accounts:{id:{initialize(){},renderButton(){},prompt(){},disableAutoSelect(){}}}};' })
      }
      if (url.origin !== 'http://127.0.0.1:5186') {
        if (!['fonts.googleapis.com', 'fonts.gstatic.com', 'accounts.google.com'].includes(url.hostname)) this.unexpected.push(`external ${url.origin}`)
        return route.abort()
      }
      if (!url.pathname.startsWith('/api/')) return route.continue()
      const path = url.pathname.slice(4)
      const method = request.method()
      const body = request.postData() ? request.postDataJSON() : null
      this.requests.push({ method, path, body, headers: request.headers() })
      const reply = (json: unknown, status = 200) => route.fulfill({ status, json })
      if (path === '/settings/risk-preference' && (method === 'GET' || method === 'PUT')) {
        if (method === 'PUT') this.riskPreference = body.value
        const value = this.riskPreference ?? 'Balanced'
        return reply({ value, label: value === 'Balanced' ? '穩健' : value === 'Conservative' ? '保守' : '積極', isDefault: this.riskPreference === null, statement: '測試偏好' })
      }
      if (method === 'POST' && path === '/auth/login') return this.rejectLogin ? reply({ message: 'Invalid credentials' }, 401) : reply(auth)
      if (method === 'GET' && path === '/auth/me') return reply({ user })
      if (method === 'GET' && path === '/market/ticker') return reply([])
      if (method === 'GET' && path === '/chat/sessions') return reply([])
      if (method === 'POST' && path === '/chat/sessions') return reply({ id: 'session-1', title: null, messageCount: 0, createdAtUtc: now, updatedAtUtc: now })
      if (method === 'POST' && path === '/chat/sessions/session-1/messages') {
        this.chatQuestion = body.content
        const card = this.chatCard('Running')
        const events = [{ type: 'run_created', messageId: 'message-1', runCard: card }, { type: 'done', model: 'ci-fixture', promptTokens: 0, completionTokens: 0 }]
        return route.fulfill({ contentType: 'text/event-stream', body: events.map(event => `data: ${JSON.stringify(event)}\n\n`).join('') })
      }
      if (method === 'GET' && path === `/chat/sessions/session-1/run-cards/${agentId}`) return reply(this.chatCard('Succeeded'))
      if (method === 'GET' && path === '/portfolios') return reply(this.hasPortfolio ? [{ ...this.portfolio(), holdingCount: this.hasHolding ? 1 : 0 }] : [])
      if (method === 'POST' && path === '/portfolios') { this.hasPortfolio = true; this.portfolioName = body.name; return reply(this.portfolio(), 201) }
      if (method === 'GET' && path === `/portfolios/${portfolioId}`) return reply(this.portfolio())
      if (method === 'GET' && path === `/portfolios/${portfolioId}/valuation`) return reply(this.valuation())
      if (method === 'GET' && path === `/portfolios/${portfolioId}/valuation/history`) return reply({ portfolioId, currency: 'TWD', points: [], benchmark: [], twr: null, xirr: null })
      if (method === 'GET' && [`/portfolios/${portfolioId}/transactions`, `/portfolios/${portfolioId}/cash-flows`, `/portfolios/${portfolioId}/dividends/cash-flows`, `/portfolios/${portfolioId}/dividends/latest`, '/securities/security-2330/prices'].includes(path)) return reply([])
      if (method === 'GET' && path === '/securities/search') return reply([{ securityId: holding.securityId, ticker: '2330', exchange: 'TWSE', name: '台積電', currency: 'TWD', source: 'Local', assetType: 'Stock' }])
      if (method === 'POST' && path === '/securities/resolve') return reply({ securityId: holding.securityId, ticker: '2330', name: '台積電', exchange: 'TWSE', created: false })
      if (method === 'POST' && path === '/securities/security-2330/prices/sync') return reply({ synced: true, skipped: false, importedCount: 0 })
      if (method === 'POST' && path === `/portfolios/${portfolioId}/transactions`) { this.hasHolding = true; return reply({ ...body, id: 'transaction-1', ticker: '2330', securityName: '台積電', createdAtUtc: now }, 201) }
      if (method === 'GET' && path === `/portfolios/${portfolioId}/risk`) return reply(risk)
      if (method === 'GET' && path === `/portfolios/${portfolioId}/risk/calculations`) return reply([riskRun])
      if (method === 'GET' && path === `/portfolios/${portfolioId}/risk/governance`) return reply({ dataStatus: 'ready', commonTradingDays: 252, alerts: [] })
      if (method === 'GET' && path === `/portfolios/${portfolioId}/risk/reports`) return reply([])
      if (method === 'POST' && path === `/portfolios/${portfolioId}/risk/reports/from-calculation/${riskId}`) { this.snapshotSaved = true; return reply({ id: 'snapshot-1', model: riskRun.selectedModel, overallStatus: 'Unvalidated', snapshot: { source: { runId: riskId } } }, 201) }
      if (method === 'GET' && path === '/research/runs') return reply([])
      if (method === 'POST' && path === '/research/investigations') return this.failResearch ? reply({ message: 'fixture unavailable' }, 503) : reply({ agentRunId: agentId, researchRunId: 'research-1', workflowType: 'ResearchAgent', status: 'Running' }, 201)
      if (method === 'GET' && path === `/agent-runs/${agentId}`) {
        this.researchPolls++
        const run = researchRun(this.researchPolls === 1 ? 'Running' : 'Succeeded', this.rejectedAnswer)
        if (this.chatQuestion.includes('投資組合')) {
          run.run.workflowType = 'PortfolioDiagnosis'
          run.nodes = []
          run.outputJson = JSON.stringify({ summary: 'CI 投組風險比較', portfolioReturn: .05, benchmarkReturn: .03, activeReturn: .02,
            mainDrags: [], mainContributors: [], recommendedAnalyses: [], evidenceStatus: 'complete', attributionCoverage: 'complete',
            riskMetrics: { annualizedVolatility: .3, maxDrawdown: -.15 },
            interpretation: Array.from({ length: 80 }, (_, i) => `第 ${i + 1} 段：固定情境比較，請檢視波動、VaR 與 ES，這不是實際投資建議。`).join('\n\n') })
        }
        return reply(run)
      }
      this.unexpected.push(`${method} ${path}`)
      return reply({ message: `Unmocked request: ${method} ${path}` }, 500)
    })
  }
  chatCard(status: string) { return { agentRunId: agentId, researchRunId: null, workflowType: this.chatQuestion.includes('投資組合') ? 'PortfolioDiagnosis' : 'ResearchAgent', status, currentStage: 'loop', currentStageDisplayName: '分析中', completedNodes: status === 'Succeeded' ? 5 : 1, totalNodes: 5, finalAnswer: status === 'Succeeded' ? 'CI 固定回答：請比較減碼國巨轉現金的情境，並確認波動、VaR 與 ES。' : '未驗證草稿不得顯示', errorMessage: null } }
}

export const test = base.extend<{ api: MockApi }>({
  api: [async ({ page }, use) => {
    const api = new MockApi()
    await api.install(page)
    await use(api)
    expect(api.unexpected, 'Every API request must be explicitly mocked').toEqual([])
    expect(api.errors, 'Browser must have no uncaught exceptions').toEqual([])
  }, { auto: true }],
})
export { expect }

export async function login(page: Page) {
  await page.goto('/login')
  await page.locator('input[type=email]').fill(user.email)
  await page.locator('input[type=password]').fill('ci-only-password')
  await page.locator('button[type=submit]').click()
  await expect(page).toHaveURL('/portfolios')
  await expect(page.getByRole('heading', { name: '投資組合', exact: true })).toBeVisible()
}
