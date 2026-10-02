import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { RiskCalculationRun } from '../../../services/risk'

vi.mock('../../../services/http', () => ({ http: {} }))
vi.mock('vue-router', () => ({ useRoute: () => ({ params: { id: 'p' } }), useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../../services/risk', async importOriginal => ({
  ...await importOriginal<typeof import('../../../services/risk')>(),
  getPortfolio: vi.fn().mockResolvedValue({ name: 'Test Portfolio' }),
  listRiskCalculations: vi.fn(),
  getPortfolioRiskGovernance: vi.fn().mockResolvedValue({ dataStatus: 'ready', commonTradingDays: 252, alerts: [] }),
  getPortfolioValuation: vi.fn().mockResolvedValue({ totalMarketValue: 1000, currency: 'TWD' }),
  getPortfolioRiskReportSnapshots: vi.fn().mockResolvedValue([]),
  createRiskCalculation: vi.fn(),
  calculatePortfolioRiskScenario: vi.fn(),
}))
import RiskRunDetailView from '../RiskRunDetailView.vue'
import { listRiskCalculations, calculatePortfolioRiskScenario, createRiskCalculation } from '../../../services/risk'

const holding = { securityId: 'a', ticker: '2330', securityName: 'Snapshot Name', exchange: 'TWSE', industry: '科技',
  weight: 1, annualizedVolatility: .16, dataPointCount: 252, componentVolatility: .16,
  componentRiskShare: 1, marginalVolatility: .16, incrementalVolatility: .16 }
const conditionalRisk = { methodVersion: 'v1', horizonDays: 1, annualizationDays: 252, dailyVolatility: .01,
  annualizedVolatility: .16, holdings: [holding], industries: [] }
const mountView = () => mount(RiskRunDetailView, { global: { stubs: {
  ScrollReveal: { template: '<div><slot /></div>' }, Footer: true,
} } })

beforeEach(() => {
  vi.clearAllMocks()
  // Keep unrelated deferred network requests out of this result-rendering test.
  vi.stubGlobal('IntersectionObserver', class { observe() {} unobserve() {} disconnect() {} })
})
afterEach(() => vi.unstubAllGlobals())

describe('risk detail result routing', () => {
  it.each(['new-python', 'old-python', 'fallback'])('renders %s without mixing scenario models', async kind => {
    const result = kind === 'fallback'
      ? { sharpeRatio: 1, concentrationHhi: 1, largestHoldingWeight: 1, historicalAnnualizedVolatility: .3, riskSourceAnnualizedVolatility: .16, holdings: [holding], horizons: [], industries: [] }
      : { historicalAnnualizedVolatility: .3, horizons: [], confidenceCurve: [], ...(kind === 'new-python' ? { conditionalRisk } : {}) }
    vi.mocked(listRiskCalculations).mockResolvedValue([{
      id: 'run-123', operation: 'risk', status: 'Completed', result,
      selectedModel: kind === 'fallback' ? 'C# MVEWMA-FHS' : 'VT-GARCH-t + Joint-Vector FHS', fallbackDepth: kind === 'fallback' ? 1 : 0,
    } as RiskCalculationRun])
    const wrapper = mountView()
    try {
      await flushPromises()
      expect(createRiskCalculation).not.toHaveBeenCalled()
      expect(calculatePortfolioRiskScenario).not.toHaveBeenCalled()
      if (kind === 'new-python') {
        expect(wrapper.text()).toContain('條件波動率（年化）')
        expect(wrapper.text()).toContain('16.00%')
        expect(wrapper.text()).toContain('Snapshot Name')
        expect(wrapper.text()).not.toContain('調整試算')
      } else if (kind === 'old-python') {
        expect(wrapper.text()).toContain('未提供條件波動率，需重新計算')
        expect(wrapper.text()).toContain('歷史年化波動率 30.00%')
        expect(wrapper.text()).not.toContain('持倉風險摘要')
      } else {
        expect(wrapper.text()).toContain('C# MVEWMA-FHS 回退資料')
        expect(wrapper.text()).toContain('調整試算')
        expect(wrapper.text()).not.toContain('條件波動率（年化）')
      }
    } finally { wrapper.unmount() }
  })
})
