<script setup lang="ts">
import RiskMeasurementBasis from '../../components/agents/RiskMeasurementBasis.vue'
import { onBeforeUnmount, onMounted, ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import RiskSources from './RiskSources.vue'
import RiskQualitySummary from './RiskQualitySummary.vue'
import RiskSnapshotReport from './RiskSnapshotReport.vue'
import { riskPercent, qualityStatusLabel, qualityCodeLabel } from './riskPresentation'
import Footer from '../../components/kimi/Footer.vue'
import {
  getPortfolio,
  getPortfolioRisk,
  createRiskCalculation,
  getRiskCalculation,
  validateRiskCalculation,
  listRiskCalculations,
  getPortfolioRiskBacktestRun,
  getPortfolioMonteCarlo,
  getPortfolioRiskGovernance,
  calculatePortfolioRiskScenario,
  createPortfolioRiskReportSnapshot,
  getPortfolioRiskReportSnapshot,
  getPortfolioRiskReportSnapshots,
  getPortfolioStressTest,
  getPortfolioValuation,
  type PortfolioRiskGovernanceResponse,
  type PortfolioRiskScenarioResponse,
  type PortfolioRiskReportSnapshotDetail,
  type PortfolioRiskReportSnapshotListItem,
  type PortfolioMonteCarloResponse,
  type PortfolioRiskBacktestResponse,
  type PortfolioRiskBacktestRun,
  type PortfolioRiskResponse,
  type RiskCalculationRun,
  type VtGarchRiskResult,
  type FitHealth,
  type NormalizedHorizon,
} from '../../services/risk.ts'

const router = useRouter()
const route = useRoute()
const portfolioId = computed(() => String(route.params.id))

const portfolioName = ref('投資組合風險分析')
const risk = ref<PortfolioRiskResponse | null>(null)
const loading = ref(false)
const calculationRun = ref<RiskCalculationRun | null>(null)
const qualityValidationLoading = ref(false)
const portfolioTotalValue = ref<number | null>(null)
const portfolioCurrency = ref('TWD')
const risk99 = ref<PortfolioRiskResponse | null>(null)
const riskEwma = ref<PortfolioRiskResponse | null>(null)
const riskEwma99 = ref<PortfolioRiskResponse | null>(null)
const riskCurve = ref<PortfolioRiskResponse[]>([])
const backtest = ref<PortfolioRiskBacktestResponse | null>(null)
const activeBacktestRun = ref<PortfolioRiskBacktestRun | null>(null)
const monteCarloBase = ref<PortfolioMonteCarloResponse | null>(null)
const monteCarloRun = ref<RiskCalculationRun | null>(null)
const governance = ref<PortfolioRiskGovernanceResponse | null>(null)
const targetWeights = ref<Record<string, number>>({})
const scenarioResult = ref<PortfolioRiskScenarioResponse | null>(null)
const scenarioLoading = ref(false)
const scenarioMessage = ref('')
const reportSnapshots = ref<PortfolioRiskReportSnapshotListItem[]>([])
const selectedReportSnapshot = ref<PortfolioRiskReportSnapshotDetail | null>(null)
const reportActionMessage = ref('')
const reportCreating = ref(false)
const monteCarlo = computed<PortfolioMonteCarloResponse | null>(() => {
  if (isVtGarch.value) {
    const run = monteCarloRun.value
    const vt = run?.result as VtGarchRiskResult | null
    if (!run || run.status !== 'Completed' || !vt?.bands?.length) return null
    return {
      portfolioId: run.portfolioId,
      status: 'ready',
      message: null,
      dataAsOfDate: vt.dataAsOfDate ?? null,
      commonTradingDays: vt.lookbackDays ?? 0,
      horizonDays: 252,
      simulations: vt.simulations,
      model: run.selectedModel ?? vt.selectedModel,
      ewmaLambda: 0,
      shrinkageAlpha: 0,
      residualCapQuantile: 0,
      cappedDrawRate: 0,
      bands: vt.bands,
      samplePaths: (vt.samplePaths ?? []).map((path, index) => ({ pathIndex: index + 1, cumulativeReturns: path })),
      positiveReturnProbability: vt.summary?.positiveReturnProbability ?? 0,
      expectedReturn: vt.summary?.expectedReturn ?? 0,
      p5FinalReturn: vt.summary?.p5FinalReturn ?? 0,
      p1FinalReturn: vt.summary?.p1FinalReturn ?? 0,
      diagnostics: null as unknown as PortfolioMonteCarloResponse['diagnostics'],
    }
  }
  return monteCarloBase.value
})
const stressTest = ref<Awaited<ReturnType<typeof getPortfolioStressTest>> | null>(null)
const selectedBacktestModel = ref('Historical')
const selectedBacktestConfidence = ref<0.95 | 0.99>(0.95)
const backtestModels = computed(() => {
  const names = backtest.value?.models.map(model => model.model) ?? ['Historical', 'VT-GARCH-t + Joint-Vector FHS']
  return [...new Set(names.filter(name => !name.includes('保守')))]
})
const backtestConfidences: Array<0.95 | 0.99> = [0.95, 0.99]
const error = ref('')

const isVtGarch = computed(() => calculationRun.value?.selectedModel === 'VT-GARCH-t + Joint-Vector FHS')
const isFallback = computed(() => (calculationRun.value?.fallbackDepth ?? 0) > 0)
const vtGarchResult = computed<VtGarchRiskResult | null>(() => {
  if (!isVtGarch.value || !calculationRun.value?.result) return null
  return calculationRun.value.result as VtGarchRiskResult
})
const fallbackResult = computed<PortfolioRiskResponse | null>(() => {
  if (isVtGarch.value || !calculationRun.value?.result) return null
  return calculationRun.value.result as PortfolioRiskResponse
})
const fitHealth = computed<FitHealth | null>(() => vtGarchResult.value?.fitHealth ?? null)
const calculationDataAsOf = computed(() =>
  vtGarchResult.value?.dataAsOfDate ?? fallbackResult.value?.dataAsOfDate ?? null)
const calculationSimulations = computed(() =>
  vtGarchResult.value?.simulations ?? fallbackResult.value?.simulations ?? null)
const calculationLookback = computed(() =>
  vtGarchResult.value?.lookbackDays ?? fallbackResult.value?.alignedReturnCount ?? null)

const normalizedHorizons = computed<Array<Omit<NormalizedHorizon, 'var95' | 'es95'> & { var95: number | null; es95: number | null }>>(() => {
  const vt = vtGarchResult.value
  if (vt) {
    return vt.horizons.map(h => ({
      horizonDays: h.horizonDays,
      var95: h.confidenceLevels.find(c => c.confidenceLevel === 0.95)?.var ?? null,
      es95: h.confidenceLevels.find(c => c.confidenceLevel === 0.95)?.expectedShortfall ?? null,
      var99: h.confidenceLevels.find(c => c.confidenceLevel === 0.99)?.var ?? null,
      es99: h.confidenceLevels.find(c => c.confidenceLevel === 0.99)?.expectedShortfall ?? null,
      expectedReturn: h.expectedReturn,
    }))
  }
  const fb = fallbackResult.value
  if (fb) {
    return fb.horizons.map(h => {
      return {
        horizonDays: h.horizonDays,
        var95: h.monteCarloVaR,
        es95: h.monteCarloES,
        var99: null,
        es99: null,
        expectedReturn: null,
      }
    })
  }
  return []
})
const maxDrawdown = computed(() =>
  vtGarchResult.value?.maxDrawdown ?? fallbackResult.value?.maxDrawdown ?? null)
const totalMarketValue = computed(() =>
  portfolioTotalValue.value ?? fallbackResult.value?.totalMarketValue ?? 0)
const baseCurrency = computed(() => fallbackResult.value?.baseCurrency ?? portfolioCurrency.value)
const hasHoldingsData = computed(() => !!fallbackResult.value?.holdings?.length)
const holdingsRisk = computed(() => fallbackResult.value?.holdings ?? [])
const dailyLogReturns = computed(() => vtGarchResult.value?.dailyLogReturns ?? fallbackResult.value?.dailyLogReturns ?? [])
const garchOneDayComparison = computed(() => {
  const hist = vtGarchResult.value?.historical
  const g1 = normalizedHorizons.value.find(h => h.horizonDays === 1)
  if (!hist || !g1) return []
  return [
    { method: '歷史模擬法', var95: hist.var95, var99: hist.var99, es95: hist.es95, es99: hist.es99 },
    { method: 'VT-GARCH-t + Joint-Vector FHS', var95: g1.var95, var99: g1.var99, es95: g1.es95, es99: g1.es99 },
  ]
})

type DeferredLoadState = 'idle' | 'loading' | 'ready' | 'error'
const modelComparisonState = ref<DeferredLoadState>('idle')
const stressTestState = ref<DeferredLoadState>('idle')
const backtestState = ref<DeferredLoadState>('idle')
const monteCarloState = ref<DeferredLoadState>('idle')
const modelComparisonError = ref('')
const stressTestError = ref('')
const backtestError = ref('')
const monteCarloError = ref('')
type RiskTab = 'overview' | 'adjustments' | 'methods'
const activeTab = ref<RiskTab>('overview')
const tabs: Array<{ id: RiskTab; label: string }> = [
  { id: 'overview', label: '風險總覽' }, { id: 'adjustments', label: '調整試算' }, { id: 'methods', label: '回測與方法' },
]
const showReports = ref(false)
const selectedHorizon = ref(1)
const selectedConfidence = ref<0.95 | 0.99>(0.95)
const selectedRisk = computed(() => normalizedHorizons.value.find(h => h.horizonDays === selectedHorizon.value))
const selectedVar = computed(() => selectedConfidence.value === .95 ? selectedRisk.value?.var95 : selectedRisk.value?.var99)
const selectedEs = computed(() => selectedConfidence.value === .95 ? selectedRisk.value?.es95 : selectedRisk.value?.es99)
async function selectTab(tab: RiskTab) {
  activeTab.value = tab
  if (tab === 'adjustments') await loadStressTest()
  if (tab === 'methods') {
    await readExistingBacktest()
    if (!isVtGarch.value) await loadModelComparison()
  }
}
function tabKeydown(event: KeyboardEvent, index: number) {
  const next = event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : null
  if (next == null) return
  event.preventDefault()
  const tab = tabs[next]!
  void selectTab(tab.id)
  document.getElementById(`risk-tab-${tab.id}`)?.focus()
}
let viewIsActive = true
let backtestPollTimer: ReturnType<typeof setTimeout> | null = null
let calculationPollTimer: ReturnType<typeof setTimeout> | null = null
let monteCarloPollTimer: ReturnType<typeof setTimeout> | null = null
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
  if (!holdingsRisk.value.length) return
  targetWeights.value = Object.fromEntries(holdingsRisk.value.map(holding => [holding.securityId, Number((holding.weight * 100).toFixed(2))]))
  scenarioResult.value = null
  scenarioMessage.value = ''
}
async function runTargetWeightScenario() {
  if (!holdingsRisk.value.length) return
  scenarioLoading.value = true; scenarioMessage.value = ''
  try {
    scenarioResult.value = await calculatePortfolioRiskScenario(portfolioId.value, holdingsRisk.value.map(h => ({ securityId: h.securityId, targetWeight: (Number(targetWeights.value[h.securityId]) || 0) / 100 })))
  } catch (e) { scenarioMessage.value = riskErrorMessage(e) } finally { scenarioLoading.value = false }
}
const reportStatusLabel = qualityStatusLabel
const reportCreatedAt = (value: string) => new Date(value).toLocaleString('zh-TW', { hour12: false })
async function createReportSnapshot() {
  const run = calculationRun.value
  if (!run || run.status !== 'Completed') {
    reportActionMessage.value = '請先完成風險計算，再保存快照。'
    return
  }
  reportCreating.value = true
  reportActionMessage.value = ''
  try {
    const report = await createPortfolioRiskReportSnapshot(portfolioId.value, run.id)
    selectedReportSnapshot.value = report
    reportSnapshots.value = [report, ...reportSnapshots.value.filter(item => item.id !== report.id)]
    reportActionMessage.value = '風險報告快照已建立；已保存此 Run 的結果與品質狀態。'
    try { reportSnapshots.value = await getPortfolioRiskReportSnapshots(portfolioId.value) }
    catch { reportActionMessage.value += ' 歷史清單更新失敗，本次快照已保存，請勿重複建立。' }
  } catch (e) { reportActionMessage.value = riskErrorMessage(e) }
  finally { reportCreating.value = false }
}
async function openReportSnapshot(reportId: string) { try { selectedReportSnapshot.value = await getPortfolioRiskReportSnapshot(portfolioId.value, reportId) } catch (e) { reportActionMessage.value = riskErrorMessage(e) } }


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
  const run = calculationRun.value
  return {
    id: run ? run.id.slice(0, 8).toUpperCase() : '—',
    portfolio: portfolioName.value,
    model: run?.selectedModel ?? run?.requestedModel ?? 'VT-GARCH-t + Joint-Vector FHS',
    confidence: '95% / 99%',
    lookback: calculationLookback.value != null ? `${calculationLookback.value} 個日報酬` : '—',
    date: calculationDataAsOf.value ?? '未提供',
    duration: run?.completedAtUtc && run.startedAtUtc
      ? `${((new Date(run.completedAtUtc).getTime() - new Date(run.startedAtUtc).getTime()) / 1000).toFixed(1)}s`
      : '—',
  }
})

