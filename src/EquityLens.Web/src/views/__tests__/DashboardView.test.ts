import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import DashboardView from '../DashboardView.vue'

const authState = { isAuthenticated: true }
vi.mock('../../stores/auth', () => ({ useAuthStore: () => authState }))

vi.mock('../../services/risk', () => ({
  getPortfolios: vi.fn(),
  getPortfolioRisk: vi.fn(),
  getPortfolioValuation: vi.fn(),
  getPortfolioValuationHistory: vi.fn(),
}))
vi.mock('../../services/research', () => ({ listResearchRuns: vi.fn() }))
vi.mock('../../services/agentRuns', () => ({ listAgentRuns: vi.fn() }))

import * as risk from '../../services/risk'
import * as research from '../../services/research'
import * as agentRuns from '../../services/agentRuns'

const mocked = {
  getPortfolios: vi.mocked(risk.getPortfolios),
  getPortfolioRisk: vi.mocked(risk.getPortfolioRisk),
  getPortfolioValuation: vi.mocked(risk.getPortfolioValuation),
  getPortfolioValuationHistory: vi.mocked(risk.getPortfolioValuationHistory),
  listResearchRuns: vi.mocked(research.listResearchRuns),
  listAgentRuns: vi.mocked(agentRuns.listAgentRuns),
}

const stub = { template: '<div><slot /></div>' }

function mountDashboard() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', name: 'any', component: stub }],
  })
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(DashboardView, {
    global: {
      plugins: [router, i18n],
      stubs: { ParticleCanvas: true, ScrollReveal: stub },
    },
  })
}

const portfolio = (id: string, name: string) => ({
  id,
  name,
  description: null,
  baseCurrency: 'TWD',
  holdingCount: 2,
  createdAtUtc: '2026-11-01T00:00:00Z',
  updatedAtUtc: '2026-11-01T00:00:00Z',
})

const valuation = (portfolioId: string, totalAssetValue: number, cost: number, pnl: number) =>
  ({ portfolioId, totalAssetValue, totalCostValue: cost, totalUnrealizedPnl: pnl, holdings: [] }) as never

const riskResponse = (var1d: number, sharpe: number) =>
  ({
    to: '2026-11-02',
    dataAsOfDate: '2026-10-31',
    sharpeRatio: sharpe,
    horizons: [
      { horizonDays: 30, historicalVaR: -0.2 },
      { horizonDays: 1, historicalVaR: var1d },
    ],
  }) as never

describe('DashboardView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    authState.isAuthenticated = true
  })

  it('keeps demo data when the user is not signed in', async () => {
    authState.isAuthenticated = false
    const wrapper = mountDashboard()
    await flushPromises()

    expect(mocked.getPortfolios).not.toHaveBeenCalled()
    expect(wrapper.get('[data-testid="data-mode"]').text()).toBe('DEMO DATA')
    expect(wrapper.text()).toContain('NT$ 12,580,000')
  })

  it('keeps demo data when the user has no portfolios', async () => {
    mocked.getPortfolios.mockResolvedValue([])
    const wrapper = mountDashboard()
    await flushPromises()

    expect(wrapper.get('[data-testid="data-mode"]').text()).toBe('DEMO DATA')
    expect(mocked.getPortfolioValuation).not.toHaveBeenCalled()
  })

  it('renders portfolio, risk, research and activity data from the API', async () => {
    mocked.getPortfolios.mockResolvedValue([portfolio('p1', 'Growth'), portfolio('p2', 'Income')])
    mocked.getPortfolioValuation.mockImplementation(async (id: string) =>
      id === 'p1' ? valuation('p1', 2_000_000, 1_000_000, 900_000) : valuation('p2', 500_000, 400_000, 100_000),
    )
    mocked.getPortfolioValuationHistory.mockResolvedValue({ beta: 0.94 } as never)
    mocked.getPortfolioRisk.mockImplementation(async (id: string, params) => {
      if (id === 'p2') throw new Error('risk.insufficient_prices')
      return params.confidenceLevel === 0.99 ? riskResponse(-0.041, 2.43) : riskResponse(-0.025, 2.43)
    })
    mocked.listResearchRuns.mockResolvedValue([
      { id: 'r1', ticker: '2330', question: 'Q3 gross margin?', status: 'Failed', citationCount: 0, retrievalMode: 'Auto', latencyMs: 0, createdAtUtc: '2026-11-02T03:00:00Z' },
    ])
    mocked.listAgentRuns.mockResolvedValue([])

    const wrapper = mountDashboard()
    await flushPromises()

    expect(wrapper.get('[data-testid="data-mode"]').text()).toBe('LIVE DATA')

    const kpis = wrapper.findAll('[data-testid="kpi"]').map((k) => k.text())
    expect(kpis[0]).toContain('NT$ 2,500,000')
    expect(kpis[0]).toContain('2 portfolio(s)')
    expect(kpis[1]).toContain('+71.43%')
    expect(kpis[2]).toContain('0.94')
    expect(kpis[3]).toContain('2.43')
    expect(mocked.getPortfolioValuationHistory).toHaveBeenCalledWith('p1', expect.any(Object))

    const riskRows = wrapper.findAll('[data-testid="risk-row"]').map((r) => r.text())
    expect(riskRows).toHaveLength(2)
    expect(riskRows[0]).toContain('Growth')
    expect(riskRows[0]).toContain('2026-10-31')
    expect(riskRows[0]).toContain('-2.5%')
    expect(riskRows[0]).toContain('-4.1%')
    expect(riskRows[1]).toContain('Income')
    expect(riskRows[1]).toContain('INSUFFICIENT DATA')

    const reports = wrapper.findAll('[data-testid="report-row"]').map((r) => r.text())
    expect(reports).toHaveLength(1)
    expect(reports[0]).toContain('2330 Q3 gross margin?')
    expect(reports[0]).toContain('FAILED')

    const activities = wrapper.findAll('[data-testid="activity-row"]').map((a) => a.text())
    expect(activities[0]).toContain('AI research failed')
    expect(activities.some((a) => a.includes('Growth'))).toBe(true)
    expect(wrapper.text()).not.toContain('NT$ 12,580,000')
  })
})
