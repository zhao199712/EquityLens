import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import RiskQualitySummary from '../RiskQualitySummary.vue'
import type { RiskQualityEvaluation, PortfolioRiskBacktestRun } from '../../../services/risk'
it('keeps validation scope tied to the evaluated model, not another model or confidence level', () => {
  const wrapper = mount(RiskQualitySummary, { props: { detailed: true,
    evaluation: { status: 'Passed', model: 'GARCH', confidenceLevel: .95, observationCount: 100,
      kupiecPValue: .3546, christoffersenPValue: .3191, warningCodes: ['ES_UNDERESTIMATED'], failureCodes: [] } as unknown as RiskQualityEvaluation,
    backtest: { from: '2020-01-01', to: '2026-10-01', lookbackDays: 252, result: { models: [
      { model: 'Historical', confidenceLevel: .95, points: [{ date: '2020-06-01' }] },
      { model: 'GARCH', confidenceLevel: .99, points: [{ date: '2021-06-01' }] },
      { model: 'GARCH', confidenceLevel: .95, points: [{ date: '2025-01-02' }, { date: '2026-09-30' }], breachCount: 5, breachRate: .05 },
    ] } } as PortfolioRiskBacktestRun,
  } })
  expect(wrapper.text()).toContain('驗證期間 2025-01-02 ～ 2026-09-30')
  expect(wrapper.text()).not.toContain('2020-06-01')
  expect(wrapper.text()).not.toContain('2021-06-01')
  expect(wrapper.text()).toContain('基本檢查')
  expect(wrapper.text()).toContain('不涵蓋多日 ES 聚集檢驗')
  expect(wrapper.text()).toContain('極端損失可能低估')
  expect(wrapper.text()).toContain('0.3191')
})