const sourceHoldings = computed(() => vtGarchResult.value?.conditionalRisk?.holdings ?? fallbackResult.value?.holdings ?? [])
const largestRiskSource = computed(() => [...sourceHoldings.value].filter(h => h.componentRiskShare != null).sort((a, b) => b.componentRiskShare! - a.componentRiskShare!)[0])
const historicalVolatility = computed(() => vtGarchResult.value?.historicalAnnualizedVolatility ?? fallbackResult.value?.historicalAnnualizedVolatility)
const oneDayRisk = computed(() => normalizedHorizons.value.find(h => h.horizonDays === 1))
function lossAmount(value: number | null | undefined) {
  return value == null || !totalMarketValue.value ? '—' : `${formatMoney(-value * totalMarketValue.value)} ${baseCurrency.value}`
}
const overviewMetrics = computed(() => [
  { label: '歷史年化波動率', value: riskPercent(historicalVolatility.value), sub: '依本次歷史資料估計' },
  { label: '歷史最大回撤', value: riskPercent(maxDrawdown.value), sub: '目前持倉權重的歷史回放，非帳戶績效' },
  { label: '95% 單日損失門檻（VaR）', value: lossAmount(oneDayRisk.value?.var95), sub: `${riskPercent(oneDayRisk.value?.var95)} · 依目前估值換算` },
])

