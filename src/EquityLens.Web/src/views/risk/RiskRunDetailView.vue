<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, computed, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import Footer from '../../components/kimi/Footer.vue'
import {
  getPortfolio,
  getPortfolioRisk,
  getPortfolioRiskBacktest,
  getPortfolioMonteCarlo,
  getPortfolioRiskGovernance,
  calculatePortfolioRiskScenario,
  createPortfolioRiskReportSnapshot,
  getPortfolioRiskReportSnapshot,
  getPortfolioRiskReportSnapshots,
  getPortfolioStressTest,
  type PortfolioRiskGovernanceResponse,
  type PortfolioRiskScenarioResponse,
  type PortfolioRiskReportSnapshotDetail,
  type PortfolioRiskReportSnapshotListItem,
  type PortfolioMonteCarloResponse,
  type PortfolioRiskBacktestResponse,
  type PortfolioRiskResponse,
} from '../../services/risk.ts'

const router = useRouter()
const route = useRoute()
const portfolioId = computed(() => String(route.params.id))

const portfolioName = ref('投資組合風險分析')
const risk = ref<PortfolioRiskResponse | null>(null)
const loading = ref(false)
const risk99 = ref<PortfolioRiskResponse | null>(null)
const riskConservative = ref<PortfolioRiskResponse | null>(null)
const riskConservative99 = ref<PortfolioRiskResponse | null>(null)
const riskEwma = ref<PortfolioRiskResponse | null>(null)
const riskEwma99 = ref<PortfolioRiskResponse | null>(null)
const riskCurve = ref<PortfolioRiskResponse[]>([])
const backtest = ref<PortfolioRiskBacktestResponse | null>(null)
const monteCarloBase = ref<PortfolioMonteCarloResponse | null>(null)
const monteCarloConservative = ref<PortfolioMonteCarloResponse | null>(null)
const governance = ref<PortfolioRiskGovernanceResponse | null>(null)
const targetWeights = ref<Record<string, number>>({})
const scenarioResult = ref<PortfolioRiskScenarioResponse | null>(null)
const scenarioLoading = ref(false)
const scenarioMessage = ref('')
const reportSnapshots = ref<PortfolioRiskReportSnapshotListItem[]>([])
const selectedReportSnapshot = ref<PortfolioRiskReportSnapshotDetail | null>(null)
const reportActionMessage = ref('')
const reportCreating = ref(false)
const selectedMonteCarloModel = ref<'base' | 'conservative'>('base')
const monteCarlo = computed(() => selectedMonteCarloModel.value === 'conservative' ? monteCarloConservative.value : monteCarloBase.value)
const stressTest = ref<Awaited<ReturnType<typeof getPortfolioStressTest>> | null>(null)
const selectedBacktestModel = ref<'Historical' | 'MVEWMA-FHS' | 'MVEWMA-FHS（保守 p99）'>('Historical')
const selectedBacktestConfidence = ref<0.95 | 0.99>(0.95)
const backtestModels: Array<'Historical' | 'MVEWMA-FHS' | 'MVEWMA-FHS（保守 p99）'> = ['Historical', 'MVEWMA-FHS', 'MVEWMA-FHS（保守 p99）']
const backtestConfidences: Array<0.95 | 0.99> = [0.95, 0.99]
const error = ref('')
type DeferredLoadState = 'idle' | 'loading' | 'ready' | 'error'
const modelComparisonState = ref<DeferredLoadState>('idle')
const stressTestState = ref<DeferredLoadState>('idle')
const backtestState = ref<DeferredLoadState>('idle')
const monteCarloState = ref<DeferredLoadState>('idle')
const modelComparisonError = ref('')
const stressTestError = ref('')
const backtestError = ref('')
const monteCarloError = ref('')
const modelComparisonSentinel = ref<HTMLElement | null>(null)
const stressTestSentinel = ref<HTMLElement | null>(null)
const backtestSentinel = ref<HTMLElement | null>(null)
const monteCarloSentinel = ref<HTMLElement | null>(null)
let sectionObserver: IntersectionObserver | null = null
let viewIsActive = true
const governanceAlerts = computed(() => (governance.value?.alerts ?? []).filter(alert => alert.status !== 'normal').sort((a, b) => (a.status === 'critical' ? -1 : 1) - (b.status === 'critical' ? -1 : 1)))
const governanceLabel = (code: string) => ({
  'concentration.largest_holding': '最大單一持倉',
  'concentration.hhi': '持倉集中度 HHI',
  'data.price_age_days': '價格資料延遲',
  'leverage.financing': '融資槓桿',
}[code] ?? code)
const scenarioAlertValue = (alert: { code: string; currentValue: number }) => alert.code === 'data.price_age_days' ? `${alert.currentValue} 日` : `${(alert.currentValue * 100).toFixed(1)}%`
const governanceDataStatus = (status: string) => ({ ready: '資料正常', warning: '資料需注意', critical: '資料嚴重不足' }[status] ?? status)
const targetWeightTotal = computed(() => Object.values(targetWeights.value).reduce((sum, weight) => sum + (Number(weight) || 0), 0))
const targetCashWeight = computed(() => 1 - targetWeightTotal.value / 100)
function resetTargetWeights() {
  if (!risk.value) return
  targetWeights.value = Object.fromEntries(risk.value.holdings.map(holding => [holding.securityId, Number((holding.weight * 100).toFixed(2))]))
  scenarioResult.value = null
  scenarioMessage.value = ''
}
async function runTargetWeightScenario() {
  if (!risk.value) return
  scenarioLoading.value = true; scenarioMessage.value = ''
  try {
    scenarioResult.value = await calculatePortfolioRiskScenario(portfolioId.value, risk.value.holdings.map(h => ({ securityId: h.securityId, targetWeight: (Number(targetWeights.value[h.securityId]) || 0) / 100 })))
  } catch (e) { scenarioMessage.value = riskErrorMessage(e) } finally { scenarioLoading.value = false }
}
const reportStatusLabel = (status: string) => ({ ready: '資料正常', warning: '資料需注意', critical: '資料嚴重不足' }[status] ?? status)
const reportCreatedAt = (value: string) => new Date(value).toLocaleString('zh-TW', { hour12: false })
async function createReportSnapshot() { reportCreating.value = true; reportActionMessage.value = ''; try { const report = await createPortfolioRiskReportSnapshot(portfolioId.value); selectedReportSnapshot.value = report; reportSnapshots.value = await getPortfolioRiskReportSnapshots(portfolioId.value); reportActionMessage.value = '風險報告快照已建立；內容已固定，不會隨後續價格更新而改變。' } catch (e) { reportActionMessage.value = riskErrorMessage(e) } finally { reportCreating.value = false } }
async function openReportSnapshot(reportId: string) { try { selectedReportSnapshot.value = await getPortfolioRiskReportSnapshot(portfolioId.value, reportId) } catch (e) { reportActionMessage.value = riskErrorMessage(e) } }
function printReportSnapshot() { window.print() }

const selectedScenario = ref<string | null>(null)
const formalStressScenarios = computed(() => (stressTest.value?.scenarios ?? []).slice().sort((a,b)=>a.totalImpact-b.totalImpact))
const selectedStressScenario = computed(() => formalStressScenarios.value.find(s => s.id === selectedScenario.value) ?? null)

const today = new Date()
const oneYearAgo = new Date(today.getFullYear() - 1, today.getMonth(), today.getDate())
const fromDate = computed(() => oneYearAgo.toISOString().split('T')[0])
const toDate = computed(() => today.toISOString().split('T')[0])
const backtestFromDate = computed(() => new Date(today.getFullYear() - 3, today.getMonth(), today.getDate()).toISOString().split('T')[0])

const backtestModel = computed(() => backtest.value?.models.find(model =>
  model.model === selectedBacktestModel.value && model.confidenceLevel === selectedBacktestConfidence.value,
) ?? null)
const backtestData = computed(() => {
  const points = backtestModel.value?.points.slice(-60) ?? []
  const step = 420 / Math.max(points.length - 1, 1)
  return points.map((point, index) => ({
    date: point.date,
    actual: point.actualReturn * 100,
    var95: point.predictedVaR * 100,
    breached: point.breached,
    x: 55 + index * step,
  }))
})
const backtestVaRLine = computed(() => {
  const points = backtestData.value
  return points.length ? points.reduce((sum, point) => sum + point.var95, 0) / points.length : 0
})
const backtestChart = computed(() => {
  const values = [0, ...backtestData.value.flatMap(point => [point.actual, point.var95])]
  const rawMin = Math.min(...values)
  const rawMax = Math.max(...values)
  const step = Math.max(5, Math.ceil(Math.max(10, rawMax - rawMin) / 5 / 5) * 5)
  const min = Math.floor(rawMin / step) * step
  const max = Math.max(min + step * 5, Math.ceil(rawMax / step) * step)
  const ticks: number[] = []
  for (let tick = max; tick >= min; tick -= step) ticks.push(tick)
  const y = (value: number) => 20 + ((max - value) / (max - min)) * 200
  return { ticks, y }
})
const backtestStats = computed(() => {
  const model = backtestModel.value
  if (!model) return []
  const p = (value: number | null) => value == null ? '資料不足' : `p=${value.toFixed(2)}`
  const pColor = (value: number | null) => value == null ? '#9a917c' : value < 0.05 ? '#b05c5c' : value < 0.10 ? '#d4a24e' : '#7fa387'
  const esColor = model.esTailLossRatio == null ? '#9a917c' : model.esTailLossRatio > 1.10 ? '#b05c5c' : model.esTailLossRatio < 0.90 ? '#d4a24e' : '#7fa387'
  const esValue = model.esTailLossRatio == null ? '資料不足' : `${model.esTailLossRatio.toFixed(2)}×`
  return [
    { label: '觀察期間', value: `${model.observationCount} 交易日`, color: '#f5efe0' }, { label: '例外次數', value: `${model.breachCount} 次`, color: '#f5efe0' },
    { label: '例外比率', value: `${(model.breachRate * 100).toFixed(1)}%`, color: '#f5efe0' }, { label: '預期比率', value: `${(model.expectedBreachRate * 100).toFixed(1)}%`, color: '#f5efe0' },
    { label: 'Kupiec 檢定', value: p(model.kupiecPValue), color: pColor(model.kupiecPValue) }, { label: 'Christoffersen', value: p(model.christoffersenPValue), color: pColor(model.christoffersenPValue) },
    { label: 'ES 尾端樣本', value: `${model.tailObservationCount} 日`, color: '#f5efe0' }, { label: 'ES 尾端損失比', value: esValue, color: esColor },
  ]
})

