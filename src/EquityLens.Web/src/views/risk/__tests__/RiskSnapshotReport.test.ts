import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import RiskSnapshotReport from '../RiskSnapshotReport.vue'
import type { PortfolioRiskReportSnapshotDetail } from '../../../services/risk'
const render = (snapshot: Record<string, unknown>) => mount(RiskSnapshotReport, { props: { report: {
  id: 'saved-1', portfolioId: 'p', createdAtUtc: '2026-11-01T00:00:00Z', dataAsOfDate: '2026-10-30',
  model: 'VT-GARCH-t + Joint-Vector FHS', thresholdVersion: 'v1', overallStatus: 'Passed', snapshot,
} as PortfolioRiskReportSnapshotDetail } })

describe('saved report presentation', () => {
  it('renders saved Python figures, scope and warnings without a JSON dump', () => {
    const wrapper = render({ kind: 'calculation-run', scope: '僅保存本次結果',
      source: { input: { from: '2023-01-01', to: '2026-10-30' } },
      risk: { historicalAnnualizedVolatility: .24, maxDrawdown: -.31,
        horizons: [{ horizonDays: 1, confidenceLevels: [{ confidenceLevel: .95, var: -.02, expectedShortfall: -.03 }] }],
        conditionalRisk: { holdings: [{ ticker: '2330', securityName: '快照持股', weight: .8, componentRiskShare: .9 }] } },
      qualityEvaluation: { status: 'Passed', confidenceLevel: .95, observationCount: 300, kupiecPValue: .3, christoffersenPValue: .4, warningCodes: ['ES_UNDERESTIMATED'] },
    })
    expect(wrapper.text()).toContain('24.00%')
    expect(wrapper.text()).toContain('-31.00%')
    expect(wrapper.text()).toContain('-2.00%')
    expect(wrapper.text()).toContain('-3.00%')
    expect(wrapper.text()).toContain('快照持股')
    expect(wrapper.text()).toContain('極端損失可能低估')
    expect(wrapper.text()).toContain('未保存逐日回測日期')
    expect(wrapper.find('pre').exists()).toBe(false)
  })
  it('supports legacy full reports with PascalCase projected fields', () => {
    const wrapper = render({ risk: { annualizedVolatility: .12, horizons: [{ HorizonDays: 1, MonteCarloVaR: -.01, MonteCarloES: -.02 }] },
      holdings: [{ Ticker: '0050', SecurityName: '舊報告持股', Weight: 1, ComponentRiskShare: -.1 }],
      backtest: [{ Model: 'Historical', ConfidenceLevel: .95, ObservationCount: 200, BreachCount: 12 }],
      stress: [{ Name: '歷史壓力情境', TotalImpact: -.2 }],
    })
    expect(wrapper.text()).toContain('12.00%')
    expect(wrapper.text()).toContain('舊報告持股')
    expect(wrapper.text()).toContain('-10.0%')
    expect(wrapper.text()).toContain('Historical')
    expect(wrapper.text()).toContain('歷史壓力情境')
    expect(wrapper.text()).not.toMatch(/NaN|undefined/)
  })
  it('preserves zero and leaves missing figures unavailable', () => {
    const wrapper = render({ risk: { historicalAnnualizedVolatility: 0 } })
    expect(wrapper.findAll('dd').map(d => d.text())).toEqual(['0.00%', '—', '— 個交易日'])
  })
})