function historicalPercent(value: number | null | undefined) {
  return value == null ? '資料不足／未提供' : `${(value * 100).toFixed(2)}%`
}

const diagnosticVarTableRows = computed(() => {
  const h95 = risk.value?.horizons.find((h) => h.horizonDays === 1)
  const h99 = risk99.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma95 = riskEwma.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma99 = riskEwma99.value?.horizons.find((h) => h.horizonDays === 1)
  return [
    { method: '歷史模擬法', var95: h95?.historicalVaR ?? null, var99: h99?.historicalVaR ?? null, note: `1日；有效視窗 ${h95?.effectiveHistoricalObservations ?? '未知'}` },
    { method: 'MVEWMA-FHS', var95: h95?.monteCarloVaR ?? 0, var99: h99?.monteCarloVaR ?? 0, note: '1日' },
    { method: 'EWMA 常態蒙地卡羅', var95: ewma95?.monteCarloVaR ?? 0, var99: ewma99?.monteCarloVaR ?? 0, note: 'λ=0.94' },
  ]
})
const diagnosticEsTableRows = computed(() => {
  const h95 = risk.value?.horizons.find((h) => h.horizonDays === 1)
  const h99 = risk99.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma95 = riskEwma.value?.horizons.find((h) => h.horizonDays === 1)
  const ewma99 = riskEwma99.value?.horizons.find((h) => h.horizonDays === 1)
  return [
    { method: '歷史模擬法', es95: h95?.historicalES ?? null, es99: h99?.historicalES ?? null },
    { method: 'MVEWMA-FHS', es95: h95?.monteCarloES ?? 0, es99: h99?.monteCarloES ?? 0 },
    { method: 'EWMA 常態蒙地卡羅', es95: ewma95?.monteCarloES ?? 0, es99: ewma99?.monteCarloES ?? 0 },
  ]
})

const officialHistogram = computed(() => {
  const returns = dailyLogReturns.value.map(value => (Math.exp(value) - 1) * 100)
  if (!returns.length) return []
  const bounds = [-Infinity, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, Infinity]
  const labels = ['<-6%', '-6~-5%', '-5~-4%', '-4~-3%', '-3~-2%', '-2~-1%', '-1~0%', '0~1%', '1~2%', '2~3%', '3~4%', '4~5%', '5~6%', '>6%']
  const h1 = normalizedHorizons.value.find(h => h.horizonDays === 1)
  const var95 = (h1?.var95 ?? 0) * 100
  const var99 = (h1?.var99 ?? 0) * 100
  return labels.map((bin, index) => ({ bin, count: returns.filter(value => value >= bounds[index] && value < bounds[index + 1]).length, isTail95: bounds[index + 1] <= var95, isTail99: bounds[index + 1] <= var99 }))
})
const histogramChart = computed(() => {
  const max = Math.max(10, Math.ceil(Math.max(...officialHistogram.value.map(bin => bin.count), 0) / 10) * 10)
  const ticks = Array.from({ length: 6 }, (_, index) => max - (max / 5) * index)
  const y = (value: number) => 30 + (1 - value / max) * 180
  return { ticks, y, height: (value: number) => value / max * 180 }
})
const officialVarEsComparison = computed(() => {
  if (isVtGarch.value) {
    return (vtGarchResult.value?.confidenceCurve ?? []).map(point => ({
      confidence: `${(point.confidenceLevel * 100).toFixed(point.confidenceLevel % 0.01 === 0 ? 0 : 1)}%`,
      var: point.var * 100,
      es: point.es * 100,
    }))
  }
  return riskCurve.value.map(item => {
    const horizon = item.horizons.find(h => h.horizonDays === 1)
    return { confidence: `${(item.confidenceLevel * 100).toFixed(item.confidenceLevel % 0.01 === 0 ? 0 : 1)}%`, var: (horizon?.monteCarloVaR ?? 0) * 100, es: (horizon?.monteCarloES ?? 0) * 100 }
  })
})
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
  const percent = (value: number) => `${value >= 0 ? '+' : ''}${(value * 100).toFixed(1)}%`
  if (isVtGarch.value) {
    const vt = monteCarloRun.value?.result as VtGarchRiskResult | null
    const fh = vt?.fitHealth
    if (!fh) return []
    const lastBand = (monteCarlo.value?.bands ?? []).at(-1)
    return [
      { label: 'GARCH 拟合健康', value: fh.healthy ? 'Healthy' : 'Degraded' },
      { label: '最大 persistence', value: fh.maxPersistence.toFixed(4) },
      { label: '最小 Student-ν', value: fh.minNu.toFixed(2) },
      { label: 'near-unit 比率', value: `${(fh.nearUnitRate * 100).toFixed(1)}%` },
      { label: '條件波動率（年化）', value: riskPercent(vt?.conditionalRisk?.annualizedVolatility) },
      { label: '歷史年化波動率', value: riskPercent(vt?.historicalAnnualizedVolatility) },
      { label: '期末 p95', value: lastBand ? percent(lastBand.p95) : '—' },
      { label: '期末 p99', value: lastBand ? percent(lastBand.p99) : '—' },
    ]
  }
  const diagnostics = monteCarloBase.value?.diagnostics
  if (!diagnostics) return []
  return [
    { label: '年化投組波動率', value: percent(diagnostics.annualizedPortfolioVolatility) },
    { label: '期末 p95', value: percent(diagnostics.p95FinalReturn) },
    { label: '期末 p99', value: percent(diagnostics.p99FinalReturn) },
    { label: '殘差向量 p99', value: diagnostics.residualNormP99.toFixed(2) },
    { label: '殘差向量最大值', value: diagnostics.maxResidualNorm.toFixed(2) },
    { label: '期望－中位數', value: percent(diagnostics.expectedMedianGap) },
  ]
})
const monteCarloRightSkew = computed(() => {
  if (isVtGarch.value) {
    const vt = monteCarloRun.value?.result as VtGarchRiskResult | null
    const median = (monteCarlo.value?.bands ?? []).at(-1)?.p50 ?? 0
    const gap = (vt?.summary?.expectedReturn ?? 0) - median
    return gap >= 0.25
      ? { warn: true, msg: `期望值與中位數差距 ${(gap * 100).toFixed(1)} 個百分點，分布右偏；下行情境請一併參考。` }
      : { warn: false, msg: '' }
  }
  const diagnostics = monteCarloBase.value?.diagnostics
  return diagnostics?.rightSkewWarning
    ? { warn: true, msg: diagnostics.rightSkewMessage ?? '' }
    : { warn: false, msg: '' }
})
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