const runInfo = computed(() => {
  const r = risk.value
  const modelName =
    r?.covarianceMethod === 'MultivariateEWMA'
      ? 'MVEWMA-FHS'
      : 'Historical VaR + Monte Carlo'
  return {
    id: `RR-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}-001`,
    portfolio: portfolioName.value,
    model: modelName,
    confidence: r ? `${(r.confidenceLevel * 100).toFixed(0)}%` : '95%',
    // 價格日數扣除第一日後，才是可用於計算的日報酬筆數。
    lookback: r ? `${r.alignedReturnCount} 個日報酬` : '—',
    date: new Date().toISOString().split('T')[0],
    duration: '—',
  }
})

const metrics = computed(() => {
  const r = risk.value
  if (!r) return []
  const h1 = r.horizons.find((h) => h.horizonDays === 1)
  return [
    {
      label: 'VaR 95% (1日)',
      value: h1 ? `${(h1.historicalVaR * 100).toFixed(2)}%` : '—',
      sub: 'Historical Simulation',
    },
    {
      label: 'ES 95% (1日)',
      value: h1 ? `${(h1.historicalES * 100).toFixed(2)}%` : '—',
      sub: 'Expected Shortfall',
    },
    {
      label: '年化波動率',
      value: `${(r.historicalAnnualizedVolatility * 100).toFixed(2)}%`,
      sub: r.volatilityMethod,
    },
    {
      label: '最大回撤',
      value: `${(r.maxDrawdown * 100).toFixed(2)}%`,
      sub: 'Historical',
    },
  ]
})

const riskKPIData = computed(() => {
  const r = risk.value
  const h1 = r?.horizons.find((h) => h.horizonDays === 1)
  const total = r?.totalMarketValue ?? 0
  const varAmount = h1 ? total * Number(h1.historicalVaR) : 0
  const esAmount = h1 ? total * Number(h1.historicalES) : 0
  return [
    {
      label: 'DAILY VaR (95%)',
      value: h1 ? `${(h1.historicalVaR * 100).toFixed(2)}%` : '—',
      sub: varAmount ? `${formatMoney(varAmount)} ${r?.baseCurrency ?? ''}` : '—',
      color: '#b05c5c',
    },
    {
      label: 'DAILY ES (95%)',
      value: h1 ? `${(h1.historicalES * 100).toFixed(2)}%` : '—',
      sub: esAmount ? `預期損失 ${formatMoney(esAmount)}` : '—',
      color: '#b05c5c',
    },
    {
      label: 'STRESS VaR',
      value: '-18.52%',
      sub: 'AI泡沫情境（示範資料）',
      color: '#b05c5c',
    },
    {
      label: 'MAX DRAWDOWN',
      value: r ? `${(r.maxDrawdown * 100).toFixed(2)}%` : '—',
      sub: '歷史最大回撤',
      color: '#b05c5c',
    },
    {
      label: 'SHARPE RATIO',
      value: r ? r.sharpeRatio.toFixed(2) : '—',
      sub: '風險調整後報酬',
      color: '#7fa387',
    },
    {
      label: 'CONCENTRATION HHI',
      value: r ? r.concentrationHhi.toFixed(4) : '—',
      sub: '持倉集中度',
      color: '#c9a86a',
    },
    {
      label: 'LARGEST HOLDING',
      value: r ? `${(r.largestHoldingWeight * 100).toFixed(2)}%` : '—',
      sub: '最大單一持倉',
      color: '#c9a86a',
    },
  ]
})

const varTableRows = computed(() => {
  const h95 = risk.value?.horizons.find((h) => h.horizonDays === 1)
  const h99 = risk99.value?.horizons.find((h) => h.horizonDays === 1)
  const conservative95 = riskConservative.value?.horizons.find((h) => h.horizonDays === 1)
  const conservative99 = riskConservative99.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma95 = riskEwma.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma99 = riskEwma99.value?.horizons.find((h) => h.horizonDays === 1)
  return [
    { method: '歷史模擬法', var95: h95?.historicalVaR ?? 0, var99: h99?.historicalVaR ?? 0, note: '1日' },
    { method: 'MVEWMA-FHS', var95: h95?.monteCarloVaR ?? 0, var99: h99?.monteCarloVaR ?? 0, note: '1日' },
    { method: 'MVEWMA-FHS（保守 p99）', var95: conservative95?.monteCarloVaR ?? 0, var99: conservative99?.monteCarloVaR ?? 0, note: '比較模型' },
    { method: 'EWMA 常態蒙地卡羅', var95: ewma95?.monteCarloVaR ?? 0, var99: ewma99?.monteCarloVaR ?? 0, note: 'λ=0.94' },
  ]
})

const esTableRows = computed(() => {
  const h95 = risk.value?.horizons.find((h) => h.horizonDays === 1)
  const h99 = risk99.value?.horizons.find((h) => h.horizonDays === 1)
  const conservative95 = riskConservative.value?.horizons.find((h) => h.horizonDays === 1)
  const conservative99 = riskConservative99.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma95 = riskEwma.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma99 = riskEwma99.value?.horizons.find((h) => h.horizonDays === 1)
  return [
    { method: '歷史模擬法', es95: h95?.historicalES ?? 0, es99: h99?.historicalES ?? 0 },
    { method: 'MVEWMA-FHS', es95: h95?.monteCarloES ?? 0, es99: h99?.monteCarloES ?? 0 },
    { method: 'MVEWMA-FHS（保守 p99）', es95: conservative95?.monteCarloES ?? 0, es99: conservative99?.monteCarloES ?? 0 },
    { method: 'EWMA 常態蒙地卡羅', es95: ewma95?.monteCarloES ?? 0, es99: ewma99?.monteCarloES ?? 0 },
  ]
})

const officialHistogram = computed(() => {
  const returns = (risk.value?.dailyLogReturns ?? []).map(value => (Math.exp(value) - 1) * 100)
  const bounds = [-Infinity, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, Infinity]
  const labels = ['<-6%', '-6~-5%', '-5~-4%', '-4~-3%', '-3~-2%', '-2~-1%', '-1~0%', '0~1%', '1~2%', '2~3%', '3~4%', '4~5%', '5~6%', '>6%']
  const var95 = (risk.value?.horizons.find(h => h.horizonDays === 1)?.historicalVaR ?? 0) * 100
  const var99 = (risk99.value?.horizons.find(h => h.horizonDays === 1)?.historicalVaR ?? 0) * 100
  return labels.map((bin, index) => ({ bin, count: returns.filter(value => value >= bounds[index] && value < bounds[index + 1]).length, isTail95: bounds[index + 1] <= var95, isTail99: bounds[index + 1] <= var99 }))
})
const histogramChart = computed(() => {
  const max = Math.max(10, Math.ceil(Math.max(...officialHistogram.value.map(bin => bin.count), 0) / 10) * 10)
  const ticks = Array.from({ length: 6 }, (_, index) => max - (max / 5) * index)
  const y = (value: number) => 30 + (1 - value / max) * 180
  return { ticks, y, height: (value: number) => value / max * 180 }
})
const officialVarEsComparison = computed(() => riskCurve.value.map(item => {
  const horizon = item.horizons.find(h => h.horizonDays === 1)
  return { confidence: `${(item.confidenceLevel * 100).toFixed(item.confidenceLevel % 0.01 === 0 ? 0 : 1)}%`, var: (horizon?.monteCarloVaR ?? 0) * 100, es: (horizon?.monteCarloES ?? 0) * 100 }
}))
const varEsChart = computed(() => {
  const maximumLoss = Math.max(...officialVarEsComparison.value.flatMap(point => [-point.var, -point.es]), 1)
  // Leave one extra tick below the largest loss so the 99%+ ES bar never
  // touches the chart boundary.
  const max = Math.ceil((maximumLoss + 2) / 2) * 2
  const ticks = Array.from({ length: 6 }, (_, index) => -(max / 5) * index)
  const y = (value: number) => 30 + (-value / max) * 200
  const height = (value: number) => -value / max * 200
  return { ticks, y, height }
})

