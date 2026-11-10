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

export function qualityStatusLabel(status?: string | null): string {
  return ({ Passed: '本次回測通過基本檢查', Failed: '本次回測未通過基本檢查',
    Pending: '歷史回測處理中', InsufficientData: '回測資料不足', Error: '歷史回測執行失敗',
    Unvalidated: '尚未回測', ready: '資料正常', warning: '資料需注意', critical: '資料需處理' } as Record<string, string>)[status ?? 'Unvalidated'] ?? '驗證狀態未提供'
}

export function qualityCodeLabel(code: string): string {
  return ({ KUPIEC_FAILED: '超損頻率與預期不符。', CHRISTOFFERSEN_FAILED: '超損時序未通過獨立性檢查，可能存在聚集。',
    FIT_UNHEALTHY: '模型擬合未通過健康檢查。', ES_UNDERESTIMATED: '極端損失可能低估，請留意 ES 警示。',
    INSUFFICIENT_OBSERVATIONS: '樣本數或可用檢驗結果不足，無法判定。', BACKTEST_FAILED: '回測計算失敗，請重試。',
    BACKTEST_QUEUE_FAILED: '回測工作未成功排入，請重試。', MODEL_RESULT_MISSING: '缺少本次模型的回測結果。' } as Record<string, string>)[code] ?? `其他驗證提醒：${code}`
}