async function loadDeferred(state: typeof modelComparisonState, message: typeof modelComparisonError, request: () => Promise<void>) {
  if (state.value === 'loading' || state.value === 'ready') return
  state.value = 'loading'; message.value = ''
  try { await request(); if (viewIsActive) state.value = 'ready' }
  catch (e) { if (viewIsActive) { message.value = riskErrorMessage(e); state.value = 'error' } }
}
const loadModelComparison = () => loadDeferred(modelComparisonState, modelComparisonError, async () => {
  const [syncRisk, at99, ewma95, ewma99, ...curve] = await Promise.all([
    getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: .95, simulations: 10000, model: 'mvewma_fhs' }),
    getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: .99, simulations: 10000, model: 'mvewma_fhs' }),
    getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: .95, simulations: 10000, model: 'gbm_ewma_normal' }),
    getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 30, confidenceLevel: .99, simulations: 10000, model: 'gbm_ewma_normal' }),
    ...[.90, .95, .975, .99, .995].map(confidenceLevel => getPortfolioRisk(portfolioId.value, { from: fromDate.value, to: toDate.value, horizonDays: 1, confidenceLevel, simulations: 5000, model: 'mvewma_fhs' })),
  ])
  risk.value = syncRisk
  risk99.value = at99; riskEwma.value = ewma95; riskEwma99.value = ewma99; riskCurve.value = curve
})
const loadStressTest = () => loadDeferred(stressTestState, stressTestError, async () => { stressTest.value = await getPortfolioStressTest(portfolioId.value) })
const loadBacktest = () => {
  if (backtestState.value === 'loading') return
  backtestState.value = 'idle'
  return loadDeferred(backtestState, backtestError, runQualityValidation)
}
async function readExistingBacktest() {
  const id = calculationRun.value?.qualityEvaluation?.riskBacktestRunId
  if (!id || activeBacktestRun.value?.id === id || backtestState.value === 'loading') return
  await loadDeferred(backtestState, backtestError, async () => {
    await waitForBacktestRun(await getPortfolioRiskBacktestRun(portfolioId.value, id))
    if (calculationRun.value?.qualityEvaluation?.status === 'Pending') {
      calculationRun.value = await getRiskCalculation(portfolioId.value, calculationRun.value.id)
    }
  })
}
async function runQualityValidation() {
  const calculation = calculationRun.value
  if (!calculation || calculation.status !== 'Completed' || qualityValidationLoading.value) return
  qualityValidationLoading.value = true
  try {
    const evaluation = await validateRiskCalculation(portfolioId.value, calculation.id)
    const linked = await getPortfolioRiskBacktestRun(portfolioId.value, evaluation.riskBacktestRunId)
    await waitForBacktestRun(linked)
    calculationRun.value = await getRiskCalculation(portfolioId.value, calculation.id)
  } finally {
    qualityValidationLoading.value = false
  }
}
async function waitForBacktestRun(initialRun: PortfolioRiskBacktestRun) {
  let run = initialRun
  while (viewIsActive) {
    if (run.status === 'Completed') {
      if (!run.result) throw new Error('回測已完成，但找不到結果快照。')
      backtest.value = run.result
      activeBacktestRun.value = run
      const qualityModel = calculationRun.value?.qualityEvaluation?.model
      selectedBacktestModel.value = qualityModel ?? run.selectedModel ?? run.result.models[0]?.model ?? 'Historical'
      return
    }
    if (run.status === 'Failed') throw new Error(run.errorMessage || '回測失敗，請重新執行。')
    await new Promise<void>(resolve => { backtestPollTimer = setTimeout(resolve, 1500) })
    if (!viewIsActive) return
    run = await getPortfolioRiskBacktestRun(portfolioId.value, run.id)
  }
}
const loadMonteCarlo = () => loadDeferred(monteCarloState, monteCarloError, async () => {
  if (isVtGarch.value) {
    const runs = await listRiskCalculations(portfolioId.value)
    const existing = runs.find(run => run.operation === 'monte-carlo' && run.status !== 'Failed' && isReusableRun(run))
    const run = existing ?? await createRiskCalculation(portfolioId.value, 'monte-carlo', {
      simulations: 10000,
      from: backtestFromDate.value,
      to: toDate.value,
    })
    monteCarloRun.value = run
    if (run.status !== 'Completed' && run.status !== 'Failed') {
      const finalRun = await waitForMonteCarloRun(run)
      if (finalRun.status === 'Failed') throw new Error(finalRun.errorMessage || '蒙地卡羅計算失敗，請重新執行。')
    }
  } else {
    monteCarloBase.value = await getPortfolioMonteCarlo(portfolioId.value)
  }
})

async function waitForCalculationRun(initialRun: RiskCalculationRun) {
  let run = initialRun
  while (viewIsActive) {
    if (run.status === 'Completed' || run.status === 'Failed') {
      calculationRun.value = run
      if (run.status === 'Failed') throw new Error(run.errorMessage || '風險計算失敗，請重新執行。')
      return
    }
    await new Promise<void>(resolve => { calculationPollTimer = setTimeout(resolve, 2000) })
    if (!viewIsActive) return
    run = await getRiskCalculation(portfolioId.value, run.id)
    calculationRun.value = run
  }
}

async function waitForMonteCarloRun(initialRun: RiskCalculationRun): Promise<RiskCalculationRun> {
  let run = initialRun
  while (viewIsActive) {
    if (run.status === 'Completed' || run.status === 'Failed') {
      monteCarloRun.value = run
      return run
    }
    await new Promise<void>(resolve => { monteCarloPollTimer = setTimeout(resolve, 2000) })
    if (!viewIsActive) return run
    run = await getRiskCalculation(portfolioId.value, run.id)
    monteCarloRun.value = run
  }
  return run
}

