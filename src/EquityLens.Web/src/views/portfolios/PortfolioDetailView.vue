<script setup lang="ts">
import type { AxiosError } from 'axios'
import { computed, onMounted, ref } from 'vue'
import { NPagination } from 'naive-ui'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import LineChart from '../../components/kimi/LineChart.vue'
import DonutChart from '../../components/kimi/DonutChart.vue'
import DataTable from '../../components/kimi/DataTable.vue'
import {
  getPortfolio,
  getPortfolioValuation,
  getPortfolioValuationHistory,
  getPortfolioRisk,
  type PortfolioDetail,
  type PortfolioValuationHistoryResponse,
  type PortfolioValuationResponse,
  type PortfolioRiskResponse,
} from '../../services/risk.ts'
import {
  createTransaction,
  deleteTransaction,
  getTransactions,
  type TransactionResponse,
} from '../../services/portfolioTransactions.ts'
import {
  getDividendCashFlows,
  getLatestDividends,
  type DividendCashFlow,
  type LatestDividendResponse,
} from '../../services/portfolioDividends.ts'
import {
  createPortfolioCashFlow,
  deletePortfolioCashFlow,
  getPortfolioCashFlows,
  updatePortfolioCashFlow,
  type PortfolioCashFlow,
  type UpsertCashFlowRequest,
} from '../../services/portfolioCashFlows.ts'
import { getSecurityPrices, syncSecurityPrices } from '../../services/marketPrices.ts'
import {
  resolveSecurity,
  searchSecurities,
  type SecuritySearchResult,
} from '../../services/securities.ts'

const router = useRouter()
const route = useRoute()
const portfolioId = computed(() => String(route.params.id))

const portfolio = ref<PortfolioDetail | null>(null)
const valuation = ref<PortfolioValuationResponse | null>(null)
const valuationHistory = ref<PortfolioValuationHistoryResponse | null>(null)
const risk = ref<PortfolioRiskResponse | null>(null)
const loading = ref(false)
const error = ref('')
const valuationError = ref('')
const valuationHistoryError = ref('')
const riskError = ref('')
const loadingPeriodData = ref(false)
const showAddStock = ref(false)
const securityQuery = ref('')
const securityResults = ref<SecuritySearchResult[]>([])
const selectedSecurity = ref<SecuritySearchResult | null>(null)
const searchingSecurities = ref(false)
const addingStock = ref(false)
const loadingClosePrice = ref(false)
const addStockError = ref('')
const addStockStatus = ref('')
const addStockWarning = ref('')
const closingPriceStatus = ref('')
const selectedHolding = ref<PortfolioValuationResponse['holdings'][number] | null>(null)
const holdingTransactions = ref<TransactionResponse[]>([])
const performanceTransactions = ref<TransactionResponse[]>([])
const allTransactions = ref<TransactionResponse[]>([])
const loadingTransactions = ref(false)
const loadingAllTransactions = ref(false)
const transactionsError = ref('')
const allTransactionsError = ref('')
const deletingTransactionId = ref<string | null>(null)
const latestDividends = ref<LatestDividendResponse[]>([])
const dividendCashFlows = ref<DividendCashFlow[]>([])
const loadingLatestDividends = ref(false)
const latestDividendsError = ref('')
const loadingDividendCashFlows = ref(false)
const dividendCashFlowsError = ref('')
const cashFlows = ref<PortfolioCashFlow[]>([])
const cashFlowsError = ref('')
const savingCashFlow = ref(false)
const cashFlowAmount = ref(0)
const cashFlowForm = ref<UpsertCashFlowRequest>({
  flowType: 'Deposit', amount: 0, effectiveDate: new Date().toISOString().split('T')[0], note: null,
})
const manualCashFlows = computed(() => cashFlows.value.filter((flow) => flow.flowType !== 'Dividend'))
const transactionPage = ref(1)
const transactionPageSize = 5
const allTransactionPage = ref(1)
const allTransactionPageSize = 10

const addStockForm = ref({
  transactionType: 'Buy' as 'Buy' | 'Sell',
  quantity: 0,
  price: 0,
  fee: 0,
  transactionDate: new Date().toISOString().split('T')[0],
  note: '',
})
const priceSource = ref<'Manual' | 'Close'>('Manual')

const timeRange = ref('1Y')
const showBenchmark = ref(false)
const hoveredSegment = ref<number | null>(null)
const hoveredIndustrySegment = ref<number | null>(null)
const timeRanges = ['1Y', '6M', '3M', '1M', 'YTD']
const allocationColors = ['#c9a86a', '#a8905e', '#8a7348', '#3d5470', '#2a3d55', '#1d2c42']
const securityResultGroups = computed(() => {
  const stocks: SecuritySearchResult[] = []
  const etfs: SecuritySearchResult[] = []
  const others: SecuritySearchResult[] = []

  for (const security of securityResults.value) {
    const assetType = security.assetType?.trim().toUpperCase() ?? ''
    const isTaiwanBondEtf = /^\d{5}B$/i.test(security.ticker.trim())
    if (assetType.includes('ETF') || isTaiwanBondEtf) {
      etfs.push(security)
    } else if (
      assetType === 'EQUITY'
      || assetType === 'STOCK'
      || assetType === 'COMMONSTOCK'
      || assetType === 'COMMON STOCK'
      || assetType.includes('股票')
    ) {
      stocks.push(security)
    } else {
      others.push(security)
    }
  }

  return [
    { label: '股票', results: stocks },
    { label: 'ETF', results: etfs },
    { label: '其他商品', results: others },
  ].filter((group) => group.results.length > 0)
})

