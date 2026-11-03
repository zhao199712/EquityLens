import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import PortfolioScenarioComparison, { type ScenarioComparison } from '../PortfolioScenarioComparison.vue'
function fixture(): ScenarioComparison {
  return { status: 'Calculated', method: 'fixed weights', inputHash: 'hash', observationCount: 252, confidenceLevel: .95, horizonDays: 1,
    snapshot: { capturedAtUtc: '2026-11-03', from: '2025-11-03', to: '2026-11-02', currency: 'TWD', dates: [] },
    baseline: { name: '原組合', status: 'Calculated', weights: { '2327': .5, '2330': .5 }, quantities: {}, cashWeight: 0, equityHhi: .5, largestEquityWeight: .5,
      metrics: { annualizedVolatility: .4, historicalVaR: -.04, expectedShortfall: -.06, hypotheticalMaxDrawdown: -.3, contributions: [] }, differenceFromBaseline: null },
    scenarios: [{ name: '國巨30%', status: 'Calculated', weights: { '2327': .3, '2330': .5 }, quantities: {}, cashWeight: .2, equityHhi: .34, largestEquityWeight: .5,
      metrics: { annualizedVolatility: .32, historicalVaR: -.032, expectedShortfall: -.048, hypotheticalMaxDrawdown: -.2, contributions: [{ ticker: '2327', weight: .3, componentVolatility: .12, componentRiskShare: .375 }] },
      differenceFromBaseline: { annualizedVolatility: -.08, historicalVaR: .008, expectedShortfall: .012, hypotheticalMaxDrawdown: .1, contributions: [] } }], warnings: ['固定權重假設，不是實際績效'] }
}
describe('PortfolioScenarioComparison', () => {
  it('distinguishes current versus hypothetical weights, cash, units and quality', () => {
    const wrapper = mount(PortfolioScenarioComparison, { props: { comparison: fixture() } })
    expect(wrapper.text()).toContain('國巨30%'); expect(wrapper.text()).toContain('20.00%')
    expect(wrapper.text()).toContain('-8.00 個百分點'); expect(wrapper.text()).toContain('252 筆共同日報酬')
    expect(wrapper.text()).toContain('尚未回測驗證'); expect(wrapper.text()).toContain('假設報酬序列回撤')
  })
  it('does not fill missing scenario metrics with baseline numbers', () => {
    const data = fixture(); data.scenarios[0].metrics = null; data.scenarios[0].differenceFromBaseline = null; data.scenarios[0].status = 'InsufficientHistory'
    const wrapper = mount(PortfolioScenarioComparison, { props: { comparison: data } })
    expect(wrapper.text()).toContain('資料不足：僅顯示配置'); expect(wrapper.text()).not.toContain('32.00%')
  })
  it('shows unsupported cases without an empty comparison table', () => {
    const data = fixture(); data.status = 'Unavailable'; data.baseline = null; data.scenarios = []; data.snapshot = null
    const wrapper = mount(PortfolioScenarioComparison, { props: { comparison: data } })
    expect(wrapper.text()).toContain('無法完成情境比較'); expect(wrapper.find('table').exists()).toBe(false)
  })
})