function isReusableRun(run: RiskCalculationRun): boolean {
  if (run.status !== 'Completed') return true
  if (run.selectedModel !== 'VT-GARCH-t + Joint-Vector FHS') return true
  const result = run.result as Record<string, unknown> | null
  if (!result) return false
  return run.operation === 'monte-carlo' ? 'bands' in result : 'confidenceCurve' in result
}

onMounted(async () => {
  loading.value = true
  try {
    const portfolio = await getPortfolio(portfolioId.value)
    portfolioName.value = portfolio.name
    if (!portfolio.holdings?.length) {
      error.value = '投資組合尚無持倉，加入持股後即可計算風險。'
      return
    }
    const [calculations, governanceData] = await Promise.all([
      listRiskCalculations(portfolioId.value), getPortfolioRiskGovernance(portfolioId.value),
    ])
    governance.value = governanceData
    try {
      const valuation = await getPortfolioValuation(portfolioId.value)
      portfolioTotalValue.value = valuation.totalMarketValue
      portfolioCurrency.value = valuation.currency
    } catch {
      portfolioTotalValue.value = null
    }

    const existing = calculations.find(run => run.operation === 'risk' && run.status !== 'Failed' && isReusableRun(run))
    const run = existing ?? await createRiskCalculation(portfolioId.value, 'risk', {
      simulations: 10000,
      from: backtestFromDate.value,
      to: toDate.value,
    })
    calculationRun.value = run

    if (run.status === 'Completed') {
      // Already done, no polling needed
    } else if (run.status === 'Failed') {
      error.value = run.errorMessage || '風險計算失敗，請重新執行。'
    } else {
      await waitForCalculationRun(run)
    }

    if (calculationRun.value?.status === 'Completed' && !isVtGarch.value) {
      risk.value = calculationRun.value.result as PortfolioRiskResponse
      if (risk.value) {
        targetWeights.value = Object.fromEntries(risk.value.holdings.map(holding => [holding.securityId, Number((holding.weight * 100).toFixed(2))]))
      }
    }

    try {
      reportSnapshots.value = await getPortfolioRiskReportSnapshots(portfolioId.value)
    } catch {
      reportActionMessage.value = '歷史報告暫時無法讀取，請稍後重試。'
    }
  } catch (e) {
    error.value = riskErrorMessage(e)
  } finally {
    loading.value = false
    if (viewIsActive && calculationRun.value?.qualityEvaluation) void readExistingBacktest()
  }
})

onBeforeUnmount(() => { viewIsActive = false; if (backtestPollTimer) clearTimeout(backtestPollTimer); if (calculationPollTimer) clearTimeout(calculationPollTimer); if (monteCarloPollTimer) clearTimeout(monteCarloPollTimer) })
</script>