function formatLocalDate(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function dateDaysBefore(dateString: string, days: number) {
  const [year, month, day] = dateString.split('-').map(Number)
  const date = new Date(year, month - 1, day)
  date.setDate(date.getDate() - days)
  return formatLocalDate(date)
}

const selectedDateRange = computed(() => {
  const to = new Date()
  const from = new Date(to)

  switch (timeRange.value) {
    case '6M':
      from.setMonth(from.getMonth() - 6)
      break
    case '3M':
      from.setMonth(from.getMonth() - 3)
      break
    case '1M':
      from.setMonth(from.getMonth() - 1)
      break
    case 'YTD':
      from.setMonth(0, 1)
      break
    default:
      from.setFullYear(from.getFullYear() - 1)
      break
  }

  return { from: formatLocalDate(from), to: formatLocalDate(to) }
})

const fromDate = computed(() => selectedDateRange.value.from)
const toDate = computed(() => selectedDateRange.value.to)

const valueTrendData = computed(() => {
  const allPoints = valuationHistory.value?.points.filter((p) => (p.totalAssetValue ?? p.totalMarketValue) > 0) ?? []
  const points = allPoints.filter((p) => p.date >= fromDate.value)
  if (points.length < 2) return null

  const normalized = performanceSeries.value
  const isBenchmarkMode = showBenchmark.value && normalized?.points.length === points.length
  const values = isBenchmarkMode
    ? normalized.values.map((v) => v / normalized.values[0] - 1)
    : points.map((p) => p.totalAssetValue ?? p.totalMarketValue)
  const max = Math.max(...values)
  const min = Math.min(...values)
  const step = (max - min) / 4 || 1
  return {
    labels: points.map((p) => p.date.slice(5)),
    values,
    yAxisLabels: Array.from({ length: 5 }, (_, i) => isBenchmarkMode ? formatPercent(max - step * i) : formatMoney(max - step * i, valuationHistory.value?.currency ?? '')),
  }
})

const benchmarkTrendData = computed(() => {
  if (!showBenchmark.value || !valueTrendData.value || !valuationHistory.value?.benchmark) return null
  const basePoint = valuationHistory.value.benchmark.find((point) => point.normalizedValue != null && point.normalizedValue > 0)
  if (!basePoint?.normalizedValue) return null
  const base = basePoint.normalizedValue
  const byDate = new Map(valuationHistory.value.benchmark.map((point) => [point.date.slice(5), point.normalizedValue]))
  const values = valueTrendData.value.labels.map((label) => {
    const nv = byDate.get(label)
    return nv != null ? nv / base - 1 : null
  })
  if (!values.every((value): value is number => value !== null)) return null
  values[0] = 0
  return values
})

const periodReturn = computed(() => {
  const values = performanceSeries.value?.values
  if (!values || values.length < 2) return null
  return values[values.length - 1] / values[0] - 1
})

const performanceSeries = computed(() => {
  // 必須使用使用者選擇的期間；不可將完整歷史的第一個極小餘額
  // 當成目前區間的起始淨值，否則報酬率會被不合理地放大。
  const points = valuationHistory.value?.points.filter((point) =>
    point.date >= fromDate.value
      && (point.totalAssetValue ?? point.totalMarketValue) > 0
      // 有持倉時，沒有任何有效價格的點不可作為績效基準。
      && (point.holdingCount === 0 || point.pricedHoldingCount > 0),
  ) ?? []
  if (points.length < 2) return null

  const values = [100]
  for (let index = 1; index < points.length; index += 1) {
    const previousValue = points[index - 1].totalAssetValue ?? points[index - 1].totalMarketValue
    const currentValue = points[index].totalAssetValue ?? points[index].totalMarketValue
    const externalCashFlow = points[index].externalCashFlow ?? 0
    const dailyReturn = previousValue > 0 ? (currentValue - externalCashFlow) / previousValue - 1 : 0
    values.push(values[index - 1] * (1 + dailyReturn))
  }

  return { points, values }
})

const kpiData = computed(() => {
  const v = valuation.value
  const pnlPercent = v?.totalUnrealizedPnlPercent ?? 0
  const pnlColor = pnlPercent >= 0 ? '#7fa387' : '#b05c5c'
  return [
    {
      label: 'TOTAL VALUE',
      value: v ? formatMoney(v.totalAssetValue ?? v.totalMarketValue, v.currency) : '—',
      sub: v ? `持股 ${formatMoney(v.totalMarketValue, v.currency)} · 現金 ${formatMoney(v.cashBalance ?? 0, v.currency)}` : '',
      valueStyle: { color: '#f5efe0' },
    },
    {
      label: 'UNREALIZED PNL',
      value: v ? formatPercent(pnlPercent) : '—',
      sub: v ? formatMoney(v.totalUnrealizedPnl, v.currency) : '',
      valueStyle: { color: pnlColor },
    },
    {
      label: 'REALIZED / TODAY',
      value: v ? formatMoney(v.totalRealizedPnl ?? 0, v.currency) : '—',
      sub: v?.todayPnl == null ? '今日損益 —' : `今日 ${formatMoney(v.todayPnl, v.currency)}`,
      valueStyle: { color: (v?.totalRealizedPnl ?? 0) >= 0 ? '#7fa387' : '#b05c5c' },
    },
    {
      label: `${timeRange.value} TWR`,
      value: valuationHistory.value?.twr == null ? '—' : formatPercent(valuationHistory.value.twr),
      valueStyle: { color: (valuationHistory.value?.twr ?? 0) >= 0 ? '#7fa387' : '#b05c5c' },
    },
    {
      label: `${timeRange.value} 年化 XIRR`,
      value: valuationHistory.value?.xirr == null ? '—' : formatPercent(valuationHistory.value.xirr),
      valueStyle: { color: (valuationHistory.value?.xirr ?? 0) >= 0 ? '#7fa387' : '#b05c5c' },
    },
    {
      label: 'BETA',
      value: valuationHistory.value?.beta == null ? '—' : valuationHistory.value.beta.toFixed(2),
      valueStyle: { color: '#f5efe0' },
    },
    {
      label: "JENSEN'S ALPHA",
      value: valuationHistory.value?.jensenAlpha == null ? '—' : formatPercent(valuationHistory.value.jensenAlpha),
      valueStyle: { color: (valuationHistory.value?.jensenAlpha ?? 0) >= 0 ? '#7fa387' : '#b05c5c' },
    },
  ]
})

const paginatedHoldingTransactions = computed(() => {
  const start = (transactionPage.value - 1) * transactionPageSize
  return holdingTransactions.value.slice(start, start + transactionPageSize)
})

const transactionPageCount = computed(() =>
  Math.ceil(holdingTransactions.value.length / transactionPageSize),
)

const paginatedAllTransactions = computed(() => {
  const start = (allTransactionPage.value - 1) * allTransactionPageSize
  return allTransactions.value.slice(start, start + allTransactionPageSize)
})

const allTransactionPageCount = computed(() =>
  Math.ceil(allTransactions.value.length / allTransactionPageSize),
)

const allocationSegments = computed(() => {
  return (
    valuation.value?.holdings.map((h, i) => ({
      label: h.ticker,
      value: h.weight ?? 0,
      color: allocationColors[i % allocationColors.length],
    })) ?? []
  )
})

const allocationRows = computed(() => {
  return (
    valuation.value?.holdings.map((h) => [
      h.ticker,
      h.securityName,
      h.exchange,
      formatNumber(h.quantity),
      h.latestPrice ? formatMoney(h.latestPrice, h.costCurrency) : '—',
      priceStatusLabel(h.valuationStatus),
      h.marketValue ? formatMoney(h.marketValue, valuation.value?.currency ?? '') : '—',
      h.weight ? `${(h.weight * 100).toFixed(2)}%` : '—',
      formatPercent(h.unrealizedPnlPercent),
    ]) ?? []
  )
})

const industrySegments = computed(() => {
  const groups = new Map<string, number>()
  valuation.value?.holdings.forEach((h) => {
    if (!h.marketValue) return
    const key = h.industry || '未分類'
    groups.set(key, (groups.get(key) || 0) + h.marketValue)
  })
  if (groups.size === 0) return []
  return Array.from(groups.entries())
    .sort((a, b) => b[1] - a[1])
    .map(([label, value], i) => ({
      label,
      value,
      color: allocationColors[i % allocationColors.length],
    }))
})

const industryRows = computed(() => {
  const total = industrySegments.value.reduce((sum, seg) => sum + seg.value, 0)
  return industrySegments.value.map((seg) => [
    seg.label,
    formatMoney(seg.value, valuation.value?.currency ?? ''),
    `${total > 0 ? ((seg.value / total) * 100).toFixed(2) : '0.00'}%`,
  ])
})

const hasMissingPrices = computed(() => {
  return valuation.value?.holdings.some((h) => h.valuationStatus !== 'Priced') ?? false
})

const riskMetrics = computed(() => {
  const r = risk.value
  return [
    { label: '波動率', value: r ? `${(r.historicalAnnualizedVolatility * 100).toFixed(2)}%` : '—' },
    { label: '最大回撤', value: r ? `${(r.maxDrawdown * 100).toFixed(2)}%` : '—' },
    { label: 'Sharpe Ratio', value: r ? r.sharpeRatio.toFixed(2) : '—' },
  ]
})

function computeCumulativeReturn(values: number[]) {
  if (values.length === 0) return []
  const base = values[0]
  return values.map((v) => (v / base - 1) * 100)
}

function computeEwmaVolatility(values: number[], lambda = 0.94) {
  const returns: number[] = []
  for (let i = 1; i < values.length; i++) {
    returns.push(values[i] / values[i - 1] - 1)
  }
  if (returns.length < 2) return []
  const initWindow = Math.min(20, returns.length)
  const mean = returns.slice(0, initWindow).reduce((a, b) => a + b, 0) / initWindow
  const initVar = returns.slice(0, initWindow).reduce((a, b) => a + (b - mean) ** 2, 0) / (initWindow - 1)
  const ewmaVars: number[] = []
  let prevVar = initVar
  for (const r of returns) {
    const v = lambda * prevVar + (1 - lambda) * r * r
    ewmaVars.push(v)
    prevVar = v
  }
  return ewmaVars.map((v) => Math.sqrt(v) * Math.sqrt(252) * 100)
}

function computeDrawdown(values: number[]) {
  let peak = values[0]
  let maxDD = 0
  let maxIdx = -1
  const dd = values.map((v, i) => {
    if (v > peak) peak = v
    const d = (v / peak - 1) * 100
    if (d < maxDD) {
      maxDD = d
      maxIdx = i
    }
    return d
  })
  return { values: dd, maxDrawdown: maxDD, maxIndex: maxIdx }
}

const cumReturnPoints = computed(() => {
  const series = performanceSeries.value
  if (!series) return null
  const { points, values } = series
  const cumReturns = computeCumulativeReturn(values)
  const labels = points.map((p) => p.date.slice(5))
  const max = Math.max(...cumReturns)
  const min = Math.min(...cumReturns)
  const range = max - min || 1
  return { labels, values: cumReturns, max, min, range }
})

const ewmaPoints = computed(() => {
  const series = performanceSeries.value
  if (!series || series.points.length < 22) return null
  const { points, values } = series
  const vols = computeEwmaVolatility(values)
  const labels = points.slice(1).map((p) => p.date.slice(5))
  const max = Math.max(...vols)
  const min = Math.min(...vols)
  const range = max - min || 1
  return { labels, values: vols, max, min, range }
})

const drawdownPoints = computed(() => {
  const series = performanceSeries.value
  if (!series) return null
  const { points, values } = series
  const { values: ddValues, maxDrawdown, maxIndex } = computeDrawdown(values)
  const labels = points.map((p) => p.date.slice(5))
  const min = Math.min(...ddValues)
  const range = -min || 1
  const maxDDDate = maxIndex >= 0 && maxIndex < labels.length ? labels[maxIndex] : ''
  return { labels, values: ddValues, maxDrawdown, maxIndex, maxDDDate, min, range }
})

function formatMoney(n: number, currency = '') {
  if (n === 0) return currency ? `0 ${currency}` : '0'
  const abs = Math.abs(n)
  const sign = n < 0 ? '-' : ''
  const suffix = currency ? ` ${currency}` : ''
  if (abs >= 1_000_000_000) return `${sign}${(abs / 1_000_000_000).toFixed(2)}B${suffix}`
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(2)}M${suffix}`
  if (abs >= 1_000) return `${sign}${(abs / 1_000).toFixed(2)}K${suffix}`
  return `${sign}${abs.toFixed(2)}${suffix}`
}

function formatNumber(n: number) {
  return n.toLocaleString('zh-TW')
}

function formatPercent(n: number | null) {
  if (n === null || n === undefined) return '—'
  return `${n >= 0 ? '+' : ''}${(n * 100).toFixed(2)}%`
}

function getEstimatedDividendAmount(dividend: LatestDividendResponse) {
  const holding = valuation.value?.holdings.find((h) => h.securityId === dividend.securityId)
  if (!holding) return '—'
  return formatMoney(holding.quantity * dividend.cashAmountPerShare, dividend.currency)
}

function portfolioTrendTooltip(index: number): string[] {
  const data = valueTrendData.value
  if (!data) return []
  const label = data.labels[index]

  if (showBenchmark.value) {
    const portfolioReturn = data.values[index]
    const benchmarkReturn = benchmarkTrendData.value?.[index]
    const lines = [label, `資產組合 ${formatPercent(portfolioReturn)}`]
    if (benchmarkReturn != null) {
      lines.push(`加權指數 ${formatPercent(benchmarkReturn)}`)
    }
    return lines
  }

  const value = data.values[index]
  const pnl = value / data.values[0] - 1
  return [
    label,
    formatMoney(value, valuationHistory.value?.currency ?? ''),
    formatPercent(pnl),
  ]
}

function priceStatusLabel(status: string) {
  if (status === 'Priced') return '市場報價'
  if (status === 'MissingPrice') return '缺價格'
  return status
}

function getApiErrorMessage(e: unknown, fallback: string) {
  const error = e as AxiosError<{ code?: string; message?: string }>
  const code = error.response?.data?.code
  const message = error.response?.data?.message
  if (code === 'risk.insufficient_prices') {
    return '此期間可用的共同日價格不足 120 筆（約半年），暫時無法估算風險；請改選較長期間或先同步價格資料。'
  }
  if (code && message) return `${code}: ${message}`
  if (message) return message
  return fallback
}

async function loadPortfolioData(showPageLoading = true) {
  if (showPageLoading) {
    loading.value = true
  }
  error.value = ''
  valuationError.value = ''
  try {
    portfolio.value = await getPortfolio(portfolioId.value)
    performanceTransactions.value = await getTransactions(portfolioId.value)
    await loadAllTransactions()

    try {
      valuation.value = await getPortfolioValuation(portfolioId.value)
      await loadLatestDividends()
      await loadDividendCashFlows()
      await loadCashFlows()
    } catch (e) {
      valuationError.value = '估值資料暫不可用。若投資組合尚無持倉或價格，這是正常狀態。'
    }

    await loadPeriodData()
  } catch (e) {
    error.value = '無法載入投資組合資料，請稍後再試。'
  } finally {
    if (showPageLoading) {
      loading.value = false
    }
  }
}

async function loadCashFlows() {
  try {
    cashFlows.value = await getPortfolioCashFlows(portfolioId.value)
    cashFlowsError.value = ''
  } catch {
    cashFlowsError.value = '無法載入現金流紀錄。'
  }
}

async function saveCashFlow() {
  if (!cashFlowAmount.value) { cashFlowsError.value = '請輸入現金變動金額。'; return }
  savingCashFlow.value = true
  try {
    await createPortfolioCashFlow(portfolioId.value, {
      flowType: cashFlowAmount.value > 0 ? 'Deposit' : 'Withdrawal',
      amount: Math.abs(cashFlowAmount.value),
      effectiveDate: cashFlowForm.value.effectiveDate,
      note: null,
    })
    cashFlowAmount.value = 0
    cashFlowForm.value = { flowType: 'Deposit', amount: 0, effectiveDate: new Date().toISOString().split('T')[0], note: null }
    await loadCashFlows()
    await loadPortfolioData(false)
  } catch (e) { cashFlowsError.value = getApiErrorMessage(e, '新增現金流失敗。') }
  finally { savingCashFlow.value = false }
}

async function editCashFlow(flow: PortfolioCashFlow) {
  const signedAmount = flow.flowType === 'Withdrawal' || flow.flowType === 'Fee' || flow.flowType === 'DividendTax' ? -flow.amount : flow.amount
  const amount = window.prompt('輸入現金變動（正數入金、負數出金）', String(signedAmount))
  if (amount === null) return
  const value = Number(amount)
  if (!Number.isFinite(value) || value === 0) { cashFlowsError.value = '金額不可為 0。'; return }
  try {
    await updatePortfolioCashFlow(portfolioId.value, flow.id, { flowType: value > 0 ? 'Deposit' : 'Withdrawal', amount: Math.abs(value), effectiveDate: flow.effectiveDate, note: null })
    await loadCashFlows(); await loadPortfolioData(false)
  } catch (e) { cashFlowsError.value = getApiErrorMessage(e, '修改現金流失敗。') }
}

async function removeCashFlow(flow: PortfolioCashFlow) {
  if (!window.confirm(`確定刪除 ${flow.effectiveDate} 的 ${flow.flowType} 現金流？`)) return
  try { await deletePortfolioCashFlow(portfolioId.value, flow.id); await loadCashFlows(); await loadPortfolioData(false) }
  catch (e) { cashFlowsError.value = getApiErrorMessage(e, '刪除現金流失敗。') }
}

async function loadPeriodData() {
  loadingPeriodData.value = true
  valuationHistoryError.value = ''
  riskError.value = ''

  const [historyResult, riskResult] = await Promise.allSettled([
    getPortfolioValuationHistory(portfolioId.value, {
      from: fromDate.value,
      to: toDate.value,
    }),
    getPortfolioRisk(portfolioId.value, {
      from: fromDate.value,
      to: toDate.value,
      horizonDays: 30,
      confidenceLevel: 0.95,
      simulations: 10000,
    }),
  ])

  if (historyResult.status === 'fulfilled') {
    valuationHistory.value = historyResult.value
  } else {
    valuationHistory.value = null
    valuationHistoryError.value = getApiErrorMessage(
      historyResult.reason,
      '歷史估值資料暫不可用，無法顯示此期間的價值走勢。',
    )
  }

  if (riskResult.status === 'fulfilled') {
    risk.value = riskResult.value
  } else {
    risk.value = null
    riskError.value = getApiErrorMessage(
      riskResult.reason,
      '市場價格或歷史價格不足，暫無法計算此期間的風險分析。',
    )
  }

  loadingPeriodData.value = false
}

async function selectTimeRange(range: string) {
  if (timeRange.value === range || loadingPeriodData.value) return
  timeRange.value = range
  await loadPeriodData()
}

async function handleSearchSecurities() {
  if (!securityQuery.value.trim()) return
  searchingSecurities.value = true
  addStockError.value = ''
  try {
    securityResults.value = await searchSecurities(securityQuery.value.trim())
    if (securityResults.value.length === 1) {
      selectSecurity(securityResults.value[0])
    } else if (securityResults.value.length === 0) {
      selectedSecurity.value = null
      addStockError.value = '找不到符合的股票，請確認代號或交易所。'
    }
  } catch (e) {
    addStockError.value = '搜尋股票失敗，請稍後再試。'
  } finally {
    searchingSecurities.value = false
  }
}

async function selectSecurity(security: SecuritySearchResult) {
  selectedSecurity.value = security
  securityQuery.value = `${security.ticker} ${security.exchange}`
  securityResults.value = []
  closingPriceStatus.value = ''
  if (priceSource.value === 'Close') {
    await useClosingPrice()
  }
}

async function useClosingPrice() {
  const security = selectedSecurity.value ?? securityResults.value[0] ?? null
  if (!security) {
    addStockError.value = '請先搜尋並選擇股票，才能帶入收盤價。'
    return
  }

  loadingClosePrice.value = true
  addStockError.value = ''
  closingPriceStatus.value = ''
  try {
    const resolved = await resolveSecurity({
      securityId: security.securityId,
      ticker: security.securityId ? null : security.ticker,
      exchange: security.securityId ? null : security.exchange,
    })
    await syncSecurityPrices(resolved.securityId, 365, false)
    const prices = await getSecurityPrices(resolved.securityId, {
      from: dateDaysBefore(addStockForm.value.transactionDate, 14),
      to: addStockForm.value.transactionDate,
    })
    const closingPrice = [...prices]
      .filter((price) => price.interval === '1d' && price.priceTime.slice(0, 10) <= addStockForm.value.transactionDate)
      .sort((a, b) => b.priceTime.localeCompare(a.priceTime))[0]

    if (!closingPrice) {
      addStockError.value = '找不到交易日期當日或之前的收盤價，請改用自訂成交價。'
      return
    }

    selectedSecurity.value = {
      ...security,
      securityId: resolved.securityId,
    }
    addStockForm.value.price = closingPrice.close
    closingPriceStatus.value = `已帶入 ${closingPrice.priceTime.slice(0, 10)} 收盤價。`
  } catch (e) {
    addStockError.value = getApiErrorMessage(e, '無法取得收盤價，請改用自訂成交價或稍後再試。')
  } finally {
    loadingClosePrice.value = false
  }
}

async function handlePriceSourceChange() {
  closingPriceStatus.value = ''
  if (priceSource.value === 'Close') {
    await useClosingPrice()
  }
}

async function handleTransactionDateChange() {
  if (priceSource.value === 'Close') {
    await useClosingPrice()
  }
}

async function loadHoldingTransactions(securityId: string) {
  loadingTransactions.value = true
  transactionsError.value = ''
  try {
    const transactions = await getTransactions(portfolioId.value, securityId)
    holdingTransactions.value = [...transactions].sort((a, b) => {
      const dateCompare = b.transactionDate.localeCompare(a.transactionDate)
      return dateCompare || b.createdAtUtc.localeCompare(a.createdAtUtc)
    })
    transactionPage.value = 1
  } catch (e) {
    holdingTransactions.value = []
    transactionsError.value = '無法載入交易紀錄，請稍後再試。'
  } finally {
    loadingTransactions.value = false
  }
}

async function loadAllTransactions() {
  loadingAllTransactions.value = true
  allTransactionsError.value = ''
  try {
    const transactions = await getTransactions(portfolioId.value)
    allTransactions.value = [...transactions].sort((a, b) => {
      const dateCompare = b.transactionDate.localeCompare(a.transactionDate)
      return dateCompare || b.createdAtUtc.localeCompare(a.createdAtUtc)
    })
    allTransactionPage.value = 1
  } catch (e) {
    allTransactions.value = []
    allTransactionsError.value = '無法載入交易紀錄，請稍後再試。'
  } finally {
    loadingAllTransactions.value = false
  }
}

async function loadLatestDividends() {
  loadingLatestDividends.value = true
  latestDividendsError.value = ''
  try {
    latestDividends.value = await getLatestDividends(portfolioId.value)
  } catch (e) {
    latestDividends.value = []
    latestDividendsError.value = '無法載入股息資訊，請稍後再試。'
  } finally {
    loadingLatestDividends.value = false
  }
}

async function loadDividendCashFlows() {
  loadingDividendCashFlows.value = true
  dividendCashFlowsError.value = ''
  try {
    dividendCashFlows.value = await getDividendCashFlows(portfolioId.value)
  } catch (e) {
    dividendCashFlows.value = []
    dividendCashFlowsError.value = getApiErrorMessage(e, '無法載入股息紀錄，請稍後再試。')
  } finally {
    loadingDividendCashFlows.value = false
  }
}

function dividendStatusLabel(status: DividendCashFlow['status']) {
  if (status === 'Posted') return '已入帳'
  if (status === 'Scheduled') return '預計入帳'
  return '已略過'
}

function selectHolding(holding: PortfolioValuationResponse['holdings'][number]) {
  selectedHolding.value = holding
  addStockError.value = ''
  addStockStatus.value = ''
  selectedSecurity.value = {
    securityId: holding.securityId,
    ticker: holding.ticker,
    exchange: holding.exchange,
    name: holding.securityName,
    assetType: null,
    currency: holding.costCurrency,
    isin: null,
    sector: null,
    industry: null,
    source: 'Portfolio',
  }
  securityResults.value = []
  securityQuery.value = `${holding.ticker} ${holding.exchange}`
}

async function openHoldingTransactions(index: number) {
  const holding = valuation.value?.holdings[index]
  if (!holding) return

  showAddStock.value = false
  selectHolding(holding)
  await loadHoldingTransactions(holding.securityId)
}

function openTransactionForm(transactionType: 'Buy' | 'Sell') {
  if (!selectedHolding.value) return
  selectHolding(selectedHolding.value)
  addStockForm.value.transactionType = transactionType
  showAddStock.value = true
}

function openClosePositionForm() {
  if (!selectedHolding.value) return
  selectHolding(selectedHolding.value)
  addStockForm.value.transactionType = 'Sell'
  addStockForm.value.quantity = selectedHolding.value.quantity
  addStockForm.value.price = selectedHolding.value.latestPrice ?? 0
  addStockForm.value.fee = 0
  addStockForm.value.transactionDate = new Date().toISOString().split('T')[0]
  addStockForm.value.note = ''
  showAddStock.value = true
}

function toggleNewStockForm() {
  showAddStock.value = !showAddStock.value
  if (showAddStock.value) {
    selectedHolding.value = null
    selectedSecurity.value = null
    securityQuery.value = ''
    securityResults.value = []
  }
}

async function handleDeleteTransaction(transaction: TransactionResponse) {
  if (!window.confirm(`確定要刪除 ${transaction.transactionDate} 的這筆${transaction.transactionType === 'BUY' ? '買入' : '賣出'}紀錄嗎？`)) {
    return
  }

  deletingTransactionId.value = transaction.id
  transactionsError.value = ''
  try {
    await deleteTransaction(portfolioId.value, transaction.id)
    await loadPortfolioData(false)
    await loadAllTransactions()
    if (selectedHolding.value) {
      const refreshedHolding = valuation.value?.holdings.find(
        (holding) => holding.securityId === selectedHolding.value?.securityId,
      )
      if (refreshedHolding) {
        selectedHolding.value = refreshedHolding
        await loadHoldingTransactions(refreshedHolding.securityId)
      } else {
        selectedHolding.value = null
        holdingTransactions.value = []
      }
    }
  } catch (e) {
    transactionsError.value = getApiErrorMessage(e, '刪除交易失敗，請稍後再試。')
  } finally {
    deletingTransactionId.value = null
  }
}

async function handleDeleteAllTransaction(transaction: TransactionResponse) {
  if (!window.confirm(`確定要刪除 ${transaction.transactionDate} 的這筆${transaction.transactionType === 'BUY' ? '買入' : '賣出'}紀錄嗎？`)) {
    return
  }

  deletingTransactionId.value = transaction.id
  allTransactionsError.value = ''
  try {
    await deleteTransaction(portfolioId.value, transaction.id)
    await loadPortfolioData(false)
    await loadAllTransactions()
    if (selectedHolding.value) {
      const refreshedHolding = valuation.value?.holdings.find(
        (holding) => holding.securityId === selectedHolding.value?.securityId,
      )
      if (refreshedHolding) {
        selectedHolding.value = refreshedHolding
        await loadHoldingTransactions(refreshedHolding.securityId)
      } else {
        selectedHolding.value = null
        holdingTransactions.value = []
      }
    }
  } catch (e) {
    allTransactionsError.value = getApiErrorMessage(e, '刪除交易失敗，請稍後再試。')
  } finally {
    deletingTransactionId.value = null
  }
}

async function handleAddStock() {
  let security = selectedSecurity.value ?? securityResults.value[0] ?? null
  if (!security && securityQuery.value.trim()) {
    try {
      const results = await searchSecurities(securityQuery.value.trim())
      securityResults.value = results
      security = results[0] ?? null
    } catch (e) {
      addStockError.value = '搜尋股票失敗，請稍後再試。'
      return
    }
  }

  if (!security) {
    addStockError.value = '請先搜尋股票，或從搜尋結果選擇一筆股票。'
    return
  }
  if (addStockForm.value.quantity <= 0 || addStockForm.value.price <= 0) {
    addStockError.value = '數量與成交價必須大於 0。'
    return
  }

  addingStock.value = true
  addStockError.value = ''
  addStockWarning.value = ''
  try {
    addStockStatus.value = '正在解析股票資料...'
    const resolved = await resolveSecurity({
      securityId: security.securityId,
      ticker: security.securityId ? null : security.ticker,
      exchange: security.securityId ? null : security.exchange,
    })

    addStockStatus.value = '正在同步近一年日線...'
    try {
      await syncSecurityPrices(resolved.securityId, 365, false)
    } catch (e) {
      addStockWarning.value = getApiErrorMessage(
        e,
        '近一年日線同步失敗；持倉會建立，但估值與風險可能暫不可用。',
      )
    }

    addStockStatus.value = '正在新增交易...'
    await createTransaction(portfolioId.value, {
      securityId: resolved.securityId,
      transactionType: addStockForm.value.transactionType,
      quantity: addStockForm.value.quantity,
      price: addStockForm.value.price,
      fee: addStockForm.value.fee || 0,
      transactionDate: addStockForm.value.transactionDate,
      note: addStockForm.value.note || null,
    })

    const selectedSecurityId = resolved.securityId
    showAddStock.value = false
    securityResults.value = []
    addStockForm.value = {
      transactionType: 'Buy',
      quantity: 0,
      price: 0,
      fee: 0,
      transactionDate: new Date().toISOString().split('T')[0],
      note: '',
    }
    priceSource.value = 'Manual'
    closingPriceStatus.value = ''
    await loadPortfolioData(false)
    if (selectedSecurityId) {
      const refreshedHolding = valuation.value?.holdings.find(
        (holding) => holding.securityId === selectedSecurityId,
      )
      if (refreshedHolding) {
        selectedHolding.value = refreshedHolding
        selectHolding(refreshedHolding)
        await loadHoldingTransactions(refreshedHolding.securityId)
      }
    }
  } catch (e) {
    addStockError.value = getApiErrorMessage(e, '新增股票失敗，請確認股票、數量與成交價是否正確。')
  } finally {
    addingStock.value = false
    addStockStatus.value = ''
  }
}

onMounted(loadPortfolioData)
</script>


<template>
  <div class="prestige-page">
    <div class="prestige-section" style="padding-top: 48px">
      <!-- Back + Header -->
      <button class="prestige-btn small back-btn" @click="router.push({ name: 'portfolios' })">
        ← BACK TO PORTFOLIOS
      </button>

      <div v-if="loading" class="skeleton-stack">
        <div v-for="i in 3" :key="i" class="prestige-skeleton" />
      </div>
      <div v-else-if="error" class="prestige-error">{{ error }}</div>

      <template v-if="!loading && portfolio">
        <div class="detail-header prestige-fade">
          <div>
            <h1 class="detail-title">{{ portfolio.name }}</h1>
            <span class="caption" style="margin-top: 4px; display: block">{{ portfolio.description || portfolio.baseCurrency }} PORTFOLIO</span>
          </div>
          <div style="display: flex; gap: 8px">
            <span class="prestige-tag prestige-mono">{{ portfolio.baseCurrency }}</span>
            <span class="caption" style="align-self: center">Last updated: {{ new Date(portfolio.updatedAtUtc).toLocaleDateString('zh-TW') }}</span>
          </div>
        </div>

        <div v-if="valuationError" class="prestige-warning" style="margin-bottom: 20px">
          {{ valuationError }}
        </div>

        <div v-if="riskError" class="prestige-warning" style="margin-bottom: 20px">
          {{ riskError }}
        </div>

        <!-- Portfolio Value Trend -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="prestige-panel trend-section">
            <!-- Header -->
            <div class="trend-header">
              <div class="trend-title-group">
                <div class="trend-title">
                  <h2 class="trend-main-title">投資組合價值走勢</h2>
                  <span class="caption" style="margin-top: 6px; display: block">PORTFOLIO VALUE TREND · {{ fromDate }} — {{ toDate }}</span>
                </div>
                <div class="trend-metrics">
                  <div v-for="(kpi, i) in kpiData" :key="i" class="trend-metric-item"
                  >
                    <span class="caption">{{ kpi.label }}</span>
                    <span class="trend-metric-value prestige-mono" :style="kpi.valueStyle">{{ kpi.value }}</span>
                    <span v-if="kpi.sub" class="trend-metric-sub">{{ kpi.sub }}</span>
                  </div>
                </div>
              </div>
              <div class="trend-pnl-range">
                <div v-if="periodReturn !== null" class="trend-pnl">
                  <div class="trend-pnl-value prestige-mono" :class="periodReturn >= 0 ? 'pnl-up' : 'pnl-down'">
                    {{ formatPercent(periodReturn) }}
                  </div>
                  <span class="caption">區間報酬</span>
                </div>
                <div class="time-range">
                  <button
                    v-for="r in timeRanges"
                    :key="r"
                    :class="['time-btn', timeRange === r && 'active']"
                    :disabled="loadingPeriodData"
                    @click="selectTimeRange(r)"
                  >
                    {{ r }}
                  </button>
                </div>
                <button class="time-btn" :class="{ active: showBenchmark }" @click="showBenchmark = !showBenchmark">與台股加權含息指數比較</button>
                <span v-if="showBenchmark && !benchmarkTrendData" class="caption" style="display:block; margin-top:8px">基準暫時不可用，僅顯示組合走勢。</span>
                <span v-if="timeRange !== '1Y' && timeRange !== 'ALL'" class="caption" style="display:block; margin-top:8px">XIRR 為年化報酬，非本區間累積報酬。</span>
              </div>
            </div>

            <!-- Chart -->
            <div class="trend-chart-wrap">
              <div v-if="loadingPeriodData" class="period-loading">正在更新 {{ timeRange }} 資料…</div>
              <LineChart
                v-if="valueTrendData"
                :data="valueTrendData.values"
                :labels="valueTrendData.labels"
                :y-axis-labels="valueTrendData.yAxisLabels"
                :height="420"
                line-color="#c9a86a"
                fill-color="rgba(201, 168, 106, 0.08)"
                grid-color="rgba(201, 168, 106, 0.14)"
                text-color="#9a917c"
                :dark="true"
                :show-area="true"
                :point-threshold="25"
                :show-tooltip="true"
                :tooltip-formatter="portfolioTrendTooltip"
                :second-line="benchmarkTrendData ?? undefined"
                second-line-color="#7fa387"
              />
              <div v-else class="insufficient-history">
                {{ valuationHistoryError || '歷史估值資料不足兩筆，暫時無法顯示走勢圖。' }}
              </div>
            </div>

            <!-- Performance & Risk grid -->
            <div class="trend-perf-grid">
              <!-- Cumulative Return -->
              <div class="perf-card">
                <div class="perf-card-header">
                  <span class="perf-card-title">累積報酬率</span>
                  <span class="caption">CUMULATIVE RETURN</span>
                </div>
                <div class="perf-chart-wrap">
                  <svg v-if="cumReturnPoints" width="100%" height="100%" viewBox="0 0 400 180" preserveAspectRatio="none">
                    <line v-for="i in 5" :key="'g-' + i" x1="50" :y1="16 + (i - 1) * 34" x2="380" :y2="16 + (i - 1) * 34" stroke="rgba(201, 168, 106, 0.14)" stroke-width="1" stroke-dasharray="4 4" />
                    <line v-if="0 >= cumReturnPoints!.min && 0 <= cumReturnPoints!.max" x1="50" :y1="16 + ((cumReturnPoints!.max - 0) / cumReturnPoints!.range) * 136" x2="380" :y2="16 + ((cumReturnPoints!.max - 0) / cumReturnPoints!.range) * 136" stroke="rgba(154, 145, 124, 0.5)" stroke-width="1" />
                    <polyline :points="cumReturnPoints!.values.map((v, i) => `${50 + (i / (cumReturnPoints!.values.length - 1)) * 330},${16 + ((cumReturnPoints!.max - v) / cumReturnPoints!.range) * 136}`).join(' ')" fill="none" stroke="#c9a86a" stroke-width="1.5" />
                    <text v-for="i in 6" :key="'y-' + i" x="45" :y="16 + (i - 1) * 34 + 4" text-anchor="end" fill="#9a917c" font-size="9">{{ (cumReturnPoints!.max - ((i - 1) / 5) * cumReturnPoints!.range).toFixed(1) }}%</text>
                  </svg>
                  <div v-else class="insufficient-history-sm">資料不足</div>
                </div>
              </div>

              <!-- EWMA Rolling Volatility -->
              <div class="perf-card">
                <div class="perf-card-header">
                  <span class="perf-card-title">滾動波動率 EWMA</span>
                  <span class="caption">ROLLING VOLATILITY</span>
                </div>
                <div class="perf-chart-wrap">
                  <svg v-if="ewmaPoints" width="100%" height="100%" viewBox="0 0 400 180" preserveAspectRatio="none">
                    <line v-for="i in 5" :key="'g-' + i" x1="50" :y1="16 + (i - 1) * 34" x2="380" :y2="16 + (i - 1) * 34" stroke="rgba(201, 168, 106, 0.14)" stroke-width="1" stroke-dasharray="4 4" />
                    <polyline :points="ewmaPoints!.values.map((v, i) => `${50 + (i / (ewmaPoints!.values.length - 1)) * 330},${16 + ((ewmaPoints!.max - v) / ewmaPoints!.range) * 136}`).join(' ')" fill="none" stroke="#a8905e" stroke-width="1.5" />
                    <text v-for="i in 6" :key="'y-' + i" x="45" :y="16 + (i - 1) * 34 + 4" text-anchor="end" fill="#9a917c" font-size="9">{{ (ewmaPoints!.max - ((i - 1) / 5) * ewmaPoints!.range).toFixed(1) }}%</text>
                  </svg>
                  <div v-else class="insufficient-history-sm">至少需要 22 筆資料</div>
                </div>
              </div>

              <!-- Historical Drawdown -->
              <div class="perf-card">
                <div class="perf-card-header">
                  <span class="perf-card-title">歷史回撤</span>
                  <span class="caption">DRAWDOWN</span>
                </div>
                <div class="perf-chart-wrap">
                  <svg v-if="drawdownPoints" width="100%" height="100%" viewBox="0 0 400 180" preserveAspectRatio="none">
                    <line v-for="i in 5" :key="'g-' + i" x1="50" :y1="16 + (i - 1) * 34" x2="380" :y2="16 + (i - 1) * 34" stroke="rgba(201, 168, 106, 0.14)" stroke-width="1" stroke-dasharray="4 4" />
                    <polygon :points="`50,152 ${drawdownPoints!.values.map((v, i) => `${50 + (i / (drawdownPoints!.values.length - 1)) * 330},${16 + ((0 - v) / drawdownPoints!.range) * 136}`).join(' ')} 380,152`" fill="rgba(176, 92, 92, 0.12)" />
                    <polyline :points="drawdownPoints!.values.map((v, i) => `${50 + (i / (drawdownPoints!.values.length - 1)) * 330},${16 + ((0 - v) / drawdownPoints!.range) * 136}`).join(' ')" fill="none" stroke="#b05c5c" stroke-width="1.5" />
                    <line v-if="drawdownPoints!.maxIndex >= 0" :x1="50 + (drawdownPoints!.maxIndex / (drawdownPoints!.values.length - 1)) * 330" :y1="16 + ((0 - drawdownPoints!.maxDrawdown) / drawdownPoints!.range) * 136" :x2="50 + (drawdownPoints!.maxIndex / (drawdownPoints!.values.length - 1)) * 330" :y2="152" stroke="#f5efe0" stroke-width="1" stroke-dasharray="4 4" />
                    <text v-if="drawdownPoints!.maxIndex >= 0" :x="Math.min(370, Math.max(30, 50 + (drawdownPoints!.maxIndex / (drawdownPoints!.values.length - 1)) * 330))" :y="Math.max(20, 16 + ((0 - drawdownPoints!.maxDrawdown) / drawdownPoints!.range) * 136 - 6)" fill="#f5efe0" font-size="9" font-weight="600" text-anchor="middle">{{ drawdownPoints!.maxDrawdown.toFixed(2) }}%</text>
                    <text v-for="i in 5" :key="'y-' + i" x="45" :y="16 + (i - 1) * 34 + 4" text-anchor="end" fill="#9a917c" font-size="9">{{ (-(i - 1) / 4 * drawdownPoints!.range).toFixed(1) }}%</text>
                  </svg>
                  <div v-else class="insufficient-history-sm">資料不足</div>
                </div>
              </div>

              <!-- Risk Metrics -->
              <div class="perf-card perf-card-metrics">
                <div class="perf-card-header">
                  <span class="perf-card-title">風險指標</span>
                  <span class="caption">RISK METRICS</span>
                </div>
                <div class="perf-metrics-list">
                  <div v-for="(r, i) in riskMetrics" :key="i" class="perf-metric-row"
                  >
                    <span class="caption">{{ r.label }}</span>
                    <span class="perf-metric-value prestige-mono">{{ r.value }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Asset Allocation -->
        <ScrollReveal :delay="0.15" style="margin-top: 60px">
          <div class="prestige-panel allocation-section">
            <!-- Donut -->
            <div class="allocation-chart">
              <DonutChart
                :segments="allocationSegments"
                :center-label="valuation ? formatMoney(valuation.totalMarketValue, valuation.currency) : '—'"
                :center-sub-label="`${allocationRows.length} 檔持倉`"
                :active-index="hoveredSegment"
                :dark="true"
                @segment-hover="(i) => hoveredSegment = i"
              />
            </div>
            <!-- Holdings -->
            <div class="allocation-holdings">
              <div class="allocation-holdings-header">
                <div>
                  <h2 class="panel-title">持倉明細</h2>
                  <span class="caption">HOLDINGS</span>
                </div>
                <button class="prestige-btn prestige-btn-solid small" @click="toggleNewStockForm">
                  {{ showAddStock ? 'CANCEL' : '+ ADD STOCK' }}
                </button>
              </div>

              <div v-if="selectedHolding && !showAddStock" class="prestige-panel prestige-panel-pad inner-panel">
                <div class="inner-panel-head">
                  <div>
                    <h3 class="inner-panel-title">
                      {{ selectedHolding.ticker }} 交易紀錄
                    </h3>
                    <span class="caption">{{ selectedHolding.exchange }} · {{ selectedHolding.securityName }}</span>
                  </div>
                  <div style="display: flex; gap: 8px">
                    <button class="prestige-btn small" @click="openTransactionForm('Buy')">買入</button>
                    <button class="prestige-btn prestige-btn-solid small" @click="openTransactionForm('Sell')">賣出</button>
                    <button class="prestige-btn small" @click="openClosePositionForm">平倉</button>
                  </div>
                </div>

                <div v-if="loadingTransactions" class="transaction-empty">載入交易紀錄中...</div>
                <div v-else-if="transactionsError" class="prestige-error">{{ transactionsError }}</div>
                <div v-else-if="!holdingTransactions.length" class="transaction-empty">尚無交易紀錄。</div>
                <div v-else style="overflow-x: auto">
                  <table class="prestige-table transaction-table">
                    <thead>
                      <tr>
                        <th>日期</th>
                        <th>類型</th>
                        <th>數量</th>
                        <th>成交價</th>
                        <th>手續費</th>
                        <th>FIFO 損益</th>
                        <th>備註</th>
                        <th aria-label="操作" />
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="transaction in paginatedHoldingTransactions" :key="transaction.id">
                        <td>{{ transaction.transactionDate }}</td>
                        <td :class="transaction.transactionType === 'BUY' ? 'tx-buy' : 'tx-sell'">
                          {{ transaction.transactionType === 'BUY' ? '買入' : '賣出' }}
                        </td>
                        <td>{{ formatNumber(transaction.quantity) }}</td>
                        <td>{{ formatMoney(transaction.price, selectedHolding.costCurrency) }}</td>
                        <td>{{ formatMoney(transaction.fee, selectedHolding.costCurrency) }}</td>
                        <td>{{ transaction.realizedPnl == null ? '—' : formatMoney(transaction.realizedPnl, selectedHolding.costCurrency) }}</td>
                        <td>{{ transaction.note || '—' }}</td>
                        <td>
                          <button
                            class="transaction-delete"
                            :disabled="deletingTransactionId === transaction.id"
                            @click="handleDeleteTransaction(transaction)"
                          >
                            {{ deletingTransactionId === transaction.id ? '刪除中...' : '刪除' }}
                          </button>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                  <div
                    v-if="transactionPageCount > 1"
                    style="margin-top: 16px; display: flex; justify-content: flex-end"
                  >
                    <NPagination
                      v-model:page="transactionPage"
                      :page-size="transactionPageSize"
                      :item-count="holdingTransactions.length"
                    />
                  </div>
                </div>
              </div>

              <div v-if="showAddStock" class="prestige-panel prestige-panel-pad inner-panel">
                <div style="margin-bottom: 14px">
                  <h3 class="inner-panel-title">新增股票交易</h3>
                  <span class="caption">先搜尋股票，選取結果後填入交易資料</span>
                </div>

                <label class="field-label">股票搜尋</label>
                <div style="display: grid; grid-template-columns: 2fr auto; gap: 12px; margin-bottom: 12px">
                  <input
                    v-model="securityQuery"
                    class="prestige-input"
                    placeholder="搜尋股票代號或名稱，例如 TSM / AAPL / 2330"
                    @keyup.enter="handleSearchSecurities"
                  />
                  <button class="prestige-btn small" :disabled="searchingSecurities" @click="handleSearchSecurities">
                    {{ searchingSecurities ? 'SEARCHING...' : 'SEARCH' }}
                  </button>
                </div>
                <div v-if="securityResults.length" class="security-results">
                  <template v-for="group in securityResultGroups" :key="group.label">
                    <div class="security-result-group-label">{{ group.label }}</div>
                    <button
                      v-for="security in group.results"
                      :key="`${security.ticker}-${security.exchange}-${security.source}`"
                      class="security-result-btn"
                      @click="selectSecurity(security)"
                    >
                      <span class="sr-ticker">{{ security.ticker }}</span>
                      <span class="sr-dim">{{ security.exchange }}</span>
                      <span>{{ security.name }}</span>
                      <span class="sr-source">{{ security.source }}</span>
                    </button>
                  </template>
                </div>

                <div v-if="selectedSecurity" class="selected-security">
                  已選擇：{{ selectedSecurity.ticker }} / {{ selectedSecurity.exchange }} / {{ selectedSecurity.name }}
                </div>

                <div class="form-note" style="margin-bottom: 16px">
                  成交價只用於計算交易成本與平均成本；目前市值、損益與風險分析會使用市場價格資料。
                </div>

                <div style="display: grid; grid-template-columns: repeat(5, 1fr); gap: 12px; margin-bottom: 12px">
                  <label>
                    <span class="field-label">交易類型</span>
                    <select v-model="addStockForm.transactionType" class="prestige-input">
                      <option value="Buy">Buy 買入</option>
                      <option value="Sell">Sell 賣出</option>
                    </select>
                  </label>
                  <label>
                    <span class="field-label">數量</span>
                    <input v-model.number="addStockForm.quantity" class="prestige-input" type="number" min="0" step="0.0001" placeholder="例如 10" />
                  </label>
                  <label>
                    <span class="field-label">成交價來源</span>
                    <select v-model="priceSource" class="prestige-input" @change="handlePriceSourceChange">
                      <option value="Manual">自訂成交價</option>
                      <option value="Close">收盤價</option>
                    </select>
                  </label>
                  <label>
                    <span class="field-label">成交價（交易成本）</span>
                    <input
                      v-model.number="addStockForm.price"
                      class="prestige-input"
                      type="number"
                      min="0"
                      step="0.0001"
                      placeholder="例如 150.5"
                      :disabled="priceSource === 'Close'"
                    />
                  </label>
                  <label>
                    <span class="field-label">手續費</span>
                    <input v-model.number="addStockForm.fee" class="prestige-input" type="number" min="0" step="0.01" placeholder="可填 0" />
                  </label>
                </div>

                <div v-if="priceSource === 'Close'" class="form-note" style="margin: -4px 0 12px">
                  {{ loadingClosePrice ? '正在帶入收盤價...' : (closingPriceStatus || '使用交易日期當日或之前最近一個交易日的收盤價。') }}
                </div>

                <div style="display: grid; grid-template-columns: 1fr 2fr auto; gap: 12px; align-items: center">
                  <label>
                    <span class="field-label">交易日期</span>
                    <input v-model="addStockForm.transactionDate" class="prestige-input" type="date" @change="handleTransactionDateChange" />
                  </label>
                  <label>
                    <span class="field-label">備註</span>
                    <input v-model="addStockForm.note" class="prestige-input" placeholder="選填，例如首次買入" />
                  </label>
                  <button class="prestige-btn prestige-btn-solid small" :disabled="addingStock" @click="handleAddStock">
                    {{ addingStock ? 'ADDING...' : 'ADD' }}
                  </button>
                </div>

                <div v-if="addStockError" class="prestige-error" style="margin-top: 12px">
                  {{ addStockError }}
                </div>
                <div v-if="addStockStatus" class="form-note" style="margin-top: 12px">
                  {{ addStockStatus }}
                </div>
              </div>

              <div v-if="addStockWarning" class="prestige-warning" style="margin-bottom: 12px">
                {{ addStockWarning }}
              </div>

              <div v-if="allocationRows.length" class="prestige-data-table">
                <DataTable
                  :headers="['代碼', '名稱', '交易所', '持有股數', '現價', '價格狀態', '市值', '占比', '損益']"
                  :rows="allocationRows"
                  :highlight-row="hoveredSegment"
                  :dark="true"
                  @row-hover="(i) => hoveredSegment = i"
                  @row-click="openHoldingTransactions"
                />
              </div>
              <div v-if="hasMissingPrices" class="prestige-warning" style="margin-top: 12px">
                部分持倉尚無市場價格，因此市值、損益、資產走勢與風險分析可能暫不可用或不完整。
              </div>
              <div v-if="!allocationRows.length" class="prestige-empty">
                此投資組合尚無持倉。新增持倉後會顯示估值與權重明細。
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Industry Allocation -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="prestige-panel allocation-section">
            <div class="allocation-chart">
              <div style="text-align: center; margin-bottom: 16px">
                <h2 class="panel-title">產業配置</h2>
                <span class="caption">INDUSTRY ALLOCATION</span>
              </div>
              <DonutChart
                v-if="industrySegments.length"
                :segments="industrySegments"
                :center-label="valuation ? formatMoney(industrySegments.reduce((s, seg) => s + seg.value, 0), valuation.currency) : '—'"
                :center-sub-label="`${industrySegments.length} 個產業`"
                :active-index="hoveredIndustrySegment"
                :dark="true"
                @segment-hover="(i) => hoveredIndustrySegment = i"
              />
              <div v-else class="insufficient-history" style="min-height: 300px">
                尚無產業分類資料。
              </div>
            </div>
            <div class="allocation-holdings">
              <div class="allocation-holdings-header">
                <div>
                  <h2 class="panel-title">產業明細</h2>
                  <span class="caption">INDUSTRY BREAKDOWN</span>
                </div>
              </div>
              <div v-if="industryRows.length" class="prestige-data-table">
                <DataTable
                  :headers="['產業', '市值', '占比']"
                  :rows="industryRows"
                  :highlight-row="hoveredIndustrySegment"
                  :dark="true"
                  @row-hover="(i) => hoveredIndustrySegment = i"
                />
              </div>
              <div v-else class="prestige-empty">
                此投資組合尚無產業分類資料。
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Transaction Ledger -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="prestige-panel">
            <div class="panel-head">
              <h2 class="panel-title">交易帳</h2>
              <span class="caption">TRANSACTION LEDGER — 所有買賣紀錄</span>
            </div>
            <div class="panel-body">
              <div v-if="loadingAllTransactions" class="transaction-empty">載入交易紀錄中...</div>
              <div v-else-if="allTransactionsError" class="prestige-error">{{ allTransactionsError }}</div>
              <div v-else-if="!allTransactions.length" class="transaction-empty">尚無交易紀錄。</div>
              <div v-else style="overflow-x: auto">
                <table class="prestige-table transaction-table">
                  <thead>
                    <tr>
                      <th>日期</th>
                      <th>類型</th>
                      <th>代碼</th>
                      <th>名稱</th>
                      <th>數量</th>
                      <th>成交價</th>
                      <th>手續費</th>
                      <th>FIFO 成本</th>
                      <th>已實現損益</th>
                      <th>備註</th>
                      <th aria-label="操作" />
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="transaction in paginatedAllTransactions" :key="transaction.id">
                      <td>{{ transaction.transactionDate }}</td>
                      <td :class="transaction.transactionType === 'BUY' ? 'tx-buy' : 'tx-sell'">
                        {{ transaction.transactionType === 'BUY' ? '買入' : '賣出' }}
                      </td>
                      <td>{{ transaction.ticker }}</td>
                      <td>{{ transaction.securityName }}</td>
                      <td>{{ formatNumber(transaction.quantity) }}</td>
                      <td>{{ formatMoney(transaction.price, valuation?.currency ?? '') }}</td>
                      <td>{{ formatMoney(transaction.fee, valuation?.currency ?? '') }}</td>
                      <td>{{ transaction.fifoCost == null ? '—' : formatMoney(transaction.fifoCost, valuation?.currency ?? '') }}</td>
                      <td>{{ transaction.realizedPnl == null ? '—' : formatMoney(transaction.realizedPnl, valuation?.currency ?? '') }}</td>
                      <td>{{ transaction.note || '—' }}</td>
                      <td>
                        <button
                          class="transaction-delete"
                          :disabled="deletingTransactionId === transaction.id"
                          @click="handleDeleteAllTransaction(transaction)"
                        >
                          {{ deletingTransactionId === transaction.id ? '刪除中...' : '刪除' }}
                        </button>
                      </td>
                    </tr>
                  </tbody>
                </table>
                <div
                  v-if="allTransactionPageCount > 1"
                  style="margin-top: 16px; display: flex; justify-content: flex-end"
                >
                  <NPagination
                    v-model:page="allTransactionPage"
                    :page-size="allTransactionPageSize"
                    :item-count="allTransactions.length"
                  />
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Manual cash flows -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="prestige-panel">
            <div class="panel-head">
              <h2 class="panel-title">現金流帳</h2>
              <span class="caption">CASH LEDGER — 現金變動</span>
            </div>
            <div class="panel-body">
              <div style="display: grid; grid-template-columns: 1fr 160px auto; gap: 10px; margin-bottom: 18px">
                <input v-model.number="cashFlowAmount" class="prestige-input" type="number" step="0.01" placeholder="金額（正數入金、負數出金）" />
                <input v-model="cashFlowForm.effectiveDate" class="prestige-input" type="date" />
                <button class="prestige-btn prestige-btn-solid small" :disabled="savingCashFlow" @click="saveCashFlow">{{ savingCashFlow ? '儲存中...' : '新增' }}</button>
              </div>
              <div v-if="cashFlowsError" class="prestige-error">{{ cashFlowsError }}</div>
              <div v-else-if="!manualCashFlows.length" class="transaction-empty">尚無現金變動紀錄。</div>
              <div v-else style="overflow-x: auto"><table class="prestige-table transaction-table"><thead><tr><th>日期</th><th>現金變動</th><th aria-label="操作" /></tr></thead><tbody>
                <tr v-for="flow in manualCashFlows" :key="flow.id"><td>{{ flow.effectiveDate }}</td><td :class="flow.flowType === 'Withdrawal' || flow.flowType === 'Fee' || flow.flowType === 'DividendTax' ? 'pnl-down' : 'pnl-up'">{{ flow.flowType === 'Withdrawal' || flow.flowType === 'Fee' || flow.flowType === 'DividendTax' ? '−' : '+' }}{{ formatMoney(flow.amount, flow.currency) }} <span v-if="flow.isSystemDerived" class="caption">系統推導</span></td><td style="white-space: nowrap"><template v-if="!flow.isSystemDerived"><button class="transaction-delete" @click="editCashFlow(flow)">修改</button><button class="transaction-delete" @click="removeCashFlow(flow)">刪除</button></template></td></tr>
              </tbody></table></div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Dividend cash flows -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="prestige-panel">
            <div class="panel-head">
              <h2 class="panel-title">股息紀錄</h2>
              <span class="caption">DIVIDEND CASH FLOWS — 自動入帳</span>
            </div>
            <div class="panel-body">
              <div v-if="loadingDividendCashFlows" class="transaction-empty">載入中...</div>
              <div v-else-if="dividendCashFlowsError" class="prestige-error">{{ dividendCashFlowsError }}</div>
              <div v-else-if="!dividendCashFlows.length" class="transaction-empty">目前沒有符合持倉的股息紀錄。</div>
              <div v-else style="overflow-x: auto">
                <table class="prestige-table transaction-table">
                  <thead>
                    <tr>
                      <th>股票</th>
                      <th>除息日</th>
                      <th>發放日</th>
                      <th>持股數</th>
                      <th>每股股利</th>
                      <th>金額</th>
                      <th>狀態</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="flow in dividendCashFlows" :key="flow.id">
                      <td><strong class="cell-strong">{{ flow.ticker }}</strong><br><span class="caption">{{ flow.securityName }}</span></td>
                      <td>{{ flow.exDividendDate }}</td>
                      <td>{{ flow.paymentDate || '—' }}</td>
                      <td>{{ formatNumber(flow.sharesEntitled) }}</td>
                      <td>{{ formatMoney(flow.cashAmountPerShare, flow.currency) }}</td>
                      <td>{{ formatMoney(flow.amount, flow.currency) }}</td>
                      <td>{{ dividendStatusLabel(flow.status) }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Latest dividend reference data -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="prestige-panel">
            <div class="panel-head">
              <h2 class="panel-title">市場最新股息</h2>
              <span class="caption">LATEST DIVIDEND REFERENCE DATA</span>
            </div>
            <div class="panel-body">
              <div
                v-if="loadingLatestDividends"
                class="transaction-empty">
                載入中...
              </div>
              <div
                v-else-if="latestDividendsError"
                class="prestige-error">
                {{ latestDividendsError }}
              </div>
              <div
                v-else-if="!latestDividends.length"
                class="transaction-empty">
                目前沒有符合的股息資訊。
              </div>
              <div v-else style="overflow-x: auto">
                <table class="prestige-table">
                  <thead>
                    <tr>
                      <th>股票</th>
                      <th>除息日</th>
                      <th>發放日</th>
                      <th>每股股利</th>
                      <th>預估金額</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="dividend in latestDividends" :key="dividend.securityId">
                      <td>
                        <div class="cell-strong">{{ dividend.ticker }}</div>
                        <div class="cell-sub">{{ dividend.securityName }}</div>
                      </td>
                      <td>{{ dividend.exDividendDate }}</td>
                      <td>{{ dividend.paymentDate || '—' }}</td>
                      <td>{{ formatMoney(dividend.cashAmountPerShare, dividend.currency) }}</td>
                      <td>{{ getEstimatedDividendAmount(dividend) }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </ScrollReveal>
      </template>
    </div>

    <footer class="detail-footer">
      <span class="prestige-mono">EQUITYLENS 2026</span>
      <span class="prestige-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">PORTFOLIO</span>
      <span>數據僅供參考</span>
    </footer>
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.back-btn {
  margin-bottom: 24px;
}

.skeleton-stack {
  display: grid;
  gap: 16px;
}

/* ---- Typography helpers ---- */
.caption {
  font-size: 11px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--muted);
}

.detail-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 32px;
  flex-wrap: wrap;
}

.detail-title {
  margin: 0;
  font-family: var(--serif);
  font-size: 30px;
  font-weight: 600;
  letter-spacing: 0.01em;
}

/* ---- Status colors ---- */
.prestige-warning {
  padding: 14px 18px;
  border: 1px solid var(--gold-border);
  border-radius: 6px;
  background: rgba(201, 168, 106, 0.07);
  color: var(--gold-strong);
  font-size: 13px;
  line-height: 1.6;
}

.pnl-up {
  color: var(--up);
}

.pnl-down {
  color: var(--down);
}

.tx-buy {
  color: var(--up);
}

.tx-sell {
  color: var(--gold);
}

/* ---- Buttons ---- */
.prestige-btn.small {
  padding: 8px 16px;
  font-size: 12px;
  letter-spacing: 0.08em;
}

/* ---- Time range buttons ---- */
.time-range {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.time-btn {
  padding: 5px 14px;
  font-size: 12px;
  font-family: 'SFMono-Regular', Consolas, 'Courier New', monospace;
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  background: transparent;
  color: var(--muted);
  cursor: pointer;
  transition: all 0.2s ease;
}

.time-btn:hover {
  border-color: var(--gold);
  color: var(--gold);
}

.time-btn.active {
  background: var(--gold);
  border-color: var(--gold);
  color: #0b1220;
}

.time-btn:disabled {
  opacity: 0.5;
  cursor: wait;
}

.period-loading {
  display: inline-block;
  margin-top: 8px;
  color: var(--gold);
  font-size: 12px;
}

/* ---- Panel building blocks ---- */
.panel-head {
  padding: 20px 24px;
  border-bottom: 1px solid var(--gold-border-soft);
}

.panel-body {
  padding: 20px 24px;
}

.panel-title {
  margin: 0;
  font-family: var(--serif);
  font-size: 18px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.inner-panel {
  margin-bottom: 20px;
}

.inner-panel-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 14px;
  flex-wrap: wrap;
}

.inner-panel-title {
  margin: 0 0 4px;
  font-family: var(--serif);
  font-size: 16px;
  font-weight: 600;
}

/* ---- Tables ---- */
.transaction-table th,
.transaction-table td {
  white-space: nowrap;
}

.cell-strong {
  font-weight: 600;
  color: var(--ivory);
}

.cell-sub {
  font-size: 12px;
  color: var(--muted);
}

.transaction-empty {
  padding: 20px 0;
  font-size: 14px;
  text-align: center;
  color: var(--muted);
}

.transaction-delete {
  border: 1px solid rgba(176, 92, 92, 0.4);
  border-radius: 4px;
  background: transparent;
  color: var(--down);
  padding: 5px 10px;
  cursor: pointer;
  font: inherit;
  font-size: 12px;
  transition: all 0.2s ease;
}

.transaction-delete:hover {
  border-color: var(--down);
  background: rgba(176, 92, 92, 0.1);
}

.transaction-delete:disabled {
  cursor: wait;
  opacity: 0.6;
}

/* ---- Add stock form ---- */
.field-label {
  display: block;
  margin-bottom: 6px;
  color: var(--muted);
  font-size: 11px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}

.form-note {
  color: var(--muted);
  font-size: 12px;
  line-height: 1.6;
}

.selected-security {
  margin-bottom: 16px;
  color: var(--up);
  font-size: 13px;
}

.security-results {
  border: 1px solid var(--gold-border-soft);
  border-radius: 6px;
  margin-bottom: 16px;
  max-height: 240px;
  overflow-y: auto;
}

.security-result-group-label {
  position: sticky;
  top: 0;
  z-index: 1;
  padding: 7px 10px;
  border-bottom: 1px solid var(--gold-border-soft);
  background: #101a2e;
  color: var(--gold);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.08em;
}

.security-result-btn {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border: none;
  border-bottom: 1px solid var(--gold-border-soft);
  background: transparent;
  text-align: left;
  cursor: pointer;
  font-family: inherit;
  color: var(--ivory);
  font-size: 13px;
  transition: background 0.2s ease;
}

.security-result-btn:hover {
  background: rgba(201, 168, 106, 0.07);
}

.sr-ticker {
  font-weight: 600;
  color: var(--gold);
}

.sr-dim {
  color: var(--muted);
}

.sr-source {
  margin-left: auto;
  color: var(--muted);
  font-size: 12px;
}

.prestige-input[type='date'] {
  color-scheme: dark;
}

/* ---- Portfolio Value Trend Section ---- */
.trend-section {
  display: flex;
  flex-direction: column;
}

.trend-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  padding: 24px;
  border-bottom: 1px solid var(--gold-border-soft);
  flex-wrap: wrap;
}

.trend-title-group {
  display: flex;
  flex-direction: column;
  gap: 20px;
  flex: 1 1 auto;
  min-width: 260px;
}

.trend-title {
  flex: 1 1 auto;
  min-width: 260px;
}

.trend-main-title {
  margin: 0;
  font-family: var(--serif);
  font-size: clamp(24px, 4vw, 32px);
  font-weight: 600;
  letter-spacing: 0.01em;
}

.trend-metrics {
  display: flex;
  flex-wrap: wrap;
  gap: 20px 40px;
}

.trend-metric-item {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  min-width: 130px;
}

.trend-metric-value {
  font-size: 20px;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.trend-metric-sub {
  font-size: 11px;
  color: var(--muted);
}

.trend-pnl-range {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 12px;
  flex-shrink: 0;
}

.trend-pnl {
  text-align: right;
}

.trend-pnl-value {
  font-size: 26px;
  font-weight: 600;
  line-height: 1;
  letter-spacing: -0.02em;
}

.trend-chart-wrap {
  padding: 24px;
  min-height: 420px;
}

.trend-perf-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0;
  border-top: 1px solid var(--gold-border-soft);
}

@media (min-width: 768px) {
  .trend-perf-grid {
    grid-template-columns: 1fr 1fr;
  }
}

@media (min-width: 1200px) {
  .trend-perf-grid {
    grid-template-columns: repeat(4, 1fr);
  }
}

.perf-card {
  display: flex;
  flex-direction: column;
  min-height: 260px;
  padding: 20px;
  border-right: 1px solid var(--gold-border-soft);
  border-bottom: 1px solid var(--gold-border-soft);
  background: rgba(11, 18, 32, 0.35);
}

.perf-card:last-child {
  border-right: none;
}

.perf-card-header {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 16px;
}

.perf-card-title {
  font-size: 16px;
  font-weight: 600;
}

.perf-chart-wrap {
  flex: 1 1 auto;
  min-height: 160px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.perf-chart-wrap svg {
  width: 100%;
  height: 100%;
}

.perf-card-metrics {
  justify-content: flex-start;
}

.perf-metrics-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.perf-metric-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px solid var(--gold-border-soft);
}

.perf-metric-row:last-child {
  border-bottom: none;
}

.perf-metric-value {
  font-size: 20px;
  font-weight: 600;
}

.insufficient-history {
  min-height: 200px;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  color: var(--muted);
  font-size: 14px;
  line-height: 1.6;
  text-align: center;
}

.insufficient-history-sm {
  color: var(--muted);
  font-size: 13px;
  text-align: center;
  padding: 20px;
}

@media (max-width: 1024px) {
  .trend-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 24px;
  }

  .trend-title-group {
    width: 100%;
  }

  .trend-pnl-range {
    width: 100%;
    flex-direction: row;
    align-items: center;
    justify-content: space-between;
  }

  .trend-pnl {
    text-align: left;
  }
}

@media (max-width: 768px) {
  .trend-metrics {
    gap: 12px 24px;
  }

  .trend-pnl-range {
    flex-direction: column;
    align-items: flex-start;
  }
}

/* ---- Allocation sections ---- */
.allocation-section {
  display: grid;
  grid-template-columns: minmax(320px, 2fr) 3fr;
}

.allocation-chart {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 32px 20px;
  border-right: 1px solid var(--gold-border-soft);
  min-height: 420px;
}

.allocation-chart :deep(svg) {
  max-width: 100%;
  height: auto;
}

.allocation-holdings {
  padding: 24px;
  min-width: 0;
}

.allocation-holdings-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
}

@media (max-width: 1024px) {
  .allocation-section {
    grid-template-columns: 1fr;
  }

  .allocation-chart {
    border-right: none;
    border-bottom: 1px solid var(--gold-border-soft);
    min-height: auto;
    padding: 24px;
  }
}

/* ---- DataTable (kimi shared component) prestige overrides ---- */
.prestige-data-table :deep(.kimi-table) {
  font-size: 13px;
}

.prestige-data-table :deep(.kimi-table-dark th) {
  background: rgba(201, 168, 106, 0.05);
  color: var(--gold);
  border-bottom: 1px solid var(--gold-border);
  font-weight: 600;
}

.prestige-data-table :deep(.kimi-table-dark td) {
  border-bottom: 1px solid var(--gold-border-soft);
  color: var(--ivory);
}

.prestige-data-table :deep(.kimi-table-dark tbody tr:nth-child(even)) {
  background: rgba(201, 168, 106, 0.03);
}

.prestige-data-table :deep(.kimi-table-dark tbody tr:hover) {
  background: rgba(201, 168, 106, 0.07);
}

.prestige-data-table :deep(.kimi-table-dark tbody tr.highlight) {
  background: rgba(201, 168, 106, 0.12) !important;
}

/* ---- Footer ---- */
.detail-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  width: min(1200px, calc(100% - 48px));
  margin: 48px auto 0;
  padding: 20px 0 28px;
  border-top: 1px solid var(--gold-border-soft);
  color: var(--muted);
  font-size: 12px;
}

@media (max-width: 560px) {
  .detail-footer {
    flex-direction: column;
    gap: 6px;
  }
}
</style>
