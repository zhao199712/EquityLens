<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
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
  const pColor = (value: number | null) => value == null ? '#666666' : value < 0.05 ? '#f87171' : value < 0.10 ? '#facc15' : '#34d399'
  const esColor = model.esTailLossRatio == null ? '#666666' : model.esTailLossRatio > 1.10 ? '#f87171' : model.esTailLossRatio < 0.90 ? '#facc15' : '#34d399'
  const esValue = model.esTailLossRatio == null ? '資料不足' : `${model.esTailLossRatio.toFixed(2)}×`
  return [
    { label: '觀察期間', value: `${model.observationCount} 交易日`, color: '#FFFFFF' }, { label: '例外次數', value: `${model.breachCount} 次`, color: '#FFFFFF' },
    { label: '例外比率', value: `${(model.breachRate * 100).toFixed(1)}%`, color: '#FFFFFF' }, { label: '預期比率', value: `${(model.expectedBreachRate * 100).toFixed(1)}%`, color: '#FFFFFF' },
    { label: 'Kupiec 檢定', value: p(model.kupiecPValue), color: pColor(model.kupiecPValue) }, { label: 'Christoffersen', value: p(model.christoffersenPValue), color: pColor(model.christoffersenPValue) },
    { label: 'ES 尾端樣本', value: `${model.tailObservationCount} 日`, color: '#FFFFFF' }, { label: 'ES 尾端損失比', value: esValue, color: esColor },
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
      color: '#FF6B00',
    },
    {
      label: 'DAILY ES (95%)',
      value: h1 ? `${(h1.historicalES * 100).toFixed(2)}%` : '—',
      sub: esAmount ? `預期損失 ${formatMoney(esAmount)}` : '—',
      color: '#8B1A2B',
    },
    {
      label: 'STRESS VaR',
      value: '-18.52%',
      sub: 'AI泡沫情境（示範資料）',
      color: '#8B1A2B',
    },
    {
      label: 'MAX DRAWDOWN',
      value: r ? `${(r.maxDrawdown * 100).toFixed(2)}%` : '—',
      sub: '歷史最大回撤',
      color: '#666666',
    },
    {
      label: 'SHARPE RATIO',
      value: r ? r.sharpeRatio.toFixed(2) : '—',
      sub: '風險調整後報酬',
      color: '#666666',
    },
    {
      label: 'CONCENTRATION HHI',
      value: r ? r.concentrationHhi.toFixed(4) : '—',
      sub: '持倉集中度',
      color: '#666666',
    },
    {
      label: 'LARGEST HOLDING',
      value: r ? `${(r.largestHoldingWeight * 100).toFixed(2)}%` : '—',
      sub: '最大單一持倉',
      color: '#666666',
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
    { label: '一年正報酬機率', value: `${(data.positiveReturnProbability * 100).toFixed(1)}%`, color: '#34d399' },
    { label: '期望報酬', value: percent(data.expectedReturn), color: data.expectedReturn >= 0 ? '#34d399' : '#f87171' },
    { label: '中位數期末情境', value: percent(medianFinalReturn), color: medianFinalReturn >= 0 ? '#34d399' : '#f87171' },
    { label: '5% 最差期末情境', value: percent(data.p5FinalReturn), color: '#f87171' },
    { label: '1% 最差期末情境', value: percent(data.p1FinalReturn), color: '#f87171' },
    { label: '共同日資料', value: `${data.commonTradingDays} 日`, color: '#FFFFFF' },
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

onMounted(async () => {
  loading.value = true
  try {
    const [portfolio, riskData, riskData99, conservativeData, conservativeData99, backtestDataResponse, ewmaData, ewmaData99, stressData, monteCarloData, monteCarloConservativeData, governanceData, ...curve] = await Promise.all([
      getPortfolio(portfolioId.value),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.95, simulations: 10000, model: 'mvewma_fhs' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.99, simulations: 10000, model: 'mvewma_fhs' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.95, simulations: 10000, model: 'mvewma_fhs_conservative' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.99, simulations: 10000, model: 'mvewma_fhs_conservative' }),
      getPortfolioRiskBacktest(portfolioId.value, backtestFromDate.value, toDate.value),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.95, simulations: 10000, model: 'gbm_ewma_normal' }),
      getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: 0.99, simulations: 10000, model: 'gbm_ewma_normal' }),
      getPortfolioStressTest(portfolioId.value),
      getPortfolioMonteCarlo(portfolioId.value),
      getPortfolioMonteCarlo(portfolioId.value, 'mvewma_fhs_conservative'),
      getPortfolioRiskGovernance(portfolioId.value),
      ...[0.90, 0.95, 0.975, 0.99, 0.995].map(confidenceLevel => getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 1, confidenceLevel, simulations: 5000, model: 'mvewma_fhs' })),
    ])
    portfolioName.value = portfolio.name
    risk.value = riskData
    risk99.value = riskData99
    riskConservative.value = conservativeData
    riskConservative99.value = conservativeData99
    riskEwma.value = ewmaData
    riskEwma99.value = ewmaData99
    stressTest.value = stressData
    monteCarloBase.value = monteCarloData
    monteCarloConservative.value = monteCarloConservativeData
    governance.value = governanceData
    targetWeights.value = Object.fromEntries(riskData.holdings.map(holding => [holding.securityId, Number((holding.weight * 100).toFixed(2))]))
    backtest.value = backtestDataResponse
    riskCurve.value = curve
    try {
      reportSnapshots.value = await getPortfolioRiskReportSnapshots(portfolioId.value)
    } catch {
      reportActionMessage.value = '風險報告快照資料表尚未完成部署；請套用資料庫 migration 後再使用快照功能。'
    }
  } catch (e) {
    error.value = riskErrorMessage(e)
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn kimi-btn-dark" style="margin-bottom: 24px" @click="router.push({ name: 'risk-runs' })">
        ← BACK TO RISK RUNS
      </button>

      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">{{ runInfo.portfolio }}</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">RISK ANALYSIS RUN — {{ runInfo.id }}</span>
        </div>
        <span class="kimi-tag" style="border-color: #34d399; color: #34d399">COMPLETED</span>
      </div>

      <!-- Loading / Error -->
      <ScrollReveal v-if="loading">
        <div class="kimi-section-dark" style="padding: 40px; text-align: center; color: #666666">載入風險分析資料中...</div>
      </ScrollReveal>

      <ScrollReveal v-if="error && !loading">
        <div class="kimi-section-dark" style="padding: 40px; text-align: center; color: #f87171">{{ error }}</div>
      </ScrollReveal>

      <template v-if="!loading && risk">
        <ScrollReveal v-if="governance" style="margin-bottom: 24px">
          <div class="kimi-section-dark" style="padding: 20px; border-left: 3px solid #FACC15">
            <div style="display:flex; justify-content:space-between; gap:12px; align-items:center; margin-bottom:12px"><div><h2 style="font-size:20px; margin:0; color:#FFFFFF">風險治理摘要</h2><span class="kimi-caption">RISK GOVERNANCE · {{ governanceDataStatus(governance.dataStatus) }} · {{ governance.commonTradingDays }} 個共同交易日</span></div><span :style="{ color: governanceAlerts.some(a => a.status === 'critical') ? '#F87171' : '#FACC15' }">{{ governanceAlerts.some(a => a.status === 'critical') ? '需處理' : governanceAlerts.length ? '需注意' : '無門檻警示' }}</span></div>
            <div v-if="governanceAlerts.length" style="display:grid; gap:8px"><div v-for="alert in governanceAlerts" :key="alert.code" :style="{ padding:'10px 12px', background: alert.status === 'critical' ? 'rgba(248,113,113,.10)' : 'rgba(250,204,21,.10)', borderLeft: `3px solid ${alert.status === 'critical' ? '#F87171' : '#FACC15'}` }"><strong :style="{ color: alert.status === 'critical' ? '#FCA5A5' : '#FDE68A' }">{{ governanceLabel(alert.code) }}</strong><span style="margin-left:12px; color:#E2E8F0">目前 {{ alert.code.includes('age') ? `${alert.currentValue} 日` : `${(alert.currentValue * 100).toFixed(1)}%` }} · 注意 {{ alert.code.includes('age') ? `${alert.warningThreshold} 日` : `${(alert.warningThreshold * 100).toFixed(1)}%` }} · 嚴重 {{ alert.code.includes('age') ? `${alert.criticalThreshold} 日` : `${(alert.criticalThreshold * 100).toFixed(1)}%` }}</span><div style="color:#A8B1C1; font-size:12px; margin-top:4px">{{ alert.message }}</div></div></div>
            <p v-else style="margin:0; color:#A8B1C1">目前沒有觸發已設定的集中度與資料品質門檻；這不代表投組沒有風險。</p>
            <div style="margin-top:16px; padding-top:16px; border-top:1px solid #2B2B2B"><button class="kimi-btn kimi-btn-dark" :disabled="reportCreating" @click="createReportSnapshot">{{ reportCreating ? '建立中…' : '建立風險報告快照' }}</button><span v-if="reportActionMessage" style="margin-left:10px; color:#A8B1C1; font-size:12px">{{ reportActionMessage }}</span><div v-if="reportSnapshots.length" style="margin-top:14px; display:grid; gap:6px"><span class="kimi-caption">歷史快照（最新在前）</span><button v-for="report in reportSnapshots" :key="report.id" class="kimi-btn kimi-btn-dark" style="text-align:left; font-size:12px" @click="openReportSnapshot(report.id)"> {{ reportCreatedAt(report.createdAtUtc) }} · {{ report.model }} · {{ reportStatusLabel(report.overallStatus) }} </button></div></div>
            <div style="margin-top:16px; padding-top:16px; border-top:1px solid #2B2B2B"><h3 style="margin:0 0 10px;color:#fff">調整試算</h3><span class="kimi-caption">僅試算，不會修改真實投組；現金為負時代表融資，不含融資利率、保證金與追繳規則。</span><div style="display:grid; gap:7px; margin-top:12px"><label v-for="holding in risk.holdings" :key="holding.securityId" style="display:grid; grid-template-columns:1fr 100px; gap:10px; align-items:center; color:#E2E8F0"><span>{{ holding.ticker }} · 目前 {{ (holding.weight * 100).toFixed(1) }}%</span><input v-model.number="targetWeights[holding.securityId]" type="number" min="0" step="0.1" style="background:#111;color:#fff;border:1px solid #444;padding:7px" /></label></div><div style="margin-top:10px;color:#A8B1C1">目標持股 {{ targetWeightTotal.toFixed(1) }}% · <span :style="{ color: targetCashWeight < 0 ? '#FACC15' : '#A8B1C1' }">{{ targetCashWeight < 0 ? `融資 ${(Math.abs(targetCashWeight) * 100).toFixed(1)}%` : `現金 ${(targetCashWeight * 100).toFixed(1)}%` }}</span></div><p v-if="targetCashWeight < 0" style="margin:6px 0;color:#FACC15;font-size:12px">槓桿試算：融資不作為風險資產加入價格序列。</p><button class="kimi-btn kimi-btn-dark" style="margin-top:10px" :disabled="scenarioLoading" @click="runTargetWeightScenario">{{ scenarioLoading ? '試算中…' : '開始試算' }}</button><button class="kimi-btn kimi-btn-dark" style="margin:10px 0 0 8px" :disabled="scenarioLoading" @click="resetTargetWeights">重設為目前權重</button><div v-if="scenarioMessage" style="margin-top:8px;color:#F87171">{{ scenarioMessage }}</div><div v-if="scenarioResult" style="margin-top:14px; display:grid; grid-template-columns:repeat(4,1fr); gap:8px"><div v-for="metric in [{label:'年化波動率',current:scenarioResult.current.historicalAnnualizedVolatility,scenario:scenarioResult.scenario.historicalAnnualizedVolatility},{label:'VaR 95%',current:scenarioResult.current.horizons[0]?.monteCarloVaR,scenario:scenarioResult.scenario.horizons[0]?.monteCarloVaR},{label:'HHI',current:scenarioResult.current.concentrationHhi,scenario:scenarioResult.scenario.concentrationHhi},{label:'最大持倉',current:scenarioResult.current.largestHoldingWeight,scenario:scenarioResult.scenario.largestHoldingWeight}]" :key="metric.label" style="padding:10px;background:#111"><span class="kimi-caption">{{ metric.label }}</span><strong style="display:block;color:#fff">{{ ((metric.current ?? 0)*100).toFixed(1) }}% → {{ ((metric.scenario ?? 0)*100).toFixed(1) }}%</strong></div></div>
            <div v-if="scenarioResult" style="margin-top:16px;display:grid;grid-template-columns:1fr 1fr;gap:12px"><div style="padding:12px;background:#111"><h4 style="margin:0 0 8px;color:#fff">治理警示變化</h4><div v-for="alert in scenarioResult.scenarioGovernance?.alerts.filter(a => a.status !== 'normal') ?? []" :key="alert.code" :style="{color:alert.status==='critical'?'#F87171':'#FACC15'}">{{ governanceLabel(alert.code) }} · {{ scenarioAlertValue(alert) }} · {{ alert.status==='critical'?'嚴重':'注意' }}</div></div><div style="padding:12px;background:#111"><h4 style="margin:0 0 8px;color:#fff">壓力情境變化</h4><div v-for="stress in scenarioResult.scenarioStress" :key="stress.id" style="color:#E2E8F0">{{ stress.name }} · {{ ((stress.totalImpact-(scenarioResult.currentStress.find(x=>x.id===stress.id)?.totalImpact??0))*100).toFixed(1) }} 個百分點</div></div></div>
            </div>
          </div>
        </ScrollReveal>
        <ScrollReveal v-if="selectedReportSnapshot" style="margin-bottom:24px"><section class="kimi-section-dark" style="padding:20px"><div style="display:flex; justify-content:space-between; gap:12px; align-items:center"><div><h2 style="font-size:20px; margin:0; color:#FFFFFF">風險報告快照</h2><span class="kimi-caption">{{ reportCreatedAt(selectedReportSnapshot.createdAtUtc) }} · 資料截止 {{ selectedReportSnapshot.dataAsOfDate ?? '—' }} · {{ selectedReportSnapshot.model }} · {{ selectedReportSnapshot.thresholdVersion }}</span></div><button class="kimi-btn kimi-btn-dark" @click="printReportSnapshot">列印／另存 PDF</button></div><p style="color:#A8B1C1; font-size:12px; margin:14px 0">此內容從建立當下的資料庫快照讀取，市場資料後續更新不會改寫它。</p><pre style="margin:0; padding:14px; overflow:auto; max-height:420px; background:#0B0B0B; color:#CBD5E1; font-size:12px; line-height:1.5">{{ JSON.stringify(selectedReportSnapshot.snapshot, null, 2) }}</pre></section></ScrollReveal>
        <!-- Run Info -->
        <ScrollReveal>
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div style="display: grid; grid-template-columns: repeat(2, 1fr)">
              <div
                v-for="(item, i) in [
                  { label: 'MODEL', value: runInfo.model },
                  { label: 'CONFIDENCE', value: runInfo.confidence },
                  { label: 'LOOKBACK', value: runInfo.lookback },
                  { label: 'DATE RANGE', value: `${fromDate} ~ ${toDate}` },
                ]"
                :key="i"
                style="padding: 20px; border-right: 1px solid #333333; border-bottom: 1px solid #333333"
              >
                <span class="kimi-caption" style="margin-bottom: 4px; display: block; color: #666666">{{ item.label }}</span>
                <span style="font-size: 16px; font-weight: 600; color: #FFFFFF">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Model Metadata -->
        <ScrollReveal :delay="0.05">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <h3 style="margin: 0 0 20px; font-size: 16px; font-weight: 600; color: #FFFFFF">模型細節</h3>
            <div style="display: grid; grid-template-columns: repeat(4, 1fr)">
              <div
                v-for="(item, i) in [
                  { label: 'COVARIANCE METHOD', value: risk.covarianceMethod ?? '—' },
                  { label: 'RESIDUAL SAMPLING', value: risk.residualSampling ?? '—' },
                  { label: 'COMMON TRADING DAYS', value: risk.commonTradingDays ? `${risk.commonTradingDays} 天` : '—' },
                  { label: 'SHRINKAGE ALPHA', value: risk.shrinkageAlpha != null ? `${(risk.shrinkageAlpha * 100).toFixed(2)}%` : '—' },
                ]"
                :key="i"
                style="padding: 20px; border-right: 1px solid #333333"
              >
                <span class="kimi-caption" style="margin-bottom: 4px; display: block; color: #666666">{{ item.label }}</span>
                <span style="font-size: 16px; font-weight: 600; color: #FFFFFF">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- KPI Cards -->
        <ScrollReveal :delay="0.1">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div class="kimi-kpi-grid-5">
              <div
                v-for="(kpi, i) in riskKPIData"
                :key="i"
                class="kimi-kpi-cell kimim-kpi-cell-dark"
                :class="{ 'kimi-kpi-cell-dark': true }"
              >
                <span class="kimi-caption" style="margin-bottom: 8px; display: block">{{ kpi.label }}</span>
                <span class="kimi-data" :style="{ color: kpi.color }">{{ kpi.value }}</span>
                <span style="font-size: 11px; color: #666666; margin-top: 4px; display: block">{{ kpi.sub }}</span>
                <div class="accent-bar" style="background-color: #8B1A2B" />
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Core Metrics -->
        <ScrollReveal :delay="0.15">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div class="kimi-kpi-grid">
              <div
                v-for="(m, i) in metrics"
                :key="i"
                class="kimi-kpi-cell kimi-kpi-cell-dark"
              >
                <span class="kimi-caption" style="color: #666666; margin-bottom: 8px; display: block">{{ m.label }}</span>
                <span class="kimi-data" style="color: #FFFFFF">{{ m.value }}</span>
                <span style="font-size: 12px; color: #666666; margin-top: 4px; display: block">{{ m.sub }}</span>
                <div class="accent-bar" style="background-color: #8B1A2B" />
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- VaR Results Table -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">Value at Risk 分析</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">VALUE AT RISK — 95% & 99% 信賴區間</span>
            </div>
            <div style="padding: 20px">
              <div style="display: grid; gap: 32px">
                <div>
                  <table                   class="kimi-table kimi-table-dark" style="border-collapse: collapse">
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
                        <td style="color: #FFFFFF">{{ v.method }}</td>
                        <td class="kimi-font-mono" style="text-align: right; color: v.var95 < -0.048 ? '#FF6B00' : '#FFFFFF'">{{ (v.var95 * 100).toFixed(2) }}%</td>
                        <td class="kimi-font-mono" style="text-align: right; color: '#8B1A2B'">{{ (v.var99 * 100).toFixed(2) }}%</td>
                        <td style="color: #666666; padding-left: 16px">{{ v.note }}</td>
                      </tr>
                    </tbody>
                  </table>
                  <p style="font-size: 11px; color: #666666; margin-top: 12px">
                    所有方法皆使用正式 API 資料；保守 FHS 僅將標準化殘差向量的最極端 1% 徑向截尾，原始 FHS 仍為預設模型。
                  </p>
                </div>

                <div>
                  <h3 style="font-size: 14px; font-weight: 500; margin: 0 0 12px; color: #FFFFFF">日報酬分布直方圖（共同日資料）</h3>
                  <svg width="100%" height="270" viewBox="0 0 400 270" style="display: block; width: min(100%, 760px)">
                    <line v-for="value in histogramChart.ticks" :key="'g-' + value" x1="50" :y1="histogramChart.y(value)" x2="380" :y2="histogramChart.y(value)" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                    <text v-for="value in histogramChart.ticks" :key="'gy-' + value" x="45" :y="histogramChart.y(value) + 4" text-anchor="end" fill="#666666" font-size="10">{{ Math.round(value) }}</text>

                    <g v-for="(bin, i) in officialHistogram" :key="i">
                      <rect
                        :x="50 + i * 23"
                        :y="histogramChart.y(bin.count)"
                        width="19"
                        :height="histogramChart.height(bin.count)"
                        :fill="bin.isTail99 ? '#8B1A2B' : bin.isTail95 ? '#FF6B00' : '#333333'"
                        class="transition-all"
                      />
                      <text :x="50 + i * 23 + 9.5" y="228" text-anchor="middle" fill="#666666" font-size="7">{{ bin.bin }}</text>
                    </g>

                    <g transform="translate(55, 12)">
                      <rect x="0" y="-6" width="10" height="8" fill="#333333" />
                      <text x="14" y="0" fill="#666666" font-size="9">正常區間</text>
                      <rect x="70" y="-6" width="10" height="8" fill="#FF6B00" />
                      <text x="84" y="0" fill="#666666" font-size="9">95% 尾部</text>
                      <rect x="145" y="-6" width="10" height="8" fill="#8B1A2B" />
                      <text x="159" y="0" fill="#666666" font-size="9">99% 尾部</text>
                    </g>
                  </svg>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- ES / CVaR Analysis -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">Expected Shortfall (ES) 分析</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">CONDITIONAL VaR — 尾部損失期望值</span>
            </div>
            <div style="padding: 20px">
              <div style="display: grid; gap: 32px">
                <div>
                  <table class="kimi-table kimi-table-dark">
                    <thead>
                      <tr>
                        <th style="text-align: left">計算方法</th>
                        <th style="text-align: right">ES 95%</th>
                        <th style="text-align: right">ES 99%</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(e, i) in esTableRows" :key="i">
                        <td style="color: #FFFFFF">{{ e.method }}</td>
                        <td class="kimi-font-mono" style="text-align: right; color: '#FF6B00'">{{ (e.es95 * 100).toFixed(2) }}%</td>
                        <td class="kimi-font-mono" style="text-align: right; color: '#8B1A2B'">{{ (e.es99 * 100).toFixed(2) }}%</td>
                      </tr>
                    </tbody>
                  </table>
                  <div class="kimi-panel-dark" style="margin-top: 16px">
                    <p style="font-size: 12px; color: #666666; margin: 0">
                      ES（Expected Shortfall）衡量當損失超過 VaR 閾值時的
                      <strong style="color: #FFFFFF">平均損失程度</strong>。
                      相較於 VaR 僅反映單一分位數點，ES 更完整地捕捉尾部風險。
                    </p>
                  </div>
                </div>

                <div>
                  <h3 style="font-size: 14px; font-weight: 500; margin: 0 0 12px; color: #FFFFFF">VaR vs ES 比較（MVEWMA-FHS）</h3>
                  <svg width="100%" height="340" viewBox="0 0 400 340" style="display: block; width: min(100%, 760px); overflow: hidden">
                    <line v-for="value in varEsChart.ticks" :key="'g-' + value" x1="80" :y1="varEsChart.y(value)" x2="380" :y2="varEsChart.y(value)" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                    <text v-for="value in varEsChart.ticks" :key="'y-' + value" x="75" :y="varEsChart.y(value) + 4" text-anchor="end" fill="#666666" font-size="10">{{ value.toFixed(0) }}%</text>

                    <g v-for="(d, i) in officialVarEsComparison" :key="i">
                      <rect :x="85 + i * 58" :y="varEsChart.y(0)" width="22" :height="varEsChart.height(d.var)" fill="#FF6B00" opacity="0.8" />
                      <rect :x="110 + i * 58" :y="varEsChart.y(0)" width="22" :height="varEsChart.height(d.es)" fill="#8B1A2B" opacity="0.8" />
                      <text :x="85 + i * 58 + 22" y="255" text-anchor="middle" fill="#666666" font-size="9">{{ d.confidence }}</text>
                    </g>

                    <g transform="translate(90, 285)">
                      <rect x="0" y="-6" width="12" height="8" fill="#FF6B00" opacity="0.8" />
                      <text x="16" y="0" fill="#666666" font-size="9">VaR</text>
                      <rect x="50" y="-6" width="12" height="8" fill="#8B1A2B" opacity="0.8" />
                      <text x="66" y="0" fill="#666666" font-size="9">ES</text>
                    </g>
                  </svg>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Stress Test Scenarios -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">壓力測試情境</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">STRESS TESTING — 六大極端情境模擬</span>
            </div>
            <div style="padding: 20px">
              <div class="kimi-grid-3">
                <div
                  v-for="s in formalStressScenarios"
                  :key="s.id"
                  class="kimi-panel-dark"
                  style="cursor: pointer; transition: all 0.2s ease"
                  :style="{ borderColor: selectedScenario === s.id ? '#8B1A2B' : '#333333', backgroundColor: selectedScenario === s.id ? '#111111' : '#0A0A0A' }"
                  @click="selectedScenario = selectedScenario === s.id ? null : s.id"
                >
                  <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
                    <span style="font-size: 14px; font-weight: 500; color: #FFFFFF">{{ s.name }}</span>
                    <span class="kimi-tag" :style="{ borderColor: s.type === 'historical' ? '#FF6B00' : '#8B1A2B', color: s.type === 'historical' ? '#FF6B00' : '#8B1A2B' }">{{ s.type === 'historical' ? '歷史回放' : '假設情境' }}</span>
                  </div>

                  <p style="font-size: 12px; color: #666666; margin: 0 0 12px">{{ s.methodology }}</p>

                  <div style="display: flex; align-items: baseline; gap: 8px">
                    <span style="font-size: 22px; font-weight: 600; color: #8B1A2B">{{ (s.totalImpact * 100).toFixed(1) }}%</span>
                    <span class="kimi-caption">投組衝擊</span>
                  </div>

                  <div v-if="selectedScenario === s.id" style="margin-top: 16px; padding-top: 16px; border-top: 1px solid #333333">
                    <div v-for="h in s.holdings" :key="h.ticker" style="display: flex; justify-content: space-between; align-items: center; padding: 6px 0">
                      <span style="font-size: 12px; color: #666666">{{ h.ticker }} · {{ h.industry }}</span>
                      <span style="font-size: 12px; color: #FFFFFF; font-family: var(--kimi-font-mono)">{{ (h.contribution * 100).toFixed(1) }}%</span>
                    </div>
                    <div style="margin-top: 12px; display: flex; flex-wrap: wrap; gap: 6px">
                      <span v-for="industry in s.industries" :key="industry.industry" class="kimi-tag" style="border-color: #333333; color: #999999">{{ industry.industry }} {{ (industry.contribution * 100).toFixed(1) }}%</span>
                    </div>
                  </div>
                </div>
              </div>
              <div v-if="selectedStressScenario" class="kimi-panel-dark" style="margin-top: 20px">
                <h3 style="margin: 0 0 6px; color: #FFFFFF">持股壓力明細 — {{ selectedStressScenario.name }}</h3>
                <p style="font-size: 12px; color: #666666; margin: 0 0 14px">{{ selectedStressScenario.methodology }}</p>
                <table class="kimi-table kimi-table-dark" style="width: 100%">
                  <thead><tr><th>代號／名稱</th><th>產業</th><th style="text-align:right">當前權重</th><th style="text-align:right">情境起始價</th><th style="text-align:right">情境結束價</th><th style="text-align:right">情境期間報酬</th><th style="text-align:right">對投組衝擊貢獻</th></tr></thead>
                  <tbody><tr v-for="h in selectedStressScenario.holdings.slice().sort((a,b)=>a.contribution-b.contribution)" :key="h.ticker"><td>{{ h.ticker }} · {{ h.securityName }}</td><td>{{ h.industry }}</td><td style="text-align:right">{{ (h.weight*100).toFixed(1) }}%</td><td style="text-align:right">{{ h.basePrice.toFixed(2) }}</td><td style="text-align:right">{{ h.stressedPrice.toFixed(2) }}</td><td style="text-align:right">{{ (h.shock*100).toFixed(1) }}%</td><td style="text-align:right">{{ (h.contribution*100).toFixed(1) }}%</td></tr></tbody>
                </table>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Backtest Results -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">VaR 回測驗證</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">BACKTESTING — 252 日滾動 VaR / ES</span>
            </div>
            <div style="padding: 20px">
              <div style="display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 20px">
                <button v-for="model in backtestModels" :key="model" class="kimi-btn kimi-btn-dark" :style="{ borderColor: selectedBacktestModel === model ? '#FF6B00' : '#333333', color: selectedBacktestModel === model ? '#FF6B00' : '#999999' }" @click="selectedBacktestModel = model">{{ model }}</button>
                <button v-for="confidence in backtestConfidences" :key="confidence" class="kimi-btn kimi-btn-dark" :style="{ borderColor: selectedBacktestConfidence === confidence ? '#FF6B00' : '#333333', color: selectedBacktestConfidence === confidence ? '#FF6B00' : '#999999' }" @click="selectedBacktestConfidence = confidence">{{ (confidence * 100).toFixed(0) }}%</button>
              </div>
              <div class="kimi-grid-2" style="gap: 32px">
                <svg width="100%" height="280" viewBox="0 0 500 280">
                  <line v-for="value in backtestChart.ticks" :key="'g-' + value" x1="50" :y1="backtestChart.y(value)" x2="480" :y2="backtestChart.y(value)" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                  <text v-for="value in backtestChart.ticks" :key="value" x="45" :y="backtestChart.y(value) + 4" text-anchor="end" fill="#666666" font-size="10">{{ value }}%</text>

                  <line x1="50" :y1="backtestChart.y(0)" x2="480" :y2="backtestChart.y(0)" stroke="#666666" stroke-width="2" />

                  <line x1="50" :y1="backtestChart.y(backtestVaRLine)" x2="480" :y2="backtestChart.y(backtestVaRLine)" stroke="#FF6B00" stroke-width="1" stroke-dasharray="4 4" />
                  <text x="485" :y="backtestChart.y(backtestVaRLine) + 3" fill="#FF6B00" font-size="9">VaR {{ (selectedBacktestConfidence * 100).toFixed(0) }}%</text>

                  <g v-for="(d, i) in backtestData" :key="i">
                    <line
                      :x1="d.x"
                      :y1="backtestChart.y(0)"
                      :x2="d.x"
                      :y2="backtestChart.y(d.actual)"
                      :stroke="d.breached ? '#8B1A2B' : d.actual >= 0 ? '#FFFFFF' : '#666666'"
                      stroke-width="4"
                    />
                    <text v-if="d.breached" :x="d.x" :y="backtestChart.y(d.actual) - 6" text-anchor="middle" fill="#8B1A2B" font-size="8">!</text>
                  </g>

                  <template v-for="(d, i) in backtestData" :key="'xl-' + i">
                    <text v-if="i % 5 === 0 || i === backtestData.length - 1" :x="d.x" y="260" text-anchor="middle" fill="#666666" font-size="8">{{ d.date.slice(5) }}</text>
                  </template>
                </svg>

                <div style="display: flex; flex-direction: column; justify-content: center">
                  <div class="kimi-panel-dark" style="margin-bottom: 16px">
                    <div class="kimi-grid-2" style="gap: 16px">
                      <div v-for="stat in backtestStats" :key="stat.label"
                      >
                        <span class="kimi-caption" style="display: block; margin-bottom: 4px">{{ stat.label }}</span>
                        <span style="font-size: 18px; font-weight: 600" :style="{ color: stat.color }">{{ stat.value }}</span>
                      </div>
                    </div>
                  </div>

                  <p style="font-size: 12px; color: #666666; margin: 0">
                    使用目前持倉權重回放近三年共同日價格；圖表顯示最近 60 個有效回測日。可切換正式 Historical 與 MVEWMA-FHS 的 95%／99% 回測結果。
                  </p>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Monte Carlo Simulation -->
        <ScrollReveal class="mt-20" style="margin-bottom: 80px">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">蒙地卡羅模擬</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">{{ monteCarlo?.model ?? 'MVEWMA-FHS' }} — {{ monteCarlo?.simulations?.toLocaleString() ?? '10,000' }} 次、{{ monteCarlo?.horizonDays ?? 252 }} 個交易日路徑模擬</span>
            </div>
            <div style="padding: 20px">
              <div style="display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 16px">
                <button v-for="choice in [{ id: 'base', label: '原始 FHS' }, { id: 'conservative', label: '保守 FHS（p99）' }]" :key="choice.id" class="kimi-btn kimi-btn-dark" :style="{ borderColor: selectedMonteCarloModel === choice.id ? '#FF6B00' : '#333333', color: selectedMonteCarloModel === choice.id ? '#FF6B00' : '#999999' }" @click="selectedMonteCarloModel = choice.id as 'base' | 'conservative'">{{ choice.label }}</button>
              </div>
              <template v-if="monteCarlo?.status === 'ready'">
              <svg width="100%" height="400" viewBox="0 0 900 400" preserveAspectRatio="xMidYMid meet">
                <line v-for="tick in monteCarloChart.ticks" :key="'g-' + tick" x1="70" :y1="monteCarloChart.y(tick)" x2="850" :y2="monteCarloChart.y(tick)" :stroke="tick === 0 ? '#E2E8F0' : '#4B5563'" :stroke-width="tick === 0 ? 1.5 : 1" :stroke-dasharray="tick === 0 ? 'none' : '4 4'" />
                <text
                  v-for="tick in monteCarloChart.ticks"
                  :key="'t-' + tick"
                  x="65"
                  :y="monteCarloChart.y(tick) + 4"
                  text-anchor="end"
                  fill="#A8B1C1"
                  font-size="10"
                >{{ `${tick > 0 ? '+' : ''}${(tick * 100).toFixed(0)}%` }}</text>

                <!-- 98% band first, then the narrower 90% band so both remain distinguishable. -->
                <polygon :points="monteCarloChart.p1" fill="#2563EB" fill-opacity="0.26" stroke="#60A5FA" stroke-opacity="0.72" stroke-width="0.8" />
                <polygon :points="monteCarloChart.p5" fill="#F97316" fill-opacity="0.30" stroke="#FDBA74" stroke-opacity="0.82" stroke-width="0.8" />

                <polyline :points="monteCarloChart.p50" fill="none" stroke="#FFFFFF" stroke-width="2" />

                <polyline
                  v-for="path in monteCarlo.samplePaths"
                  :key="path.pathIndex"
                  :points="path.cumulativeReturns.map((value, day) => `${monteCarloChart.x(day)},${monteCarloChart.y(value)}`).join(' ')"
                  fill="none"
                  :stroke="['#94A3B8', '#A78BFA', '#38BDF8', '#FBBF24'][(path.pathIndex - 1) % 4]"
                  stroke-width="1"
                  opacity="0.58"
                />

                <g transform="translate(80, 18)">
                  <line x1="0" y1="0" x2="20" y2="0" stroke="#FFFFFF" stroke-width="2" />
                  <text x="25" y="4" fill="#FFFFFF" font-size="10">中位數路徑</text>
                  <rect x="100" y="-6" width="16" height="10" fill="#2563EB" fill-opacity="0.8" />
                  <text x="120" y="4" fill="#BFDBFE" font-size="10">98% 區間</text>
                  <rect x="180" y="-6" width="16" height="10" fill="#F97316" fill-opacity="0.9" />
                  <text x="200" y="4" fill="#FED7AA" font-size="10">90% 區間</text>
                </g>
              </svg>
              <div class="kimi-kpi-grid-5" style="margin-top: 20px">
                <div v-for="stat in monteCarloStats" :key="stat.label" class="kimi-kpi-cell kimi-kpi-cell-dark">
                  <span class="kimi-caption" style="display: block; margin-bottom: 4px">{{ stat.label }}</span>
                  <span style="font-size: 18px; font-weight: 600; font-family: var(--kimi-font-mono)" :style="{ color: stat.color }">{{ stat.value }}</span>
                </div>
              </div>
              <div style="margin-top: 20px; overflow-x: auto">
                <div class="kimi-caption" style="margin-bottom: 8px">MODEL COMPARISON — 原始模型為預設，保守版僅供比較</div>
                <table class="kimi-table kimi-table-dark" style="min-width: 760px">
                  <thead><tr><th>模型</th><th>正報酬機率</th><th>中位數</th><th>期望</th><th>5% 情境</th><th>1% 情境</th><th>殘差截尾比例</th></tr></thead>
                  <tbody><tr v-for="item in monteCarloComparison" :key="item.label"><td style="color: #FFFFFF">{{ item.label }}</td><td>{{ item.positive }}</td><td>{{ item.median }}</td><td>{{ item.expected }}</td><td style="color: #FCA5A5">{{ item.p5 }}</td><td style="color: #F87171">{{ item.p1 }}</td><td>{{ item.capped }}</td></tr></tbody>
                </table>
              </div>
              <div class="kimi-panel-dark" style="margin-top: 20px; padding: 16px">
                <div style="display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 12px">
                  <span style="font-size: 15px; font-weight: 600; color: #FFFFFF">模型診斷</span>
                  <span class="kimi-caption">MODEL DIAGNOSTICS</span>
                </div>
                <div v-if="monteCarlo.diagnostics.rightSkewWarning" style="margin-bottom: 14px; padding: 12px; border-left: 3px solid #FACC15; background: rgba(250, 204, 21, 0.10); color: #FDE68A; font-size: 13px">
                  {{ monteCarlo.diagnostics.rightSkewMessage }}
                </div>
                <p v-else style="margin: 0 0 14px; color: #A8B1C1; font-size: 13px">期望值與中位數差距未達 25 個百分點右偏警示門檻；仍請一併參考下行情境。</p>
                <div class="kimi-grid-2" style="gap: 12px 24px">
                  <div v-for="item in monteCarloDiagnostics" :key="item.label" style="display: flex; justify-content: space-between; gap: 12px; border-bottom: 1px solid #2B2B2B; padding-bottom: 8px">
                    <span class="kimi-caption">{{ item.label }}</span>
                    <span class="kimi-font-mono" style="color: #E2E8F0">{{ item.value }}</span>
                  </div>
                </div>
              </div>
              <p style="font-size: 12px; color: #666666; margin: 16px 0 0">以目前持倉權重與共同日價格資料推演未來報酬分布；區間不代表發生機率保證。</p>
              </template>
              <div v-else class="kimi-panel-dark" style="color: #999999">
                {{ monteCarlo?.message ?? '蒙地卡羅路徑資料載入中。' }}
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Holdings Risk Contribution -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">持倉風險摘要</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">HOLDING RISK SUMMARY — 後端資料</span>
            </div>
            <div style="padding: 20px; overflow-x: auto">
              <div style="margin-bottom: 16px; color: #FFFFFF; font-size: 14px">
                風險來源模型年化波動率：<span class="kimi-font-mono" style="color: #FF6B00">{{ (risk.riskSourceAnnualizedVolatility * 100).toFixed(2) }}%</span>
              </div>
              <table class="kimi-table kimi-table-dark">
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
                    <td style="color: #FFFFFF">{{ h.ticker }}</td>
                    <td style="color: #FFFFFF">{{ h.securityName }}</td>
                    <td style="color: #666666">{{ h.industry }}</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #FFFFFF">{{ (h.weight * 100).toFixed(2) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: h.annualizedVolatility > 0.3 ? '#FF6B00' : '#FFFFFF'">{{ (h.annualizedVolatility * 100).toFixed(2) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #FFFFFF">{{ (h.componentVolatility * 100).toFixed(2) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: h.componentRiskShare < 0 ? '#34d399' : '#FF6B00'">{{ (h.componentRiskShare * 100).toFixed(1) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #FFFFFF">{{ h.marginalVolatility.toFixed(2) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #FFFFFF">{{ (h.incrementalVolatility * 100).toFixed(2) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #FFFFFF">{{ h.dataPointCount }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p style="padding: 0 20px 20px; margin: 0; font-size: 11px; color: #666666">
              Component 可加總為上方的風險來源模型年化波動率；Marginal 為權重增加 1 個百分點的年化波動率變化；Incremental 假設移除部位後轉為現金。負值代表分散效果。
            </p>
          </div>
        </ScrollReveal>

        <ScrollReveal class="mt-20" v-if="risk.industries?.length">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">產業風險來源</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">INDUSTRY RISK SOURCES — 後端資料</span>
            </div>
            <div style="padding: 20px; overflow-x: auto">
              <table class="kimi-table kimi-table-dark">
                <thead><tr><th>產業</th><th style="text-align: right">持倉數</th><th style="text-align: right">權重</th><th style="text-align: right">Component</th><th style="text-align: right">風險占比</th><th style="text-align: right">Marginal（+1%）</th><th style="text-align: right">Incremental</th></tr></thead>
                <tbody>
                  <tr v-for="industry in risk.industries" :key="industry.industry">
                    <td style="color: #FFFFFF">{{ industry.industry }}</td><td class="kimi-font-mono" style="text-align: right">{{ industry.holdingCount }}</td><td class="kimi-font-mono" style="text-align: right">{{ (industry.weight * 100).toFixed(2) }}%</td><td class="kimi-font-mono" style="text-align: right">{{ (industry.componentVolatility * 100).toFixed(2) }}%</td><td class="kimi-font-mono" style="text-align: right" :style="{ color: industry.componentRiskShare < 0 ? '#34d399' : '#FF6B00' }">{{ (industry.componentRiskShare * 100).toFixed(1) }}%</td><td class="kimi-font-mono" style="text-align: right">{{ industry.marginalVolatility.toFixed(2) }}%</td><td class="kimi-font-mono" style="text-align: right">{{ (industry.incrementalVolatility * 100).toFixed(2) }}%</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </ScrollReveal>
      </template>

      <div style="height: 80px" />
    </div>

    <Footer :dark="true" label="RISK" />
  </div>
</template>

<style scoped>
.mt-20 {
  margin-top: 40px;
}

.transition-all {
  transition: all 0.3s ease;
}

.kimi-mock-label {
  display: inline-block;
  padding: 2px 8px;
  font-size: 10px;
  font-weight: 500;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  border: 1px solid #8B1A2B;
  color: #8B1A2B;
  margin-bottom: 8px;
}

@media (max-width: 1024px) {
  .kimi-grid-2 > * {
    border-right: none !important;
  }
}
</style>
