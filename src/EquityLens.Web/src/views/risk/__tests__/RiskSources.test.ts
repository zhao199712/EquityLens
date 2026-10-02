import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import RiskSources from '../RiskSources.vue'
import { volatilityCard } from '../riskPresentation'
import type { ConditionalRisk, PortfolioRiskResponse, VtGarchRiskResult } from '../../../services/risk'

const sources: ConditionalRisk = {
  methodVersion: 'vt-garch-residual-correlation-v1', horizonDays: 1, annualizationDays: 252,
  dailyVolatility: .01, annualizedVolatility: .16,
  holdings: [{ securityId: 'a', ticker: '2330', securityName: '快照名稱', exchange: 'TWSE', industry: '半導體',
    weight: .2, annualizedVolatility: .25, dataPointCount: 252, componentVolatility: -.032,
    componentRiskShare: -.2, marginalVolatility: -.16, incrementalVolatility: -.025 }],
  industries: [{ industry: '半導體', holdingCount: 1, weight: .2, componentVolatility: -.032,
    componentRiskShare: -.2, marginalVolatility: -.16, incrementalVolatility: -.025 }],
}
const vt = (conditionalRisk?: ConditionalRisk) => ({
  historicalAnnualizedVolatility: .3, conditionalRisk,
}) as VtGarchRiskResult
const fallback = {
  historicalAnnualizedVolatility: .27, riskSourceAnnualizedVolatility: .28,
  holdings: sources.holdings, industries: sources.industries,
} as PortfolioRiskResponse
const render = (conditional?: ConditionalRisk, fb?: PortfolioRiskResponse) => mount(RiskSources, {
  props: { conditional, fallback: fb },
  global: { stubs: { ScrollReveal: { template: '<div><slot /></div>' } } },
})

describe('conditional risk presentation', () => {
  it('uses conditional volatility as the main card while retaining historical context', () => {
    const card = volatilityCard(true, vt(sources), null)
    expect(card.label).toBe('條件波動率（年化）')
    expect(card.value).toBe('16.00%')
    expect(card.sub).toContain('歷史年化波動率 30.00%')
  })
  it('renders Python holdings and industries with signed contributions and percentage points', () => {
    const wrapper = render(sources)
    expect(wrapper.findAll('table')).toHaveLength(2)
    expect(wrapper.text()).toContain('快照名稱')
    expect(wrapper.text()).toContain('16.00%')
    expect(wrapper.text()).toContain('-3.20%')
    expect(wrapper.text()).toContain('-20.0%')
    expect(wrapper.text()).toContain('-0.16 百分點')
    expect(wrapper.text()).toContain('-2.50 百分點')
    expect(wrapper.text()).toContain('原權重比例')
    expect(wrapper.text()).toContain('不是一年期預測')
    expect(wrapper.findAll('.td-pos')).toHaveLength(2)
    expect(wrapper.text()).not.toContain('回退資料')
  })
  it('renders null contributions as dashes and preserves an actual zero volatility', () => {
    const zero: ConditionalRisk = { ...sources, annualizedVolatility: 0,
      holdings: sources.holdings.map(row => ({ ...row, componentVolatility: null, componentRiskShare: null, marginalVolatility: null })),
      industries: sources.industries.map(row => ({ ...row, componentVolatility: null, componentRiskShare: null, marginalVolatility: null })),
    }
    const wrapper = render(zero)
    expect(wrapper.findAll('td').filter(cell => cell.text() === '—')).toHaveLength(6)
    expect(wrapper.text()).not.toMatch(/NaN|null|undefined/)
    expect(volatilityCard(true, vt(zero), null).value).toBe('0.00%')
  })
  it('tells users to recalculate legacy Python results without relabelling history', () => {
    const card = volatilityCard(true, vt(), null)
    expect(card.value).toBe('—')
    expect(card.sub).toContain('未提供條件波動率，需重新計算')
    expect(card.sub).toContain('30.00%')
    expect(render().findAll('table')).toHaveLength(0)
  })
  it('keeps C# fallback history and decomposition labelled by model', () => {
    const card = volatilityCard(false, null, fallback)
    expect(card.label).toBe('歷史年化波動率')
    expect(card.value).toBe('27.00%')
    const wrapper = render(undefined, fallback)
    expect(wrapper.findAll('table')).toHaveLength(2)
    expect(wrapper.text()).toContain('C# MVEWMA-FHS 回退資料')
    expect(wrapper.text()).toContain('28.00%')
    expect(wrapper.text()).toContain('每個持倉各增加')
  })
  it('does not invent named rows for legacy worker inputs without metadata', () => {
    expect(render({ ...sources, holdings: [], industries: [] }).findAll('table')).toHaveLength(0)
  })
})
