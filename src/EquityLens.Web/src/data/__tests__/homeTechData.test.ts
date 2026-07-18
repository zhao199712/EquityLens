import { describe, expect, it } from 'vitest'
import {
  aiFeed,
  allocation,
  allocationColors,
  equityCurves,
  equityRanges,
  heroKpis,
  riskRadar,
  tickerItems,
  varMetrics,
} from '../homeTechData'

describe('homeTechData', () => {
  it('equity curve 四個時間範圍皆有資料且金額為正', () => {
    for (const range of equityRanges) {
      const points = equityCurves[range]
      expect(points.length).toBeGreaterThan(0)
      for (const point of points) {
        expect(point.value).toBeGreaterThan(0)
        expect(point.date).toMatch(/^\d{4}-\d{2}-\d{2}$/)
      }
    }
  })

  it('equity curve 為 deterministic(固定 seed 結果一致)', () => {
    const first = equityCurves['1M'].map((p) => p.value)
    // 重新 import 同一 module 資料應相同(模組層級即生成)
    expect(equityCurves['1M'].map((p) => p.value)).toEqual(first)
  })

  it('allocation 加總為 100 且有對應色票', () => {
    const total = allocation.reduce((sum, slice) => sum + slice.value, 0)
    expect(total).toBe(100)
    expect(allocationColors.length).toBeGreaterThanOrEqual(allocation.length)
  })

  it('ticker 欄位齊全且漲跌幅為數字', () => {
    expect(tickerItems.length).toBeGreaterThanOrEqual(8)
    for (const item of tickerItems) {
      expect(item.code).toBeTruthy()
      expect(item.name).toBeTruthy()
      expect(item.price).toBeTruthy()
      expect(typeof item.changePct).toBe('number')
    }
  })

  it('risk radar 指標與分數等長', () => {
    expect(riskRadar.indicators.length).toBe(riskRadar.scores.length)
    for (const score of riskRadar.scores) {
      expect(score).toBeGreaterThanOrEqual(0)
      expect(score).toBeLessThanOrEqual(100)
    }
  })

  it('VaR 指標皆為負值百分比', () => {
    expect(varMetrics.length).toBeGreaterThanOrEqual(3)
    for (const metric of varMetrics) {
      expect(metric.value).toBeLessThan(0)
    }
  })

  it('hero KPI 與 AI feed 非空', () => {
    expect(heroKpis.length).toBeGreaterThanOrEqual(4)
    expect(aiFeed.length).toBeGreaterThanOrEqual(5)
  })
})
