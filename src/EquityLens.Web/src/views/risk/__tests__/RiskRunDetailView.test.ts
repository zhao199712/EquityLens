import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { RiskCalculationRun } from '../../../services/risk'

vi.mock('../../../services/http', () => ({ http: {} }))
vi.mock('vue-router', () => ({ useRoute: () => ({ params: { id: 'p' } }), useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../../services/risk', async importOriginal => ({
  ...await importOriginal<typeof import('../../../services/risk')>(),
  getPortfolio: vi.fn().mockResolvedValue({ name: 'Test Portfolio', holdings: [{ securityId: 'a' }] }),
  listRiskCalculations: vi.fn(),
  getPortfolioRiskGovernance: vi.fn().mockResolvedValue({ dataStatus: 'ready', commonTradingDays: 252, alerts: [] }),
  getPortfolioValuation: vi.fn().mockResolvedValue({ totalMarketValue: 1000, currency: 'TWD' }),
  getPortfolioRiskReportSnapshots: vi.fn().mockResolvedValue([]),
  createRiskCalculation: vi.fn(),
  getPortfolioRisk: vi.fn().mockResolvedValue({ horizons: [], confidenceLevel: .95 }),
  getPortfolioRiskBacktestRun: vi.fn(),
  getRiskCalculation: vi.fn(),
  validateRiskCalculation: vi.fn(),
  getPortfolioMonteCarlo: vi.fn(),
  getPortfolioStressTest: vi.fn().mockResolvedValue({ scenarios: [] }),
  createPortfolioRiskReportSnapshot: vi.fn(),
  calculatePortfolioRiskScenario: vi.fn(),
}))
import RiskRunDetailView from '../RiskRunDetailView.vue'
import { createPortfolioRiskReportSnapshot, getPortfolioRiskReportSnapshots, getPortfolio, getPortfolioRiskGovernance, listRiskCalculations, calculatePortfolioRiskScenario, createRiskCalculation, getPortfolioRisk, getPortfolioRiskBacktestRun, getRiskCalculation, validateRiskCalculation, getPortfolioMonteCarlo, getPortfolioStressTest } from '../../../services/risk'

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
      expect(wrapper.find('.overview-metrics').text()).toContain('歷史年化波動率')
      expect(wrapper.find('.overview-metrics').text()).toContain('30.00%')
      expect(wrapper.find('.overview-metrics').text()).not.toContain('16.00%')
      expect(wrapper.find('[role="tab"][aria-selected="true"]').text()).toBe('風險總覽')
      expect(wrapper.find('.weight-grid').exists()).toBe(false)
      expect(getPortfolioRisk).not.toHaveBeenCalled()
      expect(validateRiskCalculation).not.toHaveBeenCalled()
      expect(getPortfolioMonteCarlo).not.toHaveBeenCalled()
      if (kind !== 'old-python') expect(wrapper.text()).toContain('Snapshot Name')
      else expect(wrapper.text()).toContain('未提供風險分解')
      await wrapper.find('#risk-tab-adjustments').trigger('click')
      await flushPromises()
      expect(getPortfolioStressTest).toHaveBeenCalledTimes(1)
      expect(wrapper.find('.weight-grid').exists()).toBe(kind === 'fallback')
      if (kind !== 'fallback') expect(wrapper.text()).toContain('未提供同口徑')
    } finally { wrapper.unmount() }
  })
})

it('does not request risk calculations or governance for an empty portfolio', async () => {
  vi.mocked(getPortfolio).mockResolvedValueOnce({ name: 'Empty', holdings: [] } as never)
  const wrapper = mountView()
  await flushPromises()
  expect(createRiskCalculation).not.toHaveBeenCalled()
  expect(getPortfolioRiskGovernance).not.toHaveBeenCalled()
  expect(wrapper.text()).toContain('尚無持倉')
  wrapper.unmount()
})

it('saves the visible run and preserves success if refreshing the list fails', async () => {
  vi.mocked(listRiskCalculations).mockResolvedValue([{ id: 'visible-run', operation: 'risk', status: 'Completed',
    selectedModel: 'VT-GARCH-t + Joint-Vector FHS', result: { horizons: [], confidenceCurve: [], conditionalRisk } } as unknown as RiskCalculationRun])
  const wrapper = mountView()
  await flushPromises()
  vi.mocked(createPortfolioRiskReportSnapshot).mockResolvedValue({ id: 'saved', snapshot: { source: { runId: 'visible-run' } }, model: 'fixture', overallStatus: 'Failed' } as never)
  vi.mocked(getPortfolioRiskReportSnapshots).mockRejectedValueOnce(new Error('list unavailable'))
  await wrapper.findAll('button').find(b => b.text().includes('儲存風險報告'))!.trigger('click')
  await flushPromises()
  expect(createPortfolioRiskReportSnapshot).toHaveBeenCalledWith('p', 'visible-run')
  expect(wrapper.text()).toContain('快照已建立')
  expect(wrapper.text()).toContain('本次回測未通過基本檢查')
  expect(wrapper.find('pre').exists()).toBe(false)
  wrapper.unmount()
})