<template>
  <div class="prestige-page">
    <div class="prestige-section">
      <button class="prestige-btn back-btn" @click="router.push({ name: 'risk-runs' })">
        ← 返回風險分析
      </button>

      <div class="page-head">
        <div>
          <span class="prestige-label">分析編號 — <span class="prestige-mono">{{ runInfo.id }}</span></span>
          <h1 class="page-title">{{ runInfo.portfolio }}</h1>
        </div>
        <span
          class="prestige-tag"
          :class="calculationRun?.status === 'Completed' ? 'tag-completed' : calculationRun?.status === 'Failed' ? 'tag-critical' : 'tag-warning'"
        >
          <span class="dot" :class="{ pulse: calculationRun?.status === 'Running' || calculationRun?.status === 'FallbackRunning' || calculationRun?.status === 'Queued' }" />
          {{ calculationRun?.status === 'Completed' ? '計算完成' : calculationRun?.status === 'Failed' ? '計算失敗' : calculationRun?.status === 'FallbackRunning' ? '替代模型計算中' : calculationRun?.status === 'Running' ? `計算中 ${calculationRun.progressPercent}%` : '排隊中' }}
        </span>
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

      <template v-if="!loading && calculationRun?.status === 'Completed'">
        <div class="report-toolbar">
          <p class="muted-text">資料截止 {{ runInfo.date }} · 輸入期間 {{ calculationRun.inputSnapshot?.from ?? '未保存' }} ～ {{ calculationRun.inputSnapshot?.to ?? '未保存' }}</p>
          <div class="btn-row">
            <button class="prestige-btn" :disabled="reportCreating" @click="createReportSnapshot">{{ reportCreating ? '儲存中…' : '儲存風險報告' }}</button>
            <button class="prestige-btn" :aria-expanded="showReports" @click="showReports = !showReports">歷史報告（{{ reportSnapshots.length }}）</button>
          </div>
          <p v-if="reportActionMessage" role="status">{{ reportActionMessage }}</p>
          <div v-if="showReports" class="snap-list">
            <p v-if="!reportSnapshots.length" class="muted-text">尚無已保存的報告。</p>
            <button v-for="report in reportSnapshots" :key="report.id" class="prestige-btn" @click="openReportSnapshot(report.id)">{{ reportCreatedAt(report.createdAtUtc) }} · {{ reportStatusLabel(report.overallStatus) }}</button>
          </div>
        </div>
        <RiskSnapshotReport v-if="selectedReportSnapshot" :report="selectedReportSnapshot" @close="selectedReportSnapshot = null" />
        <!-- Fallback banner -->
        <ScrollReveal v-if="isFallback" style="margin-bottom: 24px">
          <div class="warn-box">
            正式模型 VT-GARCH-t + Joint-Vector FHS 不可用，已回退至 {{ calculationRun.selectedModel }}。
            原因：{{ calculationRun.fallbackReason }}
          </div>
        </ScrollReveal>


        <p v-if="fitHealth && !fitHealth.healthy" class="warn-box" role="alert">本次模型擬合未通過健康檢查，請查看回測與方法。</p>
        <!-- Risk Governance -->
        <ScrollReveal v-if="governance && governanceAlerts.length" style="margin-bottom: 24px">
          <div class="prestige-panel prestige-panel-pad">
            <div class="panel-head">
              <div>
                <h2 class="panel-title">重要提醒</h2>
                <span class="prestige-label">{{ governanceDataStatus(governance.dataStatus) }} · {{ governance.commonTradingDays }} 個共同交易日</span>
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
                  <span class="alert-thresholds prestige-mono">目前 {{ alert.code.includes('age') ? `${alert.currentValue} 日` : `${(alert.currentValue * 100).toFixed(1)}%` }}</span>
                </div>
                <div class="alert-msg">{{ alert.message }}</div>
              </div>
            </div>
            <p v-else class="muted-text">目前沒有觸發已設定的集中度與資料品質門檻；這不代表投組沒有風險。</p>

          </div>
        </ScrollReveal>

        <div v-if="calculationRun.qualityEvaluation && ['Failed', 'Error', 'InsufficientData'].includes(calculationRun.qualityEvaluation.status)" class="warn-box" role="alert">
          {{ qualityStatusLabel(calculationRun.qualityEvaluation.status) }}
          <span v-for="code in calculationRun.qualityEvaluation.failureCodes" :key="code"> · {{ qualityCodeLabel(code) }}</span>
        </div>
        <p v-for="code in calculationRun.qualityEvaluation?.warningCodes ?? []" :key="code" class="warn-box">{{ qualityCodeLabel(code) }}</p>
        <nav class="risk-tabs" role="tablist" aria-label="風險分析分頁">
          <button v-for="(tab, index) in tabs" :id="`risk-tab-${tab.id}`" :key="tab.id" role="tab" :aria-selected="activeTab === tab.id" :aria-controls="`risk-panel-${tab.id}`" :tabindex="activeTab === tab.id ? 0 : -1" @click="selectTab(tab.id)" @keydown="tabKeydown($event, index)">{{ tab.label }}</button>
        </nav>
        <section id="risk-panel-overview" v-show="activeTab === 'overview'" role="tabpanel" aria-labelledby="risk-tab-overview">
          <div class="prestige-panel overview-metrics">
            <div v-for="metric in overviewMetrics" :key="metric.label" class="kpi-cell">
              <span class="prestige-label">{{ metric.label }}</span><strong class="kpi-value prestige-mono">{{ metric.value }}</strong><span class="kpi-sub">{{ metric.sub }}</span>
            </div>
          </div>
          <div class="overview-secondary">
            <div><span>超過門檻時的平均損失（ES 95%）</span><strong>{{ lossAmount(oneDayRisk?.es95) }}</strong><small>{{ riskPercent(oneDayRisk?.es95) }} · 依目前估值換算</small></div>
            <div><span>最大風險來源</span><strong>{{ largestRiskSource ? `${largestRiskSource.ticker} · ${largestRiskSource.securityName}` : '未提供風險分解' }}</strong><small v-if="largestRiskSource">占模型波動風險 {{ riskPercent(largestRiskSource.componentRiskShare, 1) }}</small></div>
          </div>
          <p class="table-note">VaR 是模型估計的損失門檻，仍可能出現更大的損失。歷史指標依上方輸入期間計算，並非未來一年預測。</p>
          <p v-if="governance && !governanceAlerts.length" class="table-note">目前未觸發集中度與資料品質警示。</p>
          <RiskSources :conditional="vtGarchResult?.conditionalRisk" :fallback="fallbackResult" />
        </section>
        <RiskQualitySummary :evaluation="calculationRun.qualityEvaluation" :backtest="activeBacktestRun" :detailed="activeTab === 'methods'" :loading="backtestState === 'loading' || qualityValidationLoading" :error="backtestError" @view="selectTab('methods')" @run="loadBacktest" @retry="calculationRun.qualityEvaluation ? readExistingBacktest() : loadBacktest()" />
        <section id="risk-panel-adjustments" v-if="activeTab === 'adjustments'" role="tabpanel" aria-labelledby="risk-tab-adjustments">
            <!-- What-if scenario -->
            <div v-if="hasHoldingsData" class="prestige-panel prestige-panel-pad">
              <h3 class="sub-title">調整試算</h3>
              <p class="muted-text">僅試算，不會修改真實投組；現金為負時代表融資，不含融資利率、保證金與追繳規則。</p>
              <div class="weight-grid">
                <label v-for="holding in holdingsRisk" :key="holding.securityId" class="weight-row">
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
                  <strong class="mini-value prestige-mono">{{ metric.label === 'HHI' ? metric.current?.toFixed(3) ?? '—' : riskPercent(metric.current, 1) }} → {{ metric.label === 'HHI' ? metric.scenario?.toFixed(3) ?? '—' : riskPercent(metric.scenario, 1) }}</strong>
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

          <div v-if="!hasHoldingsData" class="prestige-panel prestige-panel-pad"><h2 class="panel-title">權重調整試算</h2><p class="muted-text">此模型目前未提供同口徑的權重調整試算。可先查看下方壓力情境；它不會修改實際持倉。</p></div>
        <!-- Stress Test Scenarios -->
        <ScrollReveal class="mt-20">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">壓力測試情境</h2>
              <span class="prestige-label">依目前持倉回放；與已保存的風險結果可能使用不同資料時點</span>
            </div>
            <div class="section-body">
              <p v-if="stressTestState === 'loading'" role="status">載入壓力情境中…</p>
              <p v-if="stressTestError" role="alert">{{ stressTestError }} <button class="prestige-btn" @click="loadStressTest">重試</button></p>
              <p v-if="stressTestState === 'ready' && !formalStressScenarios.length">目前沒有可用的壓力情境。</p>
              <div class="grid-3">
                <div
                  v-for="s in formalStressScenarios"
                  :key="s.id"
                  class="prestige-panel stress-card"
                  role="button" tabindex="0" :aria-expanded="selectedScenario === s.id"
                  @keydown.enter="selectedScenario = selectedScenario === s.id ? null : s.id"
                  @keydown.space.prevent="selectedScenario = selectedScenario === s.id ? null : s.id"
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
        </ScrollReveal>


        </section>
        <section id="risk-panel-methods" v-if="activeTab === 'methods'" role="tabpanel" aria-labelledby="risk-tab-methods">
        <!-- Backtest Results -->
        <ScrollReveal v-if="backtest" class="mt-20">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">VaR 回測驗證</h2>
              <span class="prestige-label">最近 {{ backtestData.length }} 個樣本外交易日 · 虛線為逐日 VaR，紅柱為超損日</span>
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

                  <polyline :points="backtestData.map(d => `${d.x},${backtestChart.y(d.var95)}`).join(' ')" fill="none" stroke="#b05c5c" stroke-width="1.5" stroke-dasharray="4 4" />
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
                    輸入資料期間 {{ activeBacktestRun?.from }} ～ {{ activeBacktestRun?.to }}；此圖顯示所選模型與水準，品質摘要固定採用本次驗證的模型與水準。
                    <template v-if="activeBacktestRun?.fallbackReason">目前結果已回退：{{ activeBacktestRun.selectedModel }}（{{ activeBacktestRun.fallbackReason }}）</template>
                  </p>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>


          <section class="prestige-panel prestige-panel-pad risk-estimates">
            <h2 class="panel-title">其他期間與信賴水準</h2>
            <div class="estimate-controls"><label>持有期間 <select v-model.number="selectedHorizon" class="prestige-input"><option v-for="h in normalizedHorizons" :key="h.horizonDays" :value="h.horizonDays">{{ h.horizonDays }} 日</option></select></label><label>信賴水準 <select v-model.number="selectedConfidence" class="prestige-input"><option :value=".95">95%</option><option :value=".99">99%</option></select></label></div>
            <div class="overview-secondary"><div><span>損失門檻 VaR</span><strong>{{ riskPercent(selectedVar) }}</strong></div><div><span>超過門檻時平均損失 ES</span><strong>{{ riskPercent(selectedEs) }}</strong></div></div>
            <p class="muted-text">僅顯示本次保存結果；未提供的水準以「—」標示。</p>
          </section>
          <details class="advanced-section"><summary>圖表與模型比較</summary>
            <p v-if="modelComparisonState === 'loading'" role="status">載入其他模型診斷中…</p>
            <p v-if="modelComparisonError" role="alert">{{ modelComparisonError }} <button class="prestige-btn" @click="loadModelComparison">重試</button></p>
        <!-- VaR Results Table -->
        <ScrollReveal class="mt-20">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">歷史報酬分布</h2>
              <span class="prestige-label">{{ runInfo.model }} — 輸入資料參考</span>
            </div>
            <div class="section-body">
              <div class="stack">
                <div v-if="officialHistogram.length">
                  <h3 class="chart-title">歷史日報酬分布（輸入參考）</h3>
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
        </ScrollReveal>

        <!-- ES / CVaR Analysis -->
        <ScrollReveal class="mt-20">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">VaR 與 ES 信心水準曲線</h2>
              <span class="prestige-label">{{ runInfo.model }} — 1 日風險估計</span>
            </div>
            <div class="section-body">
              <div class="stack">
                <div>
                  <h3 class="chart-title">{{ isVtGarch ? 'VaR vs ES 信心水準曲線（VT-GARCH-t 1 日）' : 'VaR vs ES 比較（歷史 EWMA 診斷，非正式風險引擎）' }}</h3>
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

        <!-- EWMA Diagnostic (deferred, not the official risk engine) -->
        <ScrollReveal class="mt-20" v-if="!isVtGarch && modelComparisonState === 'ready'">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">歷史 EWMA 波動率診斷</h2>
              <span class="prestige-label">非正式風險引擎 — 診斷期間 {{ fromDate }} ～ {{ toDate }}，僅供交叉比對</span>
            </div>
            <div class="section-body">
              <div class="stack">
                <div class="table-wrap">
                  <table class="prestige-table">
                    <thead>
                      <tr>
                        <th style="text-align: left">診斷方法</th>
                        <th style="text-align: right">VaR 95%</th>
                        <th style="text-align: right">VaR 99%</th>
                        <th style="text-align: left; padding-left: 16px">備註</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(v, i) in diagnosticVarTableRows" :key="i">
                        <td>{{ v.method }}</td>
                        <td class="prestige-mono td-num">{{ historicalPercent(v.var95) }}</td>
                        <td class="prestige-mono td-num">{{ historicalPercent(v.var99) }}</td>
                        <td class="td-muted" style="padding-left: 16px">{{ v.note }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <div class="table-wrap">
                  <table class="prestige-table">
                    <thead>
                      <tr>
                        <th style="text-align: left">診斷方法</th>
                        <th style="text-align: right">ES 95%</th>
                        <th style="text-align: right">ES 99%</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(e, i) in diagnosticEsTableRows" :key="i">
                        <td>{{ e.method }}</td>
                        <td class="prestige-mono td-num">{{ historicalPercent(e.es95) }}</td>
                        <td class="prestige-mono td-num">{{ historicalPercent(e.es99) }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <p v-for="h in risk?.horizons ?? []" :key="`historical-${h.horizonDays}`" class="table-note">
                  歷史 {{ h.horizonDays }} 日：有效滾動視窗 {{ h.effectiveHistoricalObservations ?? '未知' }}，門檻 {{ h.minimumHistoricalObservations ?? 100 }}；
                  VaR {{ historicalPercent(h.historicalVaR) }}，ES {{ historicalPercent(h.historicalES) }}。
                  {{ h.historicalStatus === 'InsufficientData' ? '樣本不足，停用該期限歷史數字。' : '' }}
                </p>
                <p class="table-note">重疊視窗不代表獨立樣本；不同期限使用不同樣本，不保證 VaR 單調。模擬數字另依每日資料估計，不能替代歷史樣本品質。</p>
                <p class="table-note">以上為同步 EWMA 端點的歷史診斷資料，不作為正式風險決策依據。正式模型為 {{ calculationRun?.selectedModel ?? '—' }}。</p>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- GARCH vs Historical 1-day comparison -->
        <ScrollReveal class="mt-20" v-if="isVtGarch && garchOneDayComparison.length">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">1 日風險模型對照</h2>
              <span class="prestige-label">純歷史排序 vs VT-GARCH-t — 95% & 99%</span>
            </div>
            <div class="section-body">
              <div class="table-wrap">
                <table class="prestige-table">
                  <thead>
                    <tr>
                      <th style="text-align: left">計算方法</th>
                      <th style="text-align: right">VaR 95%</th>
                      <th style="text-align: right">VaR 99%</th>
                      <th style="text-align: right">ES 95%</th>
                      <th style="text-align: right">ES 99%</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="(row, i) in garchOneDayComparison" :key="i">
                      <td>{{ row.method }}</td>
                      <td class="prestige-mono td-num td-warn">{{ riskPercent(row.var95) }}</td>
                      <td class="prestige-mono td-num td-danger">{{ riskPercent(row.var99) }}</td>
                      <td class="prestige-mono td-num td-warn">{{ riskPercent(row.es95) }}</td>
                      <td class="prestige-mono td-num td-danger">{{ riskPercent(row.es99) }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <p class="table-note">歷史模擬法為純歷史排序、不含模型假設；VT-GARCH-t 為正式風險引擎。兩者差異反映波動率聚集與厚尾修正的影響。</p>
            </div>
          </div>
        </ScrollReveal>


          </details>
        <!-- Monte Carlo Simulation -->
        <ScrollReveal class="mt-20" style="margin-bottom: 80px">
          <div class="prestige-panel">
            <div class="section-head">
              <h2 class="panel-title">蒙地卡羅模擬</h2>
              <span class="prestige-label">{{ monteCarlo?.model ?? 'VT-GARCH-t + Joint-Vector FHS' }} — {{ monteCarlo?.simulations?.toLocaleString() ?? '10,000' }} 次、{{ monteCarlo?.horizonDays ?? 252 }} 個交易日路徑模擬</span>
            </div>
            <div class="section-body">
              <button v-if="!monteCarlo" class="prestige-btn" :disabled="monteCarloState === 'loading'" @click="loadMonteCarlo">{{ monteCarloState === 'loading' ? '模擬處理中…' : monteCarloState === 'error' ? '重試路徑模擬' : '載入或執行路徑模擬' }}</button>
              <p v-if="monteCarloError" role="alert">{{ monteCarloError }}</p>
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
                <div class="mini-panel" style="margin-top: 20px">
                  <div class="panel-head" style="margin-bottom: 12px">
                    <span class="mini-title" style="margin: 0">模型診斷</span>
                    <span class="prestige-label">Model Diagnostics</span>
                  </div>
                  <div v-if="monteCarloRightSkew.warn" class="warn-box">
                    {{ monteCarloRightSkew.msg }}
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
              <div v-else class="prestige-empty">
                {{ monteCarlo?.message ?? '按下按鈕後讀取可用結果；沒有結果時才建立模擬工作。' }}
              </div>
            </div>
          </div>
        </ScrollReveal>

          <details class="advanced-section"><summary>計算口徑與技術明細</summary>
        <section class="prestige-panel prestige-panel-pad">
          <h2 class="panel-title">計算口徑</h2>
          <p>請求期間 {{ calculationRun.inputSnapshot?.from ?? '未保存' }} ～ {{ calculationRun.inputSnapshot?.to ?? '未保存' }}；方法 {{ calculationRun.selectedModel ?? '未提供' }}；估計樣本 {{ calculationLookback ?? '未知' }}。</p>
          <RiskMeasurementBasis :basis="fallbackResult?.measurementBasis" />
          <p>情境比較只使用同一計算快照、方法與期間；歷史回撤、條件波動、模型路徑回撤和帳戶實際績效不可互換。</p>
        </section>
        <!-- Model Metadata -->
        <ScrollReveal :delay="0.05">
          <div class="prestige-panel flush-panel" style="margin-bottom: 24px">
            <h3 class="panel-sub-title">模型參數與識別</h3>
            <div class="info-grid info-grid-4">
              <div
                v-for="(item, i) in [
                  { label: 'ALGORITHM VERSION', value: calculationRun?.algorithmVersion ?? '—' },
                  { label: 'DATA FACTOR', value: calculationRun?.dataFactorVersion ?? '—' },
                  { label: 'SIMULATIONS', value: calculationSimulations != null ? calculationSimulations.toLocaleString() : '—' },
                  { label: 'INPUT HASH', value: calculationRun?.inputHash ? calculationRun.inputHash.slice(0, 12) + '…' : '—' },
                ]"
                :key="i"
                class="info-cell"
              >
                <span class="prestige-label cell-label">{{ item.label }}</span>
                <span class="info-value prestige-mono">{{ item.value }}</span>
              </div>
            </div>
            <div v-if="fitHealth" class="info-grid info-grid-4" style="margin-top: 1px">
              <div
                v-for="(item, i) in [
                  { label: 'FIT HEALTH', value: fitHealth.healthy ? 'Healthy' : 'Degraded' },
                  { label: 'MAX PERSISTENCE', value: fitHealth.maxPersistence.toFixed(4) },
                  { label: 'MIN STUDENT-ν', value: fitHealth.minNu.toFixed(2) },
                  { label: 'NEAR-UNIT RATE', value: (fitHealth.nearUnitRate * 100).toFixed(1) + '%' },
                ]"
                :key="i"
                class="info-cell"
              >
                <span class="prestige-label cell-label">{{ item.label }}</span>
                <span class="info-value prestige-mono" :style="{ color: item.label === 'FIT HEALTH' ? (fitHealth.healthy ? '#7fa387' : '#b05c5c') : undefined }">{{ item.value }}</span>
              </div>
            </div>
            <div v-else-if="fallbackResult" class="info-grid info-grid-4" style="margin-top: 1px">
              <div
                v-for="(item, i) in [
                  { label: 'COVARIANCE METHOD', value: fallbackResult.covarianceMethod ?? '—' },
                  { label: 'RESIDUAL SAMPLING', value: fallbackResult.residualSampling ?? '—' },
                  { label: 'COMMON TRADING DAYS', value: fallbackResult.commonTradingDays ? `${fallbackResult.commonTradingDays} 天` : '—' },
                  { label: 'SHRINKAGE ALPHA', value: fallbackResult.shrinkageAlpha != null ? `${(fallbackResult.shrinkageAlpha * 100).toFixed(2)}%` : '—' },
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

          </details>
        </section>
      </template>

      <div style="height: 60px" />
    </div>

    <Footer :dark="true" label="RISK" />
  </div>
</template>

<style scoped>
.report-toolbar { margin-bottom: 24px; }
.risk-tabs { display: flex; gap: 8px; border-bottom: 1px solid var(--gold-border); margin: 24px 0; }
.risk-tabs button { flex: 1; max-width: 220px; padding: 16px 12px; color: var(--muted); border: 0; border-bottom: 2px solid transparent; background: transparent; cursor: pointer; font-size: 15px; }
.risk-tabs button[aria-selected="true"] { color: var(--gold); border-bottom-color: var(--gold); }
.risk-tabs button:focus-visible, summary:focus-visible { outline: 2px solid var(--gold); outline-offset: 3px; }
.overview-metrics { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); }
.overview-metrics strong { display: block; font-size: clamp(24px, 3vw, 36px); overflow-wrap: anywhere; }
.overview-secondary { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 24px; margin: 24px 0; }
.overview-secondary span, .overview-secondary small { display: block; color: var(--muted); font-size: 13px; line-height: 1.7; }
.overview-secondary strong { display: block; font-size: 20px; margin: 8px 0; }
.advanced-section { margin: 24px 0; }
.advanced-section > summary { padding: 20px 0; color: var(--gold); cursor: pointer; font-size: 16px; }
.estimate-controls { display: flex; gap: 24px; flex-wrap: wrap; margin-top: 20px; }
.estimate-controls label { color: var(--muted); font-size: 13px; }
.estimate-controls select { margin-left: 8px; }
@media (max-width: 640px) { .overview-metrics, .overview-secondary { grid-template-columns: 1fr; } .risk-tabs button { font-size: 14px; padding: 14px 8px; } }
@media print {
  .prestige-section > :not(.saved-risk-report) { display: none !important; }
  .prestige-page { background: white !important; color: black; --ivory: #111; --muted: #444; --gold: #444; }
  .prestige-section { width: 100%; padding: 0; margin: 0; }
  :global(body:has(.saved-risk-report) .kimi-app-header), :global(body:has(.saved-risk-report) footer) { display: none !important; }
  :global(body:has(.saved-risk-report) .kimi-app-shell), :global(body:has(.saved-risk-report) .kimi-app-content) { background: white; padding-top: 0 !important; }
}

.prestige-page {
  min-height: calc(100vh - 60px);
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

.kpi-grid-5 {
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
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
    grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
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