const monteCarloChart = computed(() => {
  const data = monteCarlo.value
  const bands = data?.bands ?? []
  const horizon = Math.max(data?.horizonDays ?? 252, 1)
  const values = bands.flatMap(point => [point.p1, point.p5, point.p50, point.p95, point.p99])
  const rawMin = Math.min(0, ...values)
  const rawMax = Math.max(0, ...values)
  const span = Math.max(rawMax - rawMin, 0.04)
  const pad = span * 0.10
  const min = rawMin - pad
  const max = rawMax + pad
  const x = (day: number) => 70 + (day / horizon) * 780
  const y = (value: number) => 30 + ((max - value) / (max - min)) * 300
  const polyline = (selector: (point: NonNullable<typeof bands>[number]) => number) => bands.map(point => `${x(point.day)},${y(selector(point))}`).join(' ')
  const band = (upper: (point: NonNullable<typeof bands>[number]) => number, lower: (point: NonNullable<typeof bands>[number]) => number) =>
    `${polyline(upper)} ${bands.slice().reverse().map(point => `${x(point.day)},${y(lower(point))}`).join(' ')}`
  // Always include 0% so the chart has an explicit gain/loss reference line.
  const ticks = Array.from(new Set([
    ...Array.from({ length: 6 }, (_, index) => max - (max - min) * index / 5),
    0,
  ])).sort((left, right) => right - left)
  return { bands, x, y, ticks, p1: band(point => point.p99, point => point.p1), p5: band(point => point.p95, point => point.p5), p50: polyline(point => point.p50), horizon }
})
const monteCarloStats = computed(() => {
  const data = monteCarlo.value
  if (!data || data.status !== 'ready') return []
  const percent = (value: number) => `${value >= 0 ? '+' : ''}${(value * 100).toFixed(1)}%`
  const medianFinalReturn = data.bands.at(-1)?.p50 ?? 0
  return [
    { label: '一年正報酬機率', value: `${(data.positiveReturnProbability * 100).toFixed(1)}%`, color: '#7fa387' },
    { label: '期望報酬', value: percent(data.expectedReturn), color: data.expectedReturn >= 0 ? '#7fa387' : '#b05c5c' },
    { label: '中位數期末情境', value: percent(medianFinalReturn), color: medianFinalReturn >= 0 ? '#7fa387' : '#b05c5c' },
    { label: '5% 最差期末情境', value: percent(data.p5FinalReturn), color: '#b05c5c' },
    { label: '1% 最差期末情境', value: percent(data.p1FinalReturn), color: '#b05c5c' },
    { label: '共同日資料', value: `${data.commonTradingDays} 日`, color: '#f5efe0' },
  ]
})
const monteCarloDiagnostics = computed(() => {
  const diagnostics = monteCarlo.value?.diagnostics
  if (!diagnostics) return []
  const percent = (value: number) => `${value >= 0 ? '+' : ''}${(value * 100).toFixed(1)}%`
  return [
    { label: '年化投組波動率', value: percent(diagnostics.annualizedPortfolioVolatility) },
    { label: '期末 p95', value: percent(diagnostics.p95FinalReturn) },
    { label: '期末 p99', value: percent(diagnostics.p99FinalReturn) },
    { label: '殘差向量 p99', value: diagnostics.residualNormP99.toFixed(2) },
    { label: '殘差向量最大值', value: diagnostics.maxResidualNorm.toFixed(2) },
    { label: '期望－中位數', value: percent(diagnostics.expectedMedianGap) },
  ]
})
const monteCarloComparison = computed(() => [
  { label: '原始 FHS', data: monteCarloBase.value },
  { label: '保守 FHS（p99）', data: monteCarloConservative.value },
].filter((item): item is { label: string; data: PortfolioMonteCarloResponse } => item.data?.status === 'ready').map(item => ({
  label: item.label,
  positive: `${(item.data.positiveReturnProbability * 100).toFixed(1)}%`,
  median: `${item.data.diagnostics.p50FinalReturn >= 0 ? '+' : ''}${(item.data.diagnostics.p50FinalReturn * 100).toFixed(1)}%`,
  expected: `${item.data.expectedReturn >= 0 ? '+' : ''}${(item.data.expectedReturn * 100).toFixed(1)}%`,
  p5: `${(item.data.p5FinalReturn * 100).toFixed(1)}%`,
  p1: `${(item.data.p1FinalReturn * 100).toFixed(1)}%`,
  capped: item.data.residualCapQuantile > 0 ? `${(item.data.cappedDrawRate * 100).toFixed(2)}%` : '未截尾',
})))