const run = (fallback = false) => ({ id: 'run-123', operation: 'risk', status: 'Completed',
  selectedModel: fallback ? 'C# MVEWMA-FHS' : 'VT-GARCH-t + Joint-Vector FHS',
  result: fallback ? { holdings: [holding], horizons: [{ horizonDays: 1, monteCarloVaR: -.04, monteCarloES: -.06 }], historicalAnnualizedVolatility: .3, industries: [] }
    : { horizons: [], confidenceCurve: [], conditionalRisk, historicalAnnualizedVolatility: .3 },
  qualityEvaluation: null,
}) as unknown as RiskCalculationRun

it('loads legacy diagnostics only on the methods tab, once, without changing saved values', async () => {
  vi.mocked(listRiskCalculations).mockResolvedValue([run(true)])
  const wrapper = mountView()
  await flushPromises()
  expect(getPortfolioRisk).not.toHaveBeenCalled()
  const before = wrapper.find('.overview-metrics').text()
  await wrapper.find('#risk-tab-methods').trigger('click')
  await flushPromises()
  expect(getPortfolioRisk).toHaveBeenCalledTimes(9)
  expect(validateRiskCalculation).not.toHaveBeenCalled()
  expect(getPortfolioMonteCarlo).not.toHaveBeenCalled()
  expect(createRiskCalculation).not.toHaveBeenCalled()
  await wrapper.find('#risk-tab-overview').trigger('click')
  expect(wrapper.find('.overview-metrics').text()).toBe(before)
  await wrapper.find('#risk-tab-methods').trigger('click')
  await flushPromises()
  expect(getPortfolioRisk).toHaveBeenCalledTimes(9)
  wrapper.unmount()
})

it('uses the stored Python curve without extra diagnostic requests or implicit jobs', async () => {
  vi.mocked(listRiskCalculations).mockResolvedValue([run()])
  const wrapper = mountView()
  await flushPromises()
  await wrapper.find('#risk-tab-methods').trigger('click')
  await flushPromises()
  expect(getPortfolioRisk).not.toHaveBeenCalled()
  expect(validateRiskCalculation).not.toHaveBeenCalled()
  expect(createRiskCalculation).not.toHaveBeenCalled()
  expect(wrapper.findAll('button').some(b => b.text() === '執行歷史回測')).toBe(true)
  wrapper.unmount()
})

it('reads a linked backtest without creating one and shows only its out-of-sample dates', async () => {
  const evaluation = { status: 'Passed', riskBacktestRunId: 'backtest-1', model: 'VT-GARCH-t + Joint-Vector FHS',
    confidenceLevel: .95, observationCount: 100, kupiecPValue: .35, christoffersenPValue: .32, failureCodes: [], warningCodes: [] }
  vi.mocked(listRiskCalculations).mockResolvedValue([{ ...run(), qualityEvaluation: evaluation } as unknown as RiskCalculationRun])
  vi.mocked(getPortfolioRiskBacktestRun).mockResolvedValue({ id: 'backtest-1', status: 'Completed', from: '2024-01-01', to: '2026-01-01', lookbackDays: 252,
    result: { models: [{ model: evaluation.model, confidenceLevel: .95, observationCount: 100,
      points: [{ date: '2025-01-03' }, { date: '2025-12-31' }] }] } } as never)
  const wrapper = mountView()
  await flushPromises()
  expect(getPortfolioRiskBacktestRun).toHaveBeenCalledTimes(1)
  expect(validateRiskCalculation).not.toHaveBeenCalled()
  expect(wrapper.findComponent({ name: 'RiskQualitySummary' }).text()).toContain('驗證期間 2025-01-03 ～ 2025-12-31')
  expect(wrapper.findComponent({ name: 'RiskQualitySummary' }).text()).not.toContain('2024-01-01')
  await wrapper.find('#risk-tab-methods').trigger('click')
  await flushPromises()
  expect(getPortfolioRiskBacktestRun).toHaveBeenCalledTimes(1)
  wrapper.unmount()
})

it('starts a backtest only after the explicit action and renders a recoverable error', async () => {
  vi.mocked(listRiskCalculations).mockResolvedValue([run()])
  vi.mocked(validateRiskCalculation).mockRejectedValueOnce(new Error('offline'))
  const wrapper = mountView()
  await flushPromises()
  await wrapper.find('#risk-tab-methods').trigger('click')
  await flushPromises()
  await wrapper.findAll('button').find(b => b.text() === '執行歷史回測')!.trigger('click')
  await flushPromises()
  expect(validateRiskCalculation).toHaveBeenCalledTimes(1)
  expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  vi.mocked(validateRiskCalculation).mockResolvedValue({ riskBacktestRunId: 'b' } as never)
  vi.mocked(getPortfolioRiskBacktestRun).mockResolvedValue({ id: 'b', status: 'Completed', result: { models: [] } } as never)
  vi.mocked(getRiskCalculation).mockResolvedValue(run())
  await wrapper.findAll('button').find(b => b.text() === '執行歷史回測')!.trigger('click')
  await flushPromises()
  expect(validateRiskCalculation).toHaveBeenCalledTimes(2)
  expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  wrapper.unmount()
})
