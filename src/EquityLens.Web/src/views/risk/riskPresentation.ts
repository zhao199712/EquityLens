import type { PortfolioRiskResponse, VtGarchRiskResult } from '../../services/risk'

export function riskPercent(value: number | null | undefined, digits = 2): string {
  return value == null ? '—' : `${(value * 100).toFixed(digits)}%`
}

export function volatilityCard(
  isVtGarch: boolean, vt: VtGarchRiskResult | null, fallback: PortfolioRiskResponse | null,
) {
  if (isVtGarch) {
    return {
      label: '條件波動率（年化）',
      value: riskPercent(vt?.conditionalRisk?.annualizedVolatility),
      sub: `${vt?.conditionalRisk ? '下一交易日預測' : '未提供條件波動率，需重新計算'}；歷史年化波動率 ${riskPercent(vt?.historicalAnnualizedVolatility)}`,
    }
  }
  return {
    label: '歷史年化波動率',
    value: riskPercent(fallback?.historicalAnnualizedVolatility),
    sub: 'C# MVEWMA-FHS 回退資料',
  }
}