function formatMoney(n: number) {
  if (n === 0) return '0'
  const abs = Math.abs(n)
  const sign = n < 0 ? '-' : ''
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(2)}M`
  if (abs >= 1_000) return `${sign}${(abs / 1_000).toFixed(2)}K`
  return `${sign}${abs.toFixed(2)}`
}

function riskErrorMessage(e: unknown) {
  const data = (e as { response?: { data?: { code?: string; message?: string } } }).response?.data
  switch (data?.code) {
    case 'risk.insufficient_prices': return '可用的共同日價格歷史不足 120 筆（約半年）；請延長區間或先同步價格資料。'
    case 'portfolio.no_holdings': return '此投資組合尚無持倉，無法進行風險分析。'
    case 'risk.invalid_market_value': return '投資組合沒有有效的正市值，無法進行風險分析。'
    case 'risk.non_positive_price': return '歷史價格含有非正值，請修正價格資料後再試。'
    default: return data?.message || '無法載入風險分析資料，請稍後再試。'
  }
}

async function loadModelComparison() {
  if (modelComparisonState.value === 'loading' || modelComparisonState.value === 'ready') return
  modelComparisonState.value = 'loading'; modelComparisonError.value = ''
  try {
    const [riskData99, conservativeData, conservativeData99, ewmaData, ewmaData99, ...curve] = await Promise.all([
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.99, simulations: 10000, model: 'mvewma_fhs' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.95, simulations: 10000, model: 'mvewma_fhs_conservative' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.99, simulations: 10000, model: 'mvewma_fhs_conservative' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.95, simulations: 10000, model: 'gbm_ewma_normal' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.99, simulations: 10000, model: 'gbm_ewma_normal' }),
      ...[0.90, 0.95, 0.975, 0.99, 0.995].map(confidenceLevel => getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 1, confidenceLevel, simulations: 5000, model: 'mvewma_fhs' })),
    ])
    if (!viewIsActive) return
    risk99.value = riskData99; riskConservative.value = conservativeData; riskConservative99.value = conservativeData99
    riskEwma.value = ewmaData; riskEwma99.value = ewmaData99; riskCurve.value = curve
    modelComparisonState.value = 'ready'
  } catch (e) { if (viewIsActive) { modelComparisonError.value = riskErrorMessage(e); modelComparisonState.value = 'error' } }
}

async function loadStressTest() {
  if (stressTestState.value === 'loading' || stressTestState.value === 'ready') return
  stressTestState.value = 'loading'; stressTestError.value = ''
  try { const data = await getPortfolioStressTest(portfolioId.value); if (viewIsActive) { stressTest.value = data; stressTestState.value = 'ready' } }
  catch (e) { if (viewIsActive) { stressTestError.value = riskErrorMessage(e); stressTestState.value = 'error' } }
}

async function loadBacktest() {
  if (backtestState.value === 'loading' || backtestState.value === 'ready') return
  backtestState.value = 'loading'; backtestError.value = ''
  try { const data = await getPortfolioRiskBacktest(portfolioId.value, backtestFromDate.value, toDate.value); if (viewIsActive) { backtest.value = data; backtestState.value = 'ready' } }
  catch (e) { if (viewIsActive) { backtestError.value = riskErrorMessage(e); backtestState.value = 'error' } }
}

async function loadMonteCarlo() {
  if (monteCarloState.value === 'loading' || monteCarloState.value === 'ready') return
  monteCarloState.value = 'loading'; monteCarloError.value = ''
  try {
    const [base, conservative] = await Promise.all([getPortfolioMonteCarlo(portfolioId.value), getPortfolioMonteCarlo(portfolioId.value, 'mvewma_fhs_conservative')])
    if (viewIsActive) { monteCarloBase.value = base; monteCarloConservative.value = conservative; monteCarloState.value = 'ready' }
  } catch (e) { if (viewIsActive) { monteCarloError.value = riskErrorMessage(e); monteCarloState.value = 'error' } }
}

function observeDeferredSections() {
  if (typeof IntersectionObserver === 'undefined') {
    void loadModelComparison(); void loadStressTest(); void loadBacktest(); void loadMonteCarlo()
    return
  }
  const loaders = new Map<Element, () => Promise<void>>([
    ...(modelComparisonSentinel.value ? [[modelComparisonSentinel.value, loadModelComparison] as const] : []),
    ...(stressTestSentinel.value ? [[stressTestSentinel.value, loadStressTest] as const] : []),
    ...(backtestSentinel.value ? [[backtestSentinel.value, loadBacktest] as const] : []),
    ...(monteCarloSentinel.value ? [[monteCarloSentinel.value, loadMonteCarlo] as const] : []),
  ])
  sectionObserver = new IntersectionObserver(entries => {
    for (const entry of entries) {
      const loader = loaders.get(entry.target)
      if (entry.isIntersecting && loader) { sectionObserver?.unobserve(entry.target); void loader() }
    }
  }, { rootMargin: '300px 0px' })
  loaders.forEach((_, target) => sectionObserver?.observe(target))
}

onMounted(async () => {
  loading.value = true
  try {
    const [portfolio, riskData, governanceData] = await Promise.all([
      getPortfolio(portfolioId.value),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.95, simulations: 10000, model: 'mvewma_fhs' }),
      getPortfolioRiskGovernance(portfolioId.value),
    ])
    portfolioName.value = portfolio.name
    risk.value = riskData
    governance.value = governanceData
    targetWeights.value = Object.fromEntries(riskData.holdings.map(holding => [holding.securityId, Number((holding.weight * 100).toFixed(2))]))
    try {
      reportSnapshots.value = await getPortfolioRiskReportSnapshots(portfolioId.value)
    } catch {
      reportActionMessage.value = '風險報告快照資料表尚未完成部署；請套用資料庫 migration 後再使用快照功能。'
    }
  } catch (e) {
    error.value = riskErrorMessage(e)
  } finally {
    loading.value = false
    await nextTick()
    if (viewIsActive && risk.value) observeDeferredSections()
  }
})

onBeforeUnmount(() => { viewIsActive = false; sectionObserver?.disconnect() })
</script>

<template>
  <div class="prestige-page">
    <div class="prestige-section">
      <button class="prestige-btn back-btn" @click="router.push({ name: 'risk-runs' })">
        ← BACK TO RISK RUNS
      </button>

      <div class="page-head">
        <div>
          <span class="prestige-label">Risk Analysis Run — <span class="prestige-mono">{{ runInfo.id }}</span></span>
          <h1 class="page-title">{{ runInfo.portfolio }}</h1>
        </div>
        <span class="prestige-tag tag-completed"><span class="dot" />COMPLETED</span>
      </div>

      <!-- Loading / Error -->
      <ScrollReveal v-if="loading">
        <div class="prestige-skeleton" style="min-height: 280px; display: flex; align-items: center; justify-content: center; color: var(--muted)">
          載入風險分析資料中...
        </div>
      </ScrollReveal>

      <ScrollReveal v-if="error && !loading">
        <div class="prestige-error" style="text-align: center">{{ error }}</div>
      </ScrollReveal>

      <template v-if="!loading && risk">
        <!-- Risk Governance -->
        <ScrollReveal v-if="governance" style="margin-bottom: 24px">
          <div class="prestige-panel prestige-panel-pad">
            <div class="panel-head">
              <div>
                <h2 class="panel-title">風險治理摘要</h2>
                <span class="prestige-label">Risk Governance · {{ governanceDataStatus(governance.dataStatus) }} · {{ governance.commonTradingDays }} 個共同交易日</span>
              </div>
              <span
                class="prestige-tag"
                :class="governanceAlerts.some(a => a.status === 'critical') ? 'tag-critical' : governanceAlerts.length ? 'tag-warning' : 'tag-normal'"
              >
                <span class="dot" :class="{ pulse: governanceAlerts.some(a => a.status === 'critical') }" />
                {{ governanceAlerts.some(a => a.status === 'critical') ? '需處理' : governanceAlerts.length ? '需注意' : '無門檻警示' }}
              </span>
            </div>

            <div v-if="governanceAlerts.length" class="alert-list">
              <div
                v-for="alert in governanceAlerts"
                :key="alert.code"
                class="alert-item"
                :class="alert.status === 'critical' ? 'critical' : 'warning'"
              >
                <div class="alert-line">
                  <strong class="alert-name">{{ governanceLabel(alert.code) }}</strong>
                  <span class="alert-thresholds prestige-mono">目前 {{ alert.code.includes('age') ? `${alert.currentValue} 日` : `${(alert.currentValue * 100).toFixed(1)}%` }} · 注意 {{ alert.code.includes('age') ? `${alert.warningThreshold} 日` : `${(alert.warningThreshold * 100).toFixed(1)}%` }} · 嚴重 {{ alert.code.includes('age') ? `${alert.criticalThreshold} 日` : `${(alert.criticalThreshold * 100).toFixed(1)}%` }}</span>
                </div>
                <div class="alert-msg">{{ alert.message }}</div>
              </div>
            </div>
            <p v-else class="muted-text">目前沒有觸發已設定的集中度與資料品質門檻；這不代表投組沒有風險。</p>

            <!-- Report snapshots -->
            <div class="sub-block">
              <div class="btn-row">
                <button class="prestige-btn" :disabled="reportCreating" @click="createReportSnapshot">{{ reportCreating ? '建立中…' : '建立風險報告快照' }}</button>
                <span v-if="reportActionMessage" class="muted-text snap-msg">{{ reportActionMessage }}</span>
              </div>
              <div v-if="reportSnapshots.length" class="snap-list">
                <span class="prestige-label">歷史快照（最新在前）</span>
                <button
                  v-for="report in reportSnapshots"
                  :key="report.id"
                  class="prestige-btn snap-btn"
                  @click="openReportSnapshot(report.id)"
                >
                  {{ reportCreatedAt(report.createdAtUtc) }} · {{ report.model }} · {{ reportStatusLabel(report.overallStatus) }}
                </button>
              </div>
            </div>

            <!-- What-if scenario -->
            <div class="sub-block">
              <h3 class="sub-title">調整試算</h3>
              <p class="muted-text">僅試算，不會修改真實投組；現金為負時代表融資，不含融資利率、保證金與追繳規則。</p>
              <div class="weight-grid">
                <label v-for="holding in risk.holdings" :key="holding.securityId" class="weight-row">
                  <span class="weight-label">{{ holding.ticker }} · 目前 <span class="prestige-mono">{{ (holding.weight * 100).toFixed(1) }}%</span></span>
                  <input
                    v-model.number="targetWeights[holding.securityId]"
                    type="number"
                    min="0"
                    step="0.1"
                    class="prestige-input weight-input"
                  />
                </label>
              </div>
              <div class="weight-total">
                目標持股 <span class="prestige-mono">{{ targetWeightTotal.toFixed(1) }}%</span> ·
                <span class="prestige-mono" :class="{ neg: targetCashWeight < 0 }">{{ targetCashWeight < 0 ? `融資 ${(Math.abs(targetCashWeight) * 100).toFixed(1)}%` : `現金 ${(targetCashWeight * 100).toFixed(1)}%` }}</span>
              </div>
              <p v-if="targetCashWeight < 0" class="leverage-note">槓桿試算：融資不作為風險資產加入價格序列。</p>
              <div class="btn-row" style="margin-top: 12px">
                <button class="prestige-btn" :disabled="scenarioLoading" @click="runTargetWeightScenario">{{ scenarioLoading ? '試算中…' : '開始試算' }}</button>
                <button class="prestige-btn" :disabled="scenarioLoading" @click="resetTargetWeights">重設為目前權重</button>
              </div>
              <div v-if="scenarioMessage" class="prestige-error" style="margin-top: 10px">{{ scenarioMessage }}</div>
              <div v-if="scenarioResult" class="scenario-metrics">
                <div
                  v-for="metric in [{label:'年化波動率',current:scenarioResult.current.historicalAnnualizedVolatility,scenario:scenarioResult.scenario.historicalAnnualizedVolatility},{label:'VaR 95%',current:scenarioResult.current.horizons[0]?.monteCarloVaR,scenario:scenarioResult.scenario.horizons[0]?.monteCarloVaR},{label:'HHI',current:scenarioResult.current.concentrationHhi,scenario:scenarioResult.scenario.concentrationHhi},{label:'最大持倉',current:scenarioResult.current.largestHoldingWeight,scenario:scenarioResult.scenario.largestHoldingWeight}]"
                  :key="metric.label"
                  class="mini-cell"
                >
                  <span class="prestige-label">{{ metric.label }}</span>
                  <strong class="mini-value prestige-mono">{{ ((metric.current ?? 0)*100).toFixed(1) }}% → {{ ((metric.scenario ?? 0)*100).toFixed(1) }}%</strong>
                </div>
              </div>
              <div v-if="scenarioResult" class="scenario-panels">
                <div class="mini-panel">
                  <h4 class="mini-title">治理警示變化</h4>
                  <div
                    v-for="alert in scenarioResult.scenarioGovernance?.alerts.filter(a => a.status !== 'normal') ?? []"
                    :key="alert.code"
                    class="mini-line"
                    :class="alert.status === 'critical' ? 'critical' : 'warning'"
                    >
                    {{ governanceLabel(alert.code) }} · <span class="prestige-mono">{{ scenarioAlertValue(alert) }}</span> · {{ alert.status === 'critical' ? '嚴重' : '注意' }}
                  </div>
                </div>
                <div class="mini-panel">
                  <h4 class="mini-title">壓力情境變化</h4>
                  <div v-for="stress in scenarioResult.scenarioStress" :key="stress.id" class="mini-line">
                    {{ stress.name }} · <span class="prestige-mono">{{ ((stress.totalImpact-(scenarioResult.currentStress.find(x=>x.id===stress.id)?.totalImpact??0))*100).toFixed(1) }} 個百分點</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Report snapshot detail -->
        <ScrollReveal v-if="selectedReportSnapshot" style="margin-bottom: 24px">
          <section class="prestige-panel prestige-panel-pad">
            <div class="panel-head">
              <div>
                <h2 class="panel-title">風險報告快照</h2>
                <span class="prestige-label">{{ reportCreatedAt(selectedReportSnapshot.createdAtUtc) }} · 資料截止 {{ selectedReportSnapshot.dataAsOfDate ?? '—' }} · {{ selectedReportSnapshot.model }} · {{ selectedReportSnapshot.thresholdVersion }}</span>
              </div>
              <button class="prestige-btn" @click="printReportSnapshot">列印／另存 PDF</button>
            </div>
            <p class="muted-text" style="margin-bottom: 14px">此內容從建立當下的資料庫快照讀取，市場資料後續更新不會改寫它。</p>
            <pre class="snapshot-pre">{{ JSON.stringify(selectedReportSnapshot.snapshot, null, 2) }}</pre>
          </section>
        </ScrollReveal>

        <!-- Run Info -->
        <ScrollReveal>
          <div class="prestige-panel flush-panel" style="margin-bottom: 24px">
            <div class="info-grid info-grid-2">
              <div
                v-for="(item, i) in [
                  { label: 'MODEL', value: runInfo.model },
                  { label: 'CONFIDENCE', value: runInfo.confidence },
                  { label: 'LOOKBACK', value: runInfo.lookback },
                  { label: 'DATE RANGE', value: `${fromDate} ~ ${toDate}` },
                ]"
                :key="i"
                class="info-cell"
              >
                <span class="prestige-label cell-label">{{ item.label }}</span>
                <span class="info-value prestige-mono">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Model Metadata -->
        <ScrollReveal :delay="0.05">
          <div class="prestige-panel flush-panel" style="margin-bottom: 24px">
            <h3 class="panel-sub-title">模型細節</h3>
            <div class="info-grid info-grid-4">
              <div
                v-for="(item, i) in [
                  { label: 'COVARIANCE METHOD', value: risk.covarianceMethod ?? '—' },
                  { label: 'RESIDUAL SAMPLING', value: risk.residualSampling ?? '—' },
                  { label: 'COMMON TRADING DAYS', value: risk.commonTradingDays ? `${risk.commonTradingDays} 天` : '—' },
                  { label: 'SHRINKAGE ALPHA', value: risk.shrinkageAlpha != null ? `${(risk.shrinkageAlpha * 100).toFixed(2)}%` : '—' },
                ]"
                :key="i"
                class="info-cell"
              >
                <span class="prestige-label cell-label">{{ item.label }}</span>
                <span class="info-value prestige-mono">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- KPI Cards -->
        <ScrollReveal :delay="0.1">
          <div class="prestige-panel flush-panel" style="margin-bottom: 24px">
            <div class="kpi-grid kpi-grid-5">
              <div v-for="(kpi, i) in riskKPIData" :key="i" class="kpi-cell">
                <span class="prestige-label cell-label">{{ kpi.label }}</span>
                <span class="kpi-value prestige-mono" :style="{ color: kpi.color }">{{ kpi.value }}</span>
                <span class="kpi-sub">{{ kpi.sub }}</span>
                <div class="accent-bar" :style="{ backgroundColor: kpi.color }" />
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Core Metrics -->
        <ScrollReveal :delay="0.15">
          <div class="prestige-panel flush-panel" style="margin-bottom: 24px">
            <div class="kpi-grid">
              <div v-for="(m, i) in metrics" :key="i" class="kpi-cell">
                <span class="prestige-label cell-label">{{ m.label }}</span>
                <span class="kpi-value prestige-mono metric-value">{{ m.value }}</span>
                <span class="kpi-sub">{{ m.sub }}</span>
                <div class="accent-bar metric-accent" />
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- VaR / ES model comparison -->
        <div ref="modelComparisonSentinel" class="deferred-sentinel" aria-hidden="true" />
        <ScrollReveal class="mt-20">
          <div v-if="modelComparisonState === 'ready'" class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">Value at Risk 分析</h2>
              <span class="prestige-label">Value at Risk — 95% & 99% 信賴區間</span>
            </div>
            <div class="section-body">
              <div class="stack">
                <div class="table-wrap">
                  <table class="prestige-table">
                    <thead>
                      <tr>
                        <th style="text-align: left">計算方法</th>
                        <th style="text-align: right">VaR 95%</th>
                        <th style="text-align: right">VaR 99%</th>
                        <th style="text-align: left; padding-left: 16px">備註</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(v, i) in varTableRows" :key="i">
                        <td>{{ v.method }}</td>
                        <td class="prestige-mono td-num" :class="{ 'td-warn': v.var95 < -0.048 }">{{ (v.var95 * 100).toFixed(2) }}%</td>
                        <td class="prestige-mono td-num td-danger">{{ (v.var99 * 100).toFixed(2) }}%</td>
                        <td class="td-muted" style="padding-left: 16px">{{ v.note }}</td>
                      </tr>
                    </tbody>
                  </table>
                  <p class="table-note">
                    所有方法皆使用正式 API 資料；保守 FHS 僅將標準化殘差向量的最極端 1% 徑向截尾，原始 FHS 仍為預設模型。
                  </p>
                </div>

                <div>
                  <h3 class="chart-title">日報酬分布直方圖（共同日資料）</h3>
                  <svg width="100%" height="270" viewBox="0 0 400 270" style="display: block; width: min(100%, 760px)">
                    <line v-for="value in histogramChart.ticks" :key="'g-' + value" x1="50" :y1="histogramChart.y(value)" x2="380" :y2="histogramChart.y(value)" stroke="rgba(201,168,106,0.12)" stroke-width="1" stroke-dasharray="4 4" />
                    <text v-for="value in histogramChart.ticks" :key="'gy-' + value" x="45" :y="histogramChart.y(value) + 4" text-anchor="end" fill="#9a917c" font-size="10">{{ Math.round(value) }}</text>

                    <g v-for="(bin, i) in officialHistogram" :key="i">
                      <rect
                        :x="50 + i * 23"
                        :y="histogramChart.y(bin.count)"
                        width="19"
                        :height="histogramChart.height(bin.count)"
                        :fill="bin.isTail99 ? '#b05c5c' : bin.isTail95 ? '#d4a24e' : 'rgba(201,168,106,0.35)'"
                        class="transition-all"
                      />
                      <text :x="50 + i * 23 + 9.5" y="228" text-anchor="middle" fill="#9a917c" font-size="7">{{ bin.bin }}</text>
                    </g>

                    <g transform="translate(55, 12)">
                      <rect x="0" y="-6" width="10" height="8" fill="rgba(201,168,106,0.35)" />
                      <text x="14" y="0" fill="#9a917c" font-size="9">正常區間</text>
                      <rect x="70" y="-6" width="10" height="8" fill="#d4a24e" />
                      <text x="84" y="0" fill="#9a917c" font-size="9">95% 尾部</text>
                      <rect x="145" y="-6" width="10" height="8" fill="#b05c5c" />
                      <text x="159" y="0" fill="#9a917c" font-size="9">99% 尾部</text>
                    </g>
                  </svg>
                </div>
              </div>
            </div>
          </div>
          <div v-else class="prestige-panel prestige-panel-pad deferred-panel">
            <span class="prestige-label">MODEL COMPARISON</span>
            <p v-if="modelComparisonState === 'loading'" class="muted-text">正在載入 VaR／ES 模型比較…</p>
            <template v-else-if="modelComparisonState === 'error'">
              <p class="prestige-error">{{ modelComparisonError }}</p>
              <button class="prestige-btn" @click="loadModelComparison">重新載入</button>
            </template>
            <p v-else class="muted-text">捲動至此區塊時會載入模型比較。</p>
          </div>
        </ScrollReveal>

        <!-- ES / CVaR Analysis -->
        <ScrollReveal v-if="modelComparisonState === 'ready'" class="mt-20">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">Expected Shortfall (ES) 分析</h2>
              <span class="prestige-label">Conditional VaR — 尾部損失期望值</span>
            </div>
            <div class="section-body">
              <div class="stack">
                <div>
                  <div class="table-wrap">
                    <table class="prestige-table">
                      <thead>
                        <tr>
                          <th style="text-align: left">計算方法</th>
                          <th style="text-align: right">ES 95%</th>
                          <th style="text-align: right">ES 99%</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="(e, i) in esTableRows" :key="i">
                          <td>{{ e.method }}</td>
                          <td class="prestige-mono td-num td-warn">{{ (e.es95 * 100).toFixed(2) }}%</td>
                          <td class="prestige-mono td-num td-danger">{{ (e.es99 * 100).toFixed(2) }}%</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                  <div class="note-panel">
                    <p class="note-text">
                      ES（Expected Shortfall）衡量當損失超過 VaR 閾值時的
                      <strong class="note-strong">平均損失程度</strong>。
                      相較於 VaR 僅反映單一分位數點，ES 更完整地捕捉尾部風險。
                    </p>
                  </div>
                </div>

                <div>
                  <h3 class="chart-title">VaR vs ES 比較（MVEWMA-FHS）</h3>
                  <svg width="100%" height="340" viewBox="0 0 400 340" style="display: block; width: min(100%, 760px); overflow: hidden">
                    <line v-for="value in varEsChart.ticks" :key="'g-' + value" x1="80" :y1="varEsChart.y(value)" x2="380" :y2="varEsChart.y(value)" stroke="rgba(201,168,106,0.12)" stroke-width="1" stroke-dasharray="4 4" />
                    <text v-for="value in varEsChart.ticks" :key="'y-' + value" x="75" :y="varEsChart.y(value) + 4" text-anchor="end" fill="#9a917c" font-size="10">{{ value.toFixed(0) }}%</text>

                    <g v-for="(d, i) in officialVarEsComparison" :key="i">
                      <rect :x="85 + i * 58" :y="varEsChart.y(0)" width="22" :height="varEsChart.height(d.var)" fill="#c9a86a" opacity="0.85" />
                      <rect :x="110 + i * 58" :y="varEsChart.y(0)" width="22" :height="varEsChart.height(d.es)" fill="#b05c5c" opacity="0.85" />
                      <text :x="85 + i * 58 + 22" y="255" text-anchor="middle" fill="#9a917c" font-size="9">{{ d.confidence }}</text>
                    </g>

                    <g transform="translate(90, 285)">
                      <rect x="0" y="-6" width="12" height="8" fill="#c9a86a" opacity="0.85" />
                      <text x="16" y="0" fill="#9a917c" font-size="9">VaR</text>
                      <rect x="50" y="-6" width="12" height="8" fill="#b05c5c" opacity="0.85" />
                      <text x="66" y="0" fill="#9a917c" font-size="9">ES</text>
                    </g>
                  </svg>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Stress Test Scenarios -->
        <div ref="stressTestSentinel" class="deferred-sentinel" aria-hidden="true" />
        <ScrollReveal class="mt-20">
          <div v-if="stressTestState === 'ready'" class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">壓力測試情境</h2>
              <span class="prestige-label">Stress Testing — 六大極端情境模擬</span>
            </div>
            <div class="section-body">
              <div class="grid-3">
                <div
                  v-for="s in formalStressScenarios"
                  :key="s.id"
                  class="prestige-panel stress-card"
                  :class="{ selected: selectedScenario === s.id }"
                  @click="selectedScenario = selectedScenario === s.id ? null : s.id"
                >
                  <div class="stress-top">
                    <span class="stress-name">{{ s.name }}</span>
                    <span class="prestige-tag" :class="s.type === 'historical' ? 'tag-historical' : 'tag-hypo'">{{ s.type === 'historical' ? '歷史回放' : '假設情境' }}</span>
                  </div>

                  <p class="stress-method">{{ s.methodology }}</p>

                  <div class="stress-impact">
                    <span class="impact-value prestige-mono">{{ (s.totalImpact * 100).toFixed(1) }}%</span>
                    <span class="prestige-label">投組衝擊</span>
                  </div>

                  <div v-if="selectedScenario === s.id" class="stress-detail">
                    <div v-for="h in s.holdings" :key="h.ticker" class="stress-holding">
                      <span>{{ h.ticker }} · {{ h.industry }}</span>
                      <span class="prestige-mono stress-holding-value">{{ (h.contribution * 100).toFixed(1) }}%</span>
                    </div>
                    <div class="stress-industries">
                      <span v-for="industry in s.industries" :key="industry.industry" class="prestige-tag">{{ industry.industry }} <span class="prestige-mono">{{ (industry.contribution * 100).toFixed(1) }}%</span></span>
                    </div>
                  </div>
                </div>
              </div>

              <div v-if="selectedStressScenario" class="mini-panel stress-detail-panel">
                <h3 class="mini-title" style="font-size: 15px">持股壓力明細 — {{ selectedStressScenario.name }}</h3>
                <p class="muted-text" style="margin-bottom: 14px">{{ selectedStressScenario.methodology }}</p>
                <div class="table-wrap">
                  <table class="prestige-table">
                    <thead>
                      <tr>
                        <th>代號／名稱</th>
                        <th>產業</th>
                        <th style="text-align: right">當前權重</th>
                        <th style="text-align: right">情境起始價</th>
                        <th style="text-align: right">情境結束價</th>
                        <th style="text-align: right">情境期間報酬</th>
                        <th style="text-align: right">對投組衝擊貢獻</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="h in selectedStressScenario.holdings.slice().sort((a,b)=>a.contribution-b.contribution)" :key="h.ticker">
                        <td>{{ h.ticker }} · {{ h.securityName }}</td>
                        <td class="td-muted">{{ h.industry }}</td>
                        <td class="prestige-mono td-num">{{ (h.weight*100).toFixed(1) }}%</td>
                        <td class="prestige-mono td-num">{{ h.basePrice.toFixed(2) }}</td>
                        <td class="prestige-mono td-num">{{ h.stressedPrice.toFixed(2) }}</td>
                        <td class="prestige-mono td-num">{{ (h.shock*100).toFixed(1) }}%</td>
                        <td class="prestige-mono td-num td-danger">{{ (h.contribution*100).toFixed(1) }}%</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </div>
          <div v-else class="prestige-panel prestige-panel-pad deferred-panel">
            <span class="prestige-label">STRESS TESTING</span>
            <p v-if="stressTestState === 'loading'" class="muted-text">正在載入壓力測試情境…</p>
            <template v-else-if="stressTestState === 'error'">
              <p class="prestige-error">{{ stressTestError }}</p>
              <button class="prestige-btn" @click="loadStressTest">重新載入</button>
            </template>
            <p v-else class="muted-text">捲動至此區塊時會載入壓力測試。</p>
          </div>
        </ScrollReveal>

        <!-- Backtest Results -->
        <div ref="backtestSentinel" class="deferred-sentinel" aria-hidden="true" />
        <ScrollReveal class="mt-20">
          <div v-if="backtestState === 'ready'" class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">VaR 回測驗證</h2>
              <span class="prestige-label">Backtesting — 252 日滾動 VaR / ES</span>
            </div>
            <div class="section-body">
              <div class="btn-row" style="margin-bottom: 20px">
                <button
                  v-for="model in backtestModels"
                  :key="model"
                  class="prestige-btn chip-btn prestige-mono"
                  :class="{ active: selectedBacktestModel === model }"
                  @click="selectedBacktestModel = model"
                >{{ model }}</button>
                <button
                  v-for="confidence in backtestConfidences"
                  :key="confidence"
                  class="prestige-btn chip-btn prestige-mono"
                  :class="{ active: selectedBacktestConfidence === confidence }"
                  @click="selectedBacktestConfidence = confidence"
                >{{ (confidence * 100).toFixed(0) }}%</button>
              </div>
              <div class="grid-2">
                <svg width="100%" height="280" viewBox="0 0 500 280">
                  <line v-for="value in backtestChart.ticks" :key="'g-' + value" x1="50" :y1="backtestChart.y(value)" x2="480" :y2="backtestChart.y(value)" stroke="rgba(201,168,106,0.12)" stroke-width="1" stroke-dasharray="4 4" />
                  <text v-for="value in backtestChart.ticks" :key="value" x="45" :y="backtestChart.y(value) + 4" text-anchor="end" fill="#9a917c" font-size="10">{{ value }}%</text>

                  <line x1="50" :y1="backtestChart.y(0)" x2="480" :y2="backtestChart.y(0)" stroke="rgba(245,239,224,0.45)" stroke-width="1.5" />

                  <line x1="50" :y1="backtestChart.y(backtestVaRLine)" x2="480" :y2="backtestChart.y(backtestVaRLine)" stroke="#b05c5c" stroke-width="1" stroke-dasharray="4 4" />
                  <text x="485" :y="backtestChart.y(backtestVaRLine) + 3" fill="#b05c5c" font-size="9">VaR {{ (selectedBacktestConfidence * 100).toFixed(0) }}%</text>

                  <g v-for="(d, i) in backtestData" :key="i">
                    <line
                      :x1="d.x"
                      :y1="backtestChart.y(0)"
                      :x2="d.x"
                      :y2="backtestChart.y(d.actual)"
                      :stroke="d.breached ? '#b05c5c' : d.actual >= 0 ? '#c9a86a' : 'rgba(154,145,124,0.75)'"
                      stroke-width="4"
                    />
                    <text v-if="d.breached" :x="d.x" :y="backtestChart.y(d.actual) - 6" text-anchor="middle" fill="#b05c5c" font-size="8">!</text>
                  </g>

                  <template v-for="(d, i) in backtestData" :key="'xl-' + i">
                    <text v-if="i % 5 === 0 || i === backtestData.length - 1" :x="d.x" y="260" text-anchor="middle" fill="#9a917c" font-size="8">{{ d.date.slice(5) }}</text>
                  </template>
                </svg>

                <div class="backtest-side">
                  <div class="mini-panel" style="margin-bottom: 16px">
                    <div class="stat-grid-2">
                      <div v-for="stat in backtestStats" :key="stat.label">
                        <span class="prestige-label cell-label">{{ stat.label }}</span>
                        <span class="stat-value prestige-mono" :style="{ color: stat.color }">{{ stat.value }}</span>
                      </div>
                    </div>
                  </div>

                  <p class="muted-text">
                    使用目前持倉權重回放近三年共同日價格；圖表顯示最近 60 個有效回測日。可切換正式 Historical 與 MVEWMA-FHS 的 95%／99% 回測結果。
                  </p>
                </div>
              </div>
            </div>
          </div>
          <div v-else class="prestige-panel prestige-panel-pad deferred-panel">
            <span class="prestige-label">BACKTESTING</span>
            <p v-if="backtestState === 'loading'" class="muted-text">正在計算 252 日滾動 VaR／ES 回測，通常需要數秒…</p>
            <template v-else-if="backtestState === 'error'">
              <p class="prestige-error">{{ backtestError }}</p>
              <button class="prestige-btn" @click="loadBacktest">重新載入</button>
            </template>
            <p v-else class="muted-text">捲動至此區塊時會開始回測。</p>
          </div>
        </ScrollReveal>

        <!-- Monte Carlo Simulation -->
        <div ref="monteCarloSentinel" class="deferred-sentinel" aria-hidden="true" />
        <ScrollReveal class="mt-20" style="margin-bottom: 80px">
          <div v-if="monteCarloState === 'ready'" class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">蒙地卡羅模擬</h2>
              <span class="prestige-label">{{ monteCarlo?.model ?? 'MVEWMA-FHS' }} — {{ monteCarlo?.simulations?.toLocaleString() ?? '10,000' }} 次、{{ monteCarlo?.horizonDays ?? 252 }} 個交易日路徑模擬</span>
            </div>
            <div class="section-body">
              <div class="btn-row" style="margin-bottom: 16px">
                <button
                  v-for="choice in [{ id: 'base', label: '原始 FHS' }, { id: 'conservative', label: '保守 FHS（p99）' }]"
                  :key="choice.id"
                  class="prestige-btn chip-btn"
                  :class="{ active: selectedMonteCarloModel === choice.id }"
                  @click="selectedMonteCarloModel = choice.id as 'base' | 'conservative'"
                >{{ choice.label }}</button>
              </div>
              <template v-if="monteCarlo?.status === 'ready'">
                <svg width="100%" height="400" viewBox="0 0 900 400" preserveAspectRatio="xMidYMid meet">
                  <line v-for="tick in monteCarloChart.ticks" :key="'g-' + tick" x1="70" :y1="monteCarloChart.y(tick)" x2="850" :y2="monteCarloChart.y(tick)" :stroke="tick === 0 ? 'rgba(245,239,224,0.55)' : 'rgba(201,168,106,0.12)'" :stroke-width="tick === 0 ? 1.5 : 1" :stroke-dasharray="tick === 0 ? 'none' : '4 4'" />
                  <text
                    v-for="tick in monteCarloChart.ticks"
                    :key="'t-' + tick"
                    x="65"
                    :y="monteCarloChart.y(tick) + 4"
                    text-anchor="end"
                    fill="#9a917c"
                    font-size="10"
                  >{{ `${tick > 0 ? '+' : ''}${(tick * 100).toFixed(0)}%` }}</text>

                  <!-- 98% band first, then the narrower 90% band so both remain distinguishable. -->
                  <polygon :points="monteCarloChart.p1" fill="rgba(154,145,124,0.14)" stroke="#9a917c" stroke-opacity="0.72" stroke-width="0.8" />
                  <polygon :points="monteCarloChart.p5" fill="rgba(201,168,106,0.18)" stroke="#c9a86a" stroke-opacity="0.82" stroke-width="0.8" />

                  <polyline :points="monteCarloChart.p50" fill="none" stroke="#ddc18a" stroke-width="2" />

                  <polyline
                    v-for="path in monteCarlo.samplePaths"
                    :key="path.pathIndex"
                    :points="path.cumulativeReturns.map((value, day) => `${monteCarloChart.x(day)},${monteCarloChart.y(value)}`).join(' ')"
                    fill="none"
                    :stroke="['#9a917c', '#c9a86a', '#7fa387', '#ddc18a'][(path.pathIndex - 1) % 4]"
                    stroke-width="1"
                    opacity="0.58"
                  />

                  <g transform="translate(80, 18)">
                    <line x1="0" y1="0" x2="20" y2="0" stroke="#ddc18a" stroke-width="2" />
                    <text x="25" y="4" fill="#f5efe0" font-size="10">中位數路徑</text>
                    <rect x="100" y="-6" width="16" height="10" fill="rgba(154,145,124,0.5)" />
                    <text x="120" y="4" fill="#9a917c" font-size="10">98% 區間</text>
                    <rect x="180" y="-6" width="16" height="10" fill="rgba(201,168,106,0.55)" />
                    <text x="200" y="4" fill="#c9a86a" font-size="10">90% 區間</text>
                  </g>
                </svg>
                <div class="prestige-panel flush-panel" style="margin-top: 20px">
                  <div class="kpi-grid kpi-grid-5">
                    <div v-for="stat in monteCarloStats" :key="stat.label" class="kpi-cell kpi-cell-sm">
                      <span class="prestige-label cell-label">{{ stat.label }}</span>
                      <span class="stat-value prestige-mono" :style="{ color: stat.color }">{{ stat.value }}</span>
                    </div>
                  </div>
                </div>
                <div class="table-wrap" style="margin-top: 20px">
                  <div class="prestige-label" style="margin-bottom: 8px">Model Comparison — 原始模型為預設，保守版僅供比較</div>
                  <table class="prestige-table" style="min-width: 760px">
                    <thead>
                      <tr>
                        <th>模型</th>
                        <th>正報酬機率</th>
                        <th>中位數</th>
                        <th>期望</th>
                        <th>5% 情境</th>
                        <th>1% 情境</th>
                        <th>殘差截尾比例</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="item in monteCarloComparison" :key="item.label">
                        <td>{{ item.label }}</td>
                        <td class="prestige-mono">{{ item.positive }}</td>
                        <td class="prestige-mono">{{ item.median }}</td>
                        <td class="prestige-mono">{{ item.expected }}</td>
                        <td class="prestige-mono td-warn">{{ item.p5 }}</td>
                        <td class="prestige-mono td-danger">{{ item.p1 }}</td>
                        <td class="prestige-mono">{{ item.capped }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <div class="mini-panel" style="margin-top: 20px">
                  <div class="panel-head" style="margin-bottom: 12px">
                    <span class="mini-title" style="margin: 0">模型診斷</span>
                    <span class="prestige-label">Model Diagnostics</span>
                  </div>
                  <div v-if="monteCarlo.diagnostics.rightSkewWarning" class="warn-box">
                    {{ monteCarlo.diagnostics.rightSkewMessage }}
                  </div>
                  <p v-else class="muted-text" style="margin-bottom: 14px">期望值與中位數差距未達 25 個百分點右偏警示門檻；仍請一併參考下行情境。</p>
                  <div class="diag-grid">
                    <div v-for="item in monteCarloDiagnostics" :key="item.label" class="diag-row">
                      <span class="prestige-label">{{ item.label }}</span>
                      <span class="prestige-mono diag-value">{{ item.value }}</span>
                    </div>
                  </div>
                </div>
                <p class="table-note" style="margin-top: 16px">以目前持倉權重與共同日價格資料推演未來報酬分布；區間不代表發生機率保證。</p>
              </template>
            </div>
          </div>
          <div v-else class="prestige-panel prestige-panel-pad deferred-panel">
            <span class="prestige-label">MONTE CARLO</span>
            <p v-if="monteCarloState === 'loading'" class="muted-text">正在載入 10,000 次 Monte Carlo 模擬…</p>
            <template v-else-if="monteCarloState === 'error'">
              <p class="prestige-error">{{ monteCarloError }}</p>
              <button class="prestige-btn" @click="loadMonteCarlo">重新載入</button>
            </template>
            <p v-else class="muted-text">捲動至此區塊時會開始 Monte Carlo 模擬。</p>
          </div>
        </ScrollReveal>

        <!-- Holdings Risk Contribution -->
        <ScrollReveal class="mt-20">
          <div class="prestige-panel" style="margin-bottom: 40px">
            <div class="section-head">
              <h2 class="panel-title">持倉風險摘要</h2>
              <span class="prestige-label">Holding Risk Summary — 後端資料</span>
            </div>
            <div class="section-body table-wrap">
              <div class="holdings-headline">
                風險來源模型年化波動率：<span class="prestige-mono hl">{{ (risk.riskSourceAnnualizedVolatility * 100).toFixed(2) }}%</span>
              </div>
              <table class="prestige-table">
                <thead>
                  <tr>
                    <th>代號</th>
                    <th>名稱</th>
                    <th>產業</th>
                    <th style="text-align: right">權重</th>
                    <th style="text-align: right">年化波動率</th>
                    <th style="text-align: right">Component</th>
                    <th style="text-align: right">風險占比</th>
                    <th style="text-align: right">Marginal（+1%）</th>
                    <th style="text-align: right">Incremental</th>
                    <th style="text-align: right">資料筆數</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="h in risk.holdings" :key="h.securityId">
                    <td>{{ h.ticker }}</td>
                    <td>{{ h.securityName }}</td>
                    <td class="td-muted">{{ h.industry }}</td>
                    <td class="prestige-mono td-num">{{ (h.weight * 100).toFixed(2) }}%</td>
                    <td class="prestige-mono td-num" :class="{ 'td-warn': h.annualizedVolatility > 0.3 }">{{ (h.annualizedVolatility * 100).toFixed(2) }}%</td>
                    <td class="prestige-mono td-num">{{ (h.componentVolatility * 100).toFixed(2) }}%</td>
                    <td class="prestige-mono td-num" :class="h.componentRiskShare < 0 ? 'td-pos' : 'td-danger'">{{ (h.componentRiskShare * 100).toFixed(1) }}%</td>
                    <td class="prestige-mono td-num">{{ h.marginalVolatility.toFixed(2) }}%</td>
                    <td class="prestige-mono td-num">{{ (h.incrementalVolatility * 100).toFixed(2) }}%</td>
                    <td class="prestige-mono td-num">{{ h.dataPointCount }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p class="table-note" style="padding: 0 24px 20px; margin-top: 0">
              Component 可加總為上方的風險來源模型年化波動率；Marginal 為權重增加 1 個百分點的年化波動率變化；Incremental 假設移除部位後轉為現金。負值代表分散效果。
            </p>
          </div>
        </ScrollReveal>

        <ScrollReveal class="mt-20" v-if="risk.industries?.length">
          <div class="prestige-panel" style="margin-bottom: 40px">
            <div class="section-head">
              <h2 class="panel-title">產業風險來源</h2>
              <span class="prestige-label">Industry Risk Sources — 後端資料</span>
            </div>
            <div class="section-body table-wrap">
              <table class="prestige-table">
                <thead>
                  <tr>
                    <th>產業</th>
                    <th style="text-align: right">持倉數</th>
                    <th style="text-align: right">權重</th>
                    <th style="text-align: right">Component</th>
                    <th style="text-align: right">風險占比</th>
                    <th style="text-align: right">Marginal（+1%）</th>
                    <th style="text-align: right">Incremental</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="industry in risk.industries" :key="industry.industry">
                    <td>{{ industry.industry }}</td>
                    <td class="prestige-mono td-num">{{ industry.holdingCount }}</td>
                    <td class="prestige-mono td-num">{{ (industry.weight * 100).toFixed(2) }}%</td>
                    <td class="prestige-mono td-num">{{ (industry.componentVolatility * 100).toFixed(2) }}%</td>
                    <td class="prestige-mono td-num" :class="industry.componentRiskShare < 0 ? 'td-pos' : 'td-danger'">{{ (industry.componentRiskShare * 100).toFixed(1) }}%</td>
                    <td class="prestige-mono td-num">{{ industry.marginalVolatility.toFixed(2) }}%</td>
                    <td class="prestige-mono td-num">{{ (industry.incrementalVolatility * 100).toFixed(2) }}%</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </ScrollReveal>
      </template>

      <div style="height: 60px" />
    </div>

    <Footer :dark="true" label="RISK" />
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.deferred-sentinel {
  height: 1px;
}

.deferred-panel {
  min-height: 150px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 12px;
}

.mt-20 {
  margin-top: 40px;
}

.transition-all {
  transition: all 0.3s ease;
}

/* ---- Header ---- */
.back-btn {
  margin-bottom: 28px;
}

.page-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  margin-bottom: 36px;
}

.page-title {
  font-family: var(--serif);
  font-size: 28px;
  font-weight: 600;
  margin: 6px 0 0;
  letter-spacing: 0.01em;
}

/* 細金狀態點（pulse 僅以透明度呼吸，不使用光暈） */
.dot {
  display: inline-block;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

.dot.pulse {
  animation: dot-pulse 1.6s ease-in-out infinite;
}

@keyframes dot-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.tag-completed {
  color: var(--up);
  border-color: rgba(127, 163, 135, 0.4);
}

/* ---- Panel primitives ---- */
.panel-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}

.panel-title {
  margin: 0 0 6px;
  font-family: var(--serif);
  font-size: 20px;
  font-weight: 600;
}

.panel-sub-title {
  margin: 0;
  padding: 20px 20px 16px;
  font-family: var(--serif);
  font-size: 16px;
  font-weight: 600;
}

.section-head {
  padding: 20px 24px;
  border-bottom: 1px solid var(--gold-border-soft);
}

.section-body {
  padding: 24px;
}

.flush-panel {
  overflow: hidden;
}

.muted-text {
  color: var(--muted);
  font-size: 13px;
  margin: 0;
  line-height: 1.7;
}

.btn-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  align-items: center;
}

/* ---- Governance status tags ---- */
.tag-critical {
  color: var(--down);
  border-color: rgba(176, 92, 92, 0.45);
}

.tag-warning {
  color: #d4a24e;
  border-color: rgba(212, 162, 78, 0.45);
}

.tag-normal {
  color: var(--up);
  border-color: rgba(127, 163, 135, 0.4);
}

.tag-historical {
  color: #d4a24e;
  border-color: rgba(212, 162, 78, 0.45);
}

.tag-hypo {
  color: var(--gold);
  border-color: var(--gold-border);
}

/* ---- Governance alerts ---- */
.alert-list {
  display: grid;
  gap: 8px;
}

.alert-item {
  padding: 12px 14px;
  border-left: 2px solid;
  border-radius: 0 6px 6px 0;
}

.alert-item.critical {
  background: rgba(176, 92, 92, 0.08);
  border-color: var(--down);
}

.alert-item.critical .alert-name {
  color: var(--down);
}

.alert-item.warning {
  background: rgba(212, 162, 78, 0.08);
  border-color: #d4a24e;
}

.alert-item.warning .alert-name {
  color: #d4a24e;
}

.alert-line {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
  align-items: baseline;
}

.alert-thresholds {
  color: var(--ivory);
  font-size: 13px;
}

.alert-msg {
  color: var(--muted);
  font-size: 12px;
  margin-top: 4px;
}

/* ---- Sub blocks (snapshots / what-if) ---- */
.sub-block {
  margin-top: 20px;
  padding-top: 20px;
  border-top: 1px solid var(--gold-border-soft);
}

.sub-title {
  margin: 0 0 6px;
  font-family: var(--serif);
  font-size: 16px;
  font-weight: 600;
}

.snap-msg {
  font-size: 12px;
}

.snap-list {
  margin-top: 14px;
  display: grid;
  gap: 8px;
  justify-items: start;
}

.snap-btn {
  text-align: left;
  font-size: 12px;
  letter-spacing: 0.04em;
  padding: 9px 14px;
}

.weight-grid {
  display: grid;
  gap: 8px;
  margin-top: 14px;
}

.weight-row {
  display: grid;
  grid-template-columns: 1fr 110px;
  gap: 10px;
  align-items: center;
}

.weight-label {
  font-size: 13px;
  color: var(--ivory);
}

.weight-input {
  text-align: right;
  font-family: 'SFMono-Regular', Consolas, 'Courier New', monospace;
}

.weight-total {
  margin-top: 12px;
  font-size: 13px;
  color: var(--muted);
}

.weight-total .neg {
  color: #d4a24e;
}

.leverage-note {
  margin: 6px 0 0;
  color: #d4a24e;
  font-size: 12px;
}

.scenario-metrics {
  margin-top: 14px;
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 10px;
}

.scenario-panels {
  margin-top: 16px;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

/* ---- Mini panels / cells ---- */
.mini-panel {
  padding: 16px 18px;
  border: 1px solid var(--gold-border-soft);
  border-radius: 6px;
  background: var(--panel-bg);
}

.mini-cell {
  padding: 12px 14px;
  border: 1px solid var(--gold-border-soft);
  border-radius: 6px;
  background: rgba(11, 18, 32, 0.5);
}

.mini-title {
  margin: 0 0 8px;
  font-family: var(--serif);
  font-size: 14px;
  font-weight: 600;
  color: var(--ivory);
}

.mini-value {
  display: block;
  margin-top: 6px;
  color: var(--ivory);
  font-size: 14px;
  font-weight: 600;
}

.mini-line {
  font-size: 13px;
  color: var(--ivory);
  padding: 3px 0;
}

.mini-line.critical {
  color: var(--down);
}

.mini-line.warning {
  color: #d4a24e;
}

.snapshot-pre {
  margin: 0;
  padding: 14px;
  overflow: auto;
  max-height: 420px;
  background: rgba(11, 18, 32, 0.85);
  border: 1px solid var(--gold-border-soft);
  border-radius: 6px;
  color: var(--ivory);
  font-family: 'SFMono-Regular', Consolas, 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.5;
}

/* ---- Info grids (run info / metadata) ---- */
.info-grid {
  display: grid;
  gap: 1px;
  background: var(--gold-border-soft);
}

.info-grid-2 {
  grid-template-columns: repeat(2, 1fr);
}

.info-grid-4 {
  grid-template-columns: repeat(4, 1fr);
}

.info-cell {
  background: rgba(11, 18, 32, 0.6);
  padding: 20px;
}

.cell-label {
  display: block;
  margin-bottom: 6px;
}

.info-value {
  font-size: 15px;
  font-weight: 600;
  color: var(--ivory);
}

/* ---- KPI grids ---- */
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 1px;
  background: var(--gold-border-soft);
}

.kpi-cell {
  position: relative;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  min-height: 130px;
  padding: 24px 12px;
  background: rgba(11, 18, 32, 0.6);
  transition: background 0.3s ease;
}

.kpi-cell:hover {
  background: rgba(201, 168, 106, 0.07);
}

.kpi-cell-sm {
  min-height: 96px;
  padding: 16px 10px;
}

.kpi-value {
  font-size: 26px;
  font-weight: 600;
}

.kpi-sub {
  font-size: 11px;
  color: var(--muted);
  text-align: center;
  margin-top: 4px;
}

.metric-value {
  color: var(--ivory);
}

.accent-bar {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 0;
  transition: width 0.3s ease;
}

.kpi-cell:hover .accent-bar {
  width: 2px;
}

.metric-accent {
  background: var(--gold);
}

.stat-value {
  font-size: 18px;
  font-weight: 600;
}

/* ---- Tables ---- */
.table-wrap {
  overflow-x: auto;
}

.prestige-table th,
.prestige-table td {
  white-space: nowrap;
}

.td-num {
  text-align: right;
}

.td-muted {
  color: var(--muted);
}

.td-warn {
  color: #d4a24e;
}

.td-danger {
  color: var(--down);
}

.td-pos {
  color: var(--up);
}

.table-note {
  font-size: 11px;
  color: var(--muted);
  margin: 12px 0 0;
}

/* ---- Charts ---- */
.stack {
  display: grid;
  gap: 32px;
}

.chart-title {
  font-family: var(--serif);
  font-size: 14px;
  font-weight: 600;
  margin: 0 0 12px;
  color: var(--ivory);
}

.note-panel {
  margin-top: 16px;
  padding: 16px 18px;
  border: 1px solid var(--gold-border-soft);
  border-radius: 6px;
  background: var(--panel-bg);
}

.note-text {
  font-size: 12px;
  color: var(--muted);
  margin: 0;
  line-height: 1.7;
}

.note-strong {
  color: var(--ivory);
}

/* ---- Stress cards ---- */
.grid-3 {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
}

.grid-2 {
  display: grid;
  grid-template-columns: 1fr;
  gap: 32px;
}

.stress-card {
  padding: 20px;
  cursor: pointer;
  transition: transform 0.2s ease, border-color 0.3s ease, background 0.3s ease;
}

.stress-card:hover {
  transform: translateY(-2px);
}

.stress-card.selected {
  border-color: var(--gold);
  background: rgba(201, 168, 106, 0.07);
}

.stress-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 12px;
}

.stress-name {
  font-family: var(--serif);
  font-size: 14px;
  font-weight: 600;
  color: var(--ivory);
}

.stress-method {
  font-size: 12px;
  color: var(--muted);
  margin: 0 0 12px;
  line-height: 1.6;
}

.stress-impact {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.impact-value {
  font-size: 22px;
  font-weight: 600;
  color: var(--down);
}

.stress-detail {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid var(--gold-border-soft);
}

.stress-holding {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 0;
  font-size: 12px;
  color: var(--muted);
}

.stress-holding-value {
  color: var(--ivory);
}

.stress-industries {
  margin-top: 12px;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.stress-detail-panel {
  margin-top: 20px;
}

/* ---- Toggle chips ---- */
.chip-btn {
  padding: 8px 16px;
  font-size: 12px;
}

.chip-btn.active {
  background: var(--gold);
  border-color: var(--gold);
  color: #0b1220;
}

/* ---- Backtest / Monte Carlo ---- */
.backtest-side {
  display: flex;
  flex-direction: column;
  justify-content: center;
}

.stat-grid-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.warn-box {
  margin-bottom: 14px;
  padding: 12px 14px;
  border-left: 2px solid #d4a24e;
  border-radius: 0 6px 6px 0;
  background: rgba(212, 162, 78, 0.08);
  color: #d4a24e;
  font-size: 13px;
}

.diag-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 12px 24px;
}

.diag-row {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  border-bottom: 1px solid var(--gold-border-soft);
  padding-bottom: 8px;
}

.diag-value {
  color: var(--ivory);
}

.holdings-headline {
  margin-bottom: 16px;
  color: var(--ivory);
  font-size: 14px;
}

.hl {
  color: var(--gold);
  font-weight: 600;
}

/* ---- Responsive ---- */
@media (min-width: 768px) {
  .grid-3 {
    grid-template-columns: 1fr 1fr;
  }
}

@media (min-width: 1024px) {
  .kpi-grid {
    grid-template-columns: repeat(4, 1fr);
  }

  .kpi-grid-5 {
    grid-template-columns: repeat(5, 1fr);
  }

  .grid-2 {
    grid-template-columns: 1fr 1fr;
  }

  .grid-3 {
    grid-template-columns: repeat(3, 1fr);
  }

  .diag-grid {
    grid-template-columns: 1fr 1fr;
  }
}

@media (max-width: 900px) {
  .scenario-metrics {
    grid-template-columns: repeat(2, 1fr);
  }

  .scenario-panels {
    grid-template-columns: 1fr;
  }

  .info-grid-4 {
    grid-template-columns: repeat(2, 1fr);
  }

  .section-head {
    padding: 16px;
  }

  .section-body {
    padding: 16px;
  }
}
</style>
