import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import RiskMeasurementBasis from '../RiskMeasurementBasis.vue'

describe('risk measurement basis', () => {
  it('keeps absent provenance unknown rather than assuming matching methods', () => {
    expect(mount(RiskMeasurementBasis).text()).toContain('不能直接與其他頁面或情境數字比較')
  })
  it('displays period, construction, cash and samples together', () => {
    const text = mount(RiskMeasurementBasis, { props: { basis: {
      from: '2025-01-01', to: '2025-12-31', weighting: '固定股數', cashTreatment: '不含現金',
      returnMethod: '簡單報酬', volatilityMethod: '樣本標準差', observationCount: 120,
      sampleDefinition: '共同交易日', confidenceLevel: .95, horizonDays: 1,
    } } }).text()
    for (const expected of ['2025-01-01', '固定股數', '不含現金', '樣本標準差', '120', '95%', '1 日']) expect(text).toContain(expected)
  })
})
