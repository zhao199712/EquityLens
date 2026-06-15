<script setup lang="ts">
import type { AxiosError } from 'axios'
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import LineChart from '../../components/kimi/LineChart.vue'
import DonutChart from '../../components/kimi/DonutChart.vue'
import DataTable from '../../components/kimi/DataTable.vue'
import ScatterPlot from '../../components/kimi/ScatterPlot.vue'
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
import { createTransaction } from '../../services/portfolioTransactions.ts'
import { syncSecurityPrices } from '../../services/marketPrices.ts'
import {
  resolveSecurity,
  searchSecurities,
  type SecuritySearchResult,
} from '../../services/securities.ts'
import {
  portfolioValueData,
  allocationData,
  performanceAttribution,
  riskData,
  scenarioData,
} from '../../data/portfolioKimiData'

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
const showAddStock = ref(false)
const securityQuery = ref('')
const securityResults = ref<SecuritySearchResult[]>([])
const selectedSecurity = ref<SecuritySearchResult | null>(null)
const searchingSecurities = ref(false)
const addingStock = ref(false)
const addStockError = ref('')
const addStockStatus = ref('')
const addStockWarning = ref('')
const addStockForm = ref({
  transactionType: 'Buy' as 'Buy' | 'Sell',
  quantity: 0,
  price: 0,
  fee: 0,
  transactionDate: new Date().toISOString().split('T')[0],
  note: '',
})

const timeRange = ref('1Y')
const hoveredSegment = ref<number | null>(null)
const timeRanges = ['1Y', '6M', '3M', '1M', 'YTD']

const today = new Date()
const oneYearAgo = new Date(today.getFullYear() - 1, today.getMonth(), today.getDate())
const fromDate = computed(() => oneYearAgo.toISOString().split('T')[0])
const toDate = computed(() => today.toISOString().split('T')[0])

const valueTrendData = computed(() => {
  const points = valuationHistory.value?.points.filter((p) => p.totalMarketValue > 0) ?? []
  if (points.length === 0) {
    return {
      labels: portfolioValueData.labels,
      values: portfolioValueData.values,
      yAxisLabels: portfolioValueData.yAxisLabels,
      summary: portfolioValueData.summary,
      isMock: true,
    }
  }

  const values = points.map((p) => p.totalMarketValue)
  const max = Math.max(...values)
  const min = Math.min(...values)
  const step = (max - min) / 4 || 1
  return {
    labels: points.map((p) => p.date.slice(5)),
    values,
    yAxisLabels: Array.from({ length: 5 }, (_, i) => formatMoney(max - step * i, valuationHistory.value?.currency ?? '')),
    summary: {
      start: formatMoney(values[0], valuationHistory.value?.currency ?? ''),
      high: formatMoney(max, valuationHistory.value?.currency ?? ''),
      low: formatMoney(min, valuationHistory.value?.currency ?? ''),
    },
    isMock: false,
  }
})

const kpiData = computed(() => {
  const v = valuation.value
  const r = risk.value
  return [
    {
      label: 'TOTAL VALUE',
      value: v ? formatMoney(v.totalMarketValue, v.currency) : '—',
      sub: 'Total Market Value',
    },
    {
      label: 'UNREALIZED PNL',
      value: v ? formatPercent(v.totalUnrealizedPnlPercent) : '—',
      sub: v ? formatMoney(v.totalUnrealizedPnl, v.currency) : '',
    },
    {
      label: 'VOLATILITY',
      value: r ? `${(r.historicalAnnualizedVolatility * 100).toFixed(2)}%` : '—',
      sub: r ? r.volatilityMethod : '',
    },
    {
      label: 'MAX DRAWDOWN',
      value: r ? `${(r.maxDrawdown * 100).toFixed(2)}%` : '—',
      sub: 'Historical',
    },
  ]
})

const allocationSegments = computed(() => {
  return (
    valuation.value?.holdings.map((h, i) => ({
      label: h.ticker,
      value: h.weight ?? 0,
      color: allocationData.segments[i % allocationData.segments.length]?.color ?? '#333333',
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

const hasMissingPrices = computed(() => {
  return valuation.value?.holdings.some((h) => h.valuationStatus !== 'Priced') ?? false
})

const riskMetrics = computed(() => {
  const r = risk.value
  return [
    { label: '波動率', value: r ? `${(r.historicalAnnualizedVolatility * 100).toFixed(2)}%` : performanceAttribution.risk.volatility },
    { label: '最大回撤', value: r ? `${(r.maxDrawdown * 100).toFixed(2)}%` : performanceAttribution.risk.maxDrawdown },
    { label: 'Sharpe Ratio', value: r ? r.sharpeRatio.toFixed(2) : performanceAttribution.risk.infoRatio },
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
  const points = valuationHistory.value?.points.filter((p) => p.totalMarketValue > 0)
  if (!points || points.length === 0) return null
  const values = points.map((p) => p.totalMarketValue)
  const cumReturns = computeCumulativeReturn(values)
  const labels = points.map((p) => p.date.slice(5))
  const max = Math.max(...cumReturns)
  const min = Math.min(...cumReturns)
  const range = max - min || 1
  return { labels, values: cumReturns, max, min, range }
})

const ewmaPoints = computed(() => {
  const points = valuationHistory.value?.points.filter((p) => p.totalMarketValue > 0)
  if (!points || points.length < 22) return null
  const values = points.map((p) => p.totalMarketValue)
  const vols = computeEwmaVolatility(values)
  const labels = points.slice(1).map((p) => p.date.slice(5))
  const max = Math.max(...vols)
  const min = Math.min(...vols)
  const range = max - min || 1
  return { labels, values: vols, max, min, range }
})

const drawdownPoints = computed(() => {
  const points = valuationHistory.value?.points.filter((p) => p.totalMarketValue > 0)
  if (!points || points.length === 0) return null
  const values = points.map((p) => p.totalMarketValue)
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

function priceStatusLabel(status: string) {
  if (status === 'Priced') return '市場報價'
  if (status === 'MissingPrice') return '缺價格'
  return status
}

function getApiErrorMessage(e: unknown, fallback: string) {
  const error = e as AxiosError<{ code?: string; message?: string }>
  const code = error.response?.data?.code
  const message = error.response?.data?.message
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
  valuationHistoryError.value = ''
  riskError.value = ''
  try {
    portfolio.value = await getPortfolio(portfolioId.value)

    try {
      valuation.value = await getPortfolioValuation(portfolioId.value)
    } catch (e) {
      valuationError.value = '估值資料暫不可用。若投資組合尚無持倉或價格，這是正常狀態。'
    }

    try {
      valuationHistory.value = await getPortfolioValuationHistory(portfolioId.value, {
        from: fromDate.value,
        to: toDate.value,
      })
    } catch (e) {
      valuationHistoryError.value = '歷史估值資料暫不可用，價值走勢暫以示範資料顯示。'
    }

    try {
      risk.value = await getPortfolioRisk(portfolioId.value, {
        from: fromDate.value,
        to: toDate.value,
        horizonDays: 30,
        confidenceLevel: 0.95,
        simulations: 10000,
      })
    } catch (e) {
      riskError.value = '持倉已建立但市場價格或歷史價格不足時，暫無法計算風險分析。'
    }
  } catch (e) {
    error.value = '無法載入投資組合資料，請稍後再試。'
  } finally {
    if (showPageLoading) {
      loading.value = false
    }
  }
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

function selectSecurity(security: SecuritySearchResult) {
  selectedSecurity.value = security
  securityQuery.value = `${security.ticker} ${security.exchange}`
  securityResults.value = []
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

    showAddStock.value = false
    selectedSecurity.value = null
    securityQuery.value = ''
    securityResults.value = []
    addStockForm.value = {
      transactionType: 'Buy',
      quantity: 0,
      price: 0,
      fee: 0,
      transactionDate: new Date().toISOString().split('T')[0],
      note: '',
    }
    await loadPortfolioData(false)
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
  <div class="kimi-page-dark" style="padding-top: 40px">
    <!-- Back + Header -->
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn kimi-btn-dark" style="margin-bottom: 24px" @click="router.push({ name: 'portfolios' })">
        ← BACK TO PORTFOLIOS
      </button>

      <div v-if="loading" style="color: #666666; padding: 40px 0">載入中...</div>
      <div v-else-if="error" style="color: #f87171; padding: 40px 0">{{ error }}</div>

      <template v-if="!loading && portfolio">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
          <div>
            <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">{{ portfolio.name }}</h1>
            <span class="kimi-caption" style="margin-top: 4px; display: block">{{ portfolio.description || portfolio.baseCurrency }} PORTFOLIO</span>
          </div>
          <div style="display: flex; gap: 8px">
            <span class="kimi-tag" style="border-color: #FF6B00; color: #FF6B00">{{ portfolio.baseCurrency }}</span>
            <span class="kimi-caption" style="align-self: center; color: #666666">Last updated: {{ new Date(portfolio.updatedAtUtc).toLocaleDateString('zh-TW') }}</span>
          </div>
        </div>

        <div v-if="valuationError" class="kimi-panel-dark" style="margin-bottom: 20px; color: #fbbf24">
          {{ valuationError }}
        </div>

        <div v-if="riskError" class="kimi-panel-dark" style="margin-bottom: 20px; color: #fbbf24">
          {{ riskError }}
        </div>

        <!-- KPI Cards -->
        <ScrollReveal>
          <div class="kimi-section-dark">
            <div class="kimi-kpi-grid">
              <div v-for="(kpi, i) in kpiData" :key="i" class="kimi-kpi-cell kimi-kpi-cell-dark"
              >
                <span class="kimi-caption" style="margin-bottom: 8px; display: block">{{ kpi.label }}</span>
                <span class="kimi-data" style="color: #FFFFFF">{{ kpi.value }}</span>
                <span style="font-size: 12px; color: #666666; margin-top: 4px; display: block">{{ kpi.sub }}</span>
                <div class="accent-bar" style="background-color: #FF6B00" />
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Portfolio Value Trend -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="kimi-section-dark">
            <div style="display: flex; align-items: center; justify-content: space-between; padding: 20px; border-bottom: 1px solid #333333">
              <div>
                <div v-if="valueTrendData.isMock" class="kimi-mock-label">
                  {{ valuationHistoryError || '示範資料：尚無歷史估值資料' }}
                </div>
                <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">投資組合價值走勢</h2>
                <span class="kimi-caption" style="margin-top: 4px; display: block">PORTFOLIO VALUE TREND</span>
                <span class="kimi-caption" style="display: block">{{ fromDate }} — {{ toDate }}</span>
              </div>
              <div class="kimi-time-range">
                <button
                  v-for="r in timeRanges"
                  :key="r"
                  :class="['kimi-time-btn', 'kimi-time-btn-dark', timeRange === r && 'active']"
                  @click="timeRange = r"
                >
                  {{ r }}
                </button>
              </div>
            </div>
            <div style="padding: 20px">
              <LineChart
                :data="valueTrendData.values"
                :labels="valueTrendData.labels"
                :y-axis-labels="valueTrendData.yAxisLabels"
                :height="400"
                line-color="#FFFFFF"
                grid-color="#333333"
                text-color="#666666"
                :dark="true"
                :show-area="true"
              />
            </div>
            <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; padding: 20px; border-top: 1px solid #333333">
              <span style="font-size: 13px; color: #666666">起始資產 {{ valueTrendData.summary.start }}</span>
              <span style="font-size: 13px; color: #666666">最高資產 {{ valueTrendData.summary.high }}</span>
              <span style="font-size: 13px; color: #666666">最低資產 {{ valueTrendData.summary.low }}</span>
            </div>
          </div>
        </ScrollReveal>

        <!-- Asset Allocation -->
        <ScrollReveal :delay="0.15" style="margin-top: 60px">
          <div class="kimi-section-dark" style="display: grid; grid-template-columns: 2fr 3fr">
            <!-- Donut -->
            <div style="display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 40px 20px; border-right: 1px solid #333333">
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
            <div style="padding: 20px">
              <div style="display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 16px">
                <div>
                  <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">持倉明細</h2>
                  <span class="kimi-caption">HOLDINGS — 後端資料</span>
                </div>
                <button class="kimi-btn kimi-btn-solid-dark" @click="showAddStock = !showAddStock">
                  {{ showAddStock ? 'CANCEL' : '+ ADD STOCK' }}
                </button>
              </div>

              <div v-if="showAddStock" class="kimi-panel-dark" style="margin-bottom: 20px">
                <div style="margin-bottom: 14px">
                  <h3 style="margin: 0 0 4px; color: #FFFFFF; font-size: 16px; font-weight: 600">新增股票交易</h3>
                  <span class="kimi-caption">先搜尋股票，選取結果後填入交易資料</span>
                </div>

                <label class="field-label">股票搜尋</label>
                <div style="display: grid; grid-template-columns: 2fr auto; gap: 12px; margin-bottom: 12px">
                  <input
                    v-model="securityQuery"
                    class="kimi-input-dark"
                    placeholder="搜尋股票代號或名稱，例如 TSM / AAPL / 2330"
                    @keyup.enter="handleSearchSecurities"
                  />
                  <button class="kimi-btn kimi-btn-dark" :disabled="searchingSecurities" @click="handleSearchSecurities">
                    {{ searchingSecurities ? 'SEARCHING...' : 'SEARCH' }}
                  </button>
                </div>

                <div v-if="securityResults.length" style="border: 1px solid #333333; margin-bottom: 16px; max-height: 180px; overflow-y: auto">
                  <button
                    v-for="security in securityResults"
                    :key="`${security.ticker}-${security.exchange}-${security.source}`"
                    class="security-result-btn"
                    @click="selectSecurity(security)"
                  >
                    <span style="font-weight: 600; color: #FFFFFF">{{ security.ticker }}</span>
                    <span style="color: #666666">{{ security.exchange }}</span>
                    <span style="color: #FFFFFF">{{ security.name }}</span>
                    <span style="margin-left: auto; color: #666666">{{ security.source }}</span>
                  </button>
                </div>

                <div v-if="selectedSecurity" style="margin-bottom: 16px; color: #34d399; font-size: 13px">
                  已選擇：{{ selectedSecurity.ticker }} / {{ selectedSecurity.exchange }} / {{ selectedSecurity.name }}
                </div>

                <div style="margin-bottom: 16px; color: #999999; font-size: 12px; line-height: 1.6">
                  成交價只用於計算交易成本與平均成本；目前市值、損益與風險分析會使用市場價格資料。
                </div>

                <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 12px">
                  <label>
                    <span class="field-label">交易類型</span>
                    <select v-model="addStockForm.transactionType" class="kimi-input-dark">
                      <option value="Buy">Buy 買入</option>
                      <option value="Sell">Sell 賣出</option>
                    </select>
                  </label>
                  <label>
                    <span class="field-label">數量</span>
                    <input v-model.number="addStockForm.quantity" class="kimi-input-dark" type="number" min="0" step="0.0001" placeholder="例如 10" />
                  </label>
                  <label>
                    <span class="field-label">成交價（交易成本）</span>
                    <input v-model.number="addStockForm.price" class="kimi-input-dark" type="number" min="0" step="0.0001" placeholder="例如 150.5" />
                  </label>
                  <label>
                    <span class="field-label">手續費</span>
                    <input v-model.number="addStockForm.fee" class="kimi-input-dark" type="number" min="0" step="0.01" placeholder="可填 0" />
                  </label>
                </div>

                <div style="display: grid; grid-template-columns: 1fr 2fr auto; gap: 12px; align-items: center">
                  <label>
                    <span class="field-label">交易日期</span>
                    <input v-model="addStockForm.transactionDate" class="kimi-input-dark" type="date" />
                  </label>
                  <label>
                    <span class="field-label">備註</span>
                    <input v-model="addStockForm.note" class="kimi-input-dark" placeholder="選填，例如首次買入" />
                  </label>
                  <button class="kimi-btn kimi-btn-solid-dark" :disabled="addingStock" @click="handleAddStock">
                    {{ addingStock ? 'ADDING...' : 'ADD' }}
                  </button>
                </div>

                <div v-if="addStockError" style="margin-top: 12px; color: #f87171; font-size: 13px">
                  {{ addStockError }}
                </div>
                <div v-if="addStockStatus" style="margin-top: 12px; color: #999999; font-size: 13px">
                  {{ addStockStatus }}
                </div>
              </div>

              <div v-if="addStockWarning" style="margin-bottom: 12px; color: #fbbf24; font-size: 13px; line-height: 1.6">
                {{ addStockWarning }}
              </div>

              <DataTable
                v-if="allocationRows.length"
                :headers="['代碼', '名稱', '交易所', '持有股數', '現價', '價格狀態', '市值', '占比', '損益']"
                :rows="allocationRows"
                :highlight-row="hoveredSegment"
                :dark="true"
                @row-hover="(i) => hoveredSegment = i"
              />
              <div v-if="hasMissingPrices" style="margin-top: 12px; color: #fbbf24; font-size: 13px; line-height: 1.6">
                部分持倉尚無市場價格，因此市值、損益、資產走勢與風險分析可能暫不可用或不完整。
              </div>
              <div v-if="!allocationRows.length" style="padding: 32px 0; color: #666666; font-size: 14px">
                此投資組合尚無持倉。新增持倉後會顯示估值與權重明細。
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Performance & Risk Analysis -->
        <div style="margin-top: 60px">
          <div v-if="!valuationHistory?.points.some((p) => p.totalMarketValue > 0)" class="kimi-mock-label" style="margin-bottom: 8px">示範資料</div>
          <div style="margin-bottom: 24px">
            <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">績效與風險分析</h2>
            <span class="kimi-caption">PERFORMANCE & RISK</span>
          </div>
          <div class="kimi-grid-4">
            <!-- Cumulative Return -->
            <ScrollReveal :delay="0">
              <div class="kimi-panel-dark">
                <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600; color: #FFFFFF">累積報酬率</h3>
                <svg v-if="cumReturnPoints" width="100%" height="200" viewBox="0 0 400 200">
                  <line v-for="i in 5" :key="'g-' + i" x1="50" :y1="20 + (i - 1) * 36" x2="380" :y2="20 + (i - 1) * 36" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                  <line v-if="0 >= cumReturnPoints!.min && 0 <= cumReturnPoints!.max" x1="50" :y1="20 + ((cumReturnPoints!.max - 0) / cumReturnPoints!.range) * 144" x2="380" :y2="20 + ((cumReturnPoints!.max - 0) / cumReturnPoints!.range) * 144" stroke="#666666" stroke-width="1" />
                  <polyline :points="cumReturnPoints!.values.map((v, i) => `${50 + (i / (cumReturnPoints!.values.length - 1)) * 330},${20 + ((cumReturnPoints!.max - v) / cumReturnPoints!.range) * 144}`).join(' ')" fill="none" stroke="#FFFFFF" stroke-width="1.5" />
                  <text v-for="i in 6" :key="'y-' + i" x="45" :y="20 + (i - 1) * 36 + 4" text-anchor="end" fill="#666666" font-size="9">{{ (cumReturnPoints!.max - ((i - 1) / 5) * cumReturnPoints!.range).toFixed(1) }}%</text>
                </svg>
                <div v-else style="padding: 40px 0; color: #666666; text-align: center">暫無資料</div>
              </div>
            </ScrollReveal>

            <!-- EWMA Rolling Volatility -->
            <ScrollReveal :delay="0.1">
              <div class="kimi-panel-dark">
                <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600; color: #FFFFFF">滾動波動率 EWMA</h3>
                <svg v-if="ewmaPoints" width="100%" height="200" viewBox="0 0 400 200">
                  <line v-for="i in 5" :key="'g-' + i" x1="50" :y1="20 + (i - 1) * 36" x2="380" :y2="20 + (i - 1) * 36" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                  <polyline :points="ewmaPoints!.values.map((v, i) => `${50 + (i / (ewmaPoints!.values.length - 1)) * 330},${20 + ((ewmaPoints!.max - v) / ewmaPoints!.range) * 144}`).join(' ')" fill="none" stroke="#8B1A2B" stroke-width="1.5" />
                  <text v-for="i in 6" :key="'y-' + i" x="45" :y="20 + (i - 1) * 36 + 4" text-anchor="end" fill="#666666" font-size="9">{{ (ewmaPoints!.max - ((i - 1) / 5) * ewmaPoints!.range).toFixed(1) }}%</text>
                </svg>
                <div v-else style="padding: 40px 0; color: #666666; text-align: center">暫無資料</div>
              </div>
            </ScrollReveal>

            <!-- Historical Drawdown -->
            <ScrollReveal :delay="0.2">
              <div class="kimi-panel-dark">
                <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600; color: #FFFFFF">歷史回撤</h3>
                <svg v-if="drawdownPoints" width="100%" height="200" viewBox="0 0 400 200">
                  <line v-for="i in 5" :key="'g-' + i" x1="50" :y1="20 + (i - 1) * 36" x2="380" :y2="20 + (i - 1) * 36" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                  <polygon :points="`50,164 ${drawdownPoints!.values.map((v, i) => `${50 + (i / (drawdownPoints!.values.length - 1)) * 330},${20 + ((0 - v) / drawdownPoints!.range) * 144}`).join(' ')} 380,164`" fill="rgba(139,26,43,0.15)" />
                  <polyline :points="drawdownPoints!.values.map((v, i) => `${50 + (i / (drawdownPoints!.values.length - 1)) * 330},${20 + ((0 - v) / drawdownPoints!.range) * 144}`).join(' ')" fill="none" stroke="#8B1A2B" stroke-width="1.5" />
                  <line v-if="drawdownPoints!.maxIndex >= 0" :x1="50 + (drawdownPoints!.maxIndex / (drawdownPoints!.values.length - 1)) * 330" :y1="20 + ((0 - drawdownPoints!.maxDrawdown) / drawdownPoints!.range) * 144" :x2="50 + (drawdownPoints!.maxIndex / (drawdownPoints!.values.length - 1)) * 330" :y2="164" stroke="#FFFFFF" stroke-width="1" stroke-dasharray="4 4" />
                  <text v-if="drawdownPoints!.maxIndex >= 0" :x="50 + (drawdownPoints!.maxIndex / (drawdownPoints!.values.length - 1)) * 330 + 4" :y="20 + ((0 - drawdownPoints!.maxDrawdown) / drawdownPoints!.range) * 144 - 4" fill="#FFFFFF" font-size="10" font-weight="600">{{ drawdownPoints!.maxDrawdown.toFixed(2) }}%</text>
                  <text v-for="i in 5" :key="'y-' + i" x="45" :y="20 + (i - 1) * 36 + 4" text-anchor="end" fill="#666666" font-size="9">{{ (-(i - 1) / 4 * drawdownPoints!.range).toFixed(1) }}%</text>
                </svg>
                <div v-else style="padding: 40px 0; color: #666666; text-align: center">暫無資料</div>
              </div>
            </ScrollReveal>

            <!-- Risk Metrics -->
            <ScrollReveal :delay="0.3">
              <div class="kimi-panel-dark">
                <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600; color: #FFFFFF">風險指標</h3>
                <div>
                  <div
                    v-for="(r, i) in riskMetrics"
                    :key="i"
                    style="display: flex; justify-content: space-between; align-items: center; padding: 8px 0"
                  >
                    <span class="kimi-caption">{{ r.label }}</span>
                    <span class="kimi-data-sm" style="font-size: 20px; color: #FFFFFF">{{ r.value }}</span>
                  </div>
                </div>
              </div>
            </ScrollReveal>
          </div>
        </div>

        <!-- Risk Matrix -->
        <div style="margin-top: 60px; border: 1px solid #333333">
          <ScrollReveal style="padding: 20px">
            <div class="kimi-mock-label" style="margin-bottom: 8px">示範資料</div>
            <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600; color: #FFFFFF">風險矩陣</h2>
            <ScatterPlot
              :data="riskData.scatter"
              x-axis-label="波動率（標準差）"
              y-axis-label="預期報酬率"
              :x-range="[0, 30]"
              :y-range="[-5, 25]"
              :frontier-curve="[[5, 2], [8, 5], [10, 7], [12, 9], [15, 11], [18, 13], [22, 15], [25, 16]]"
              :dark="true"
            />
          </ScrollReveal>
        </div>

        <!-- Scenario Simulation -->
        <ScrollReveal :delay="0.1" style="margin-top: 60px">
          <div class="kimi-section-dark">
            <div style="padding: 20px; border-bottom: 1px solid #333333">
              <div class="kimi-mock-label" style="margin-bottom: 8px">示範資料</div>
              <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">情境模擬</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">SCENARIO SIMULATION</span>
            </div>
            <div style="overflow-x: auto">
              <table class="kimi-table kimi-table-dark">
                <thead>
                  <tr>
                    <th>情境名稱</th>
                    <th>預估影響</th>
                    <th>發生機率</th>
                    <th>說明</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="s in scenarioData" :key="s.name">
                    <td style="font-weight: 600; color: #FFFFFF">{{ s.name }}</td>
                    <td style="font-weight: 600; color: #f87171">{{ s.impact }}</td>
                    <td>
                      <span class="kimi-tag" style="border-color: #333333; color: #999999">{{ s.probability }}</span>
                    </td>
                    <td style="color: #666666">{{ s.description }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </ScrollReveal>

        <div style="height: 80px" />
      </template>
    </div>

    <footer class="kimi-footer kimi-footer-dark">
      <span style="color: #666666">EQUITYLENS 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px; color: #666666">PORTFOLIO</span>
      <span style="color: #666666">數據僅供參考</span>
    </footer>
  </div>
</template>

<style scoped>
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

.kimi-input-dark {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid #333333;
  background: transparent;
  color: #ffffff;
  outline: none;
  font-size: 14px;
  font-family: var(--kimi-font-body);
}

.kimi-input-dark:focus {
  border-color: #ffffff;
}

.kimi-input-dark::placeholder {
  color: #666666;
}

.field-label {
  display: block;
  margin-bottom: 6px;
  color: #999999;
  font-size: 11px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}

select.kimi-input-dark {
  cursor: pointer;
}

.security-result-btn {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border: none;
  border-bottom: 1px solid #333333;
  background: transparent;
  text-align: left;
  cursor: pointer;
  font-family: var(--kimi-font-body);
}

.security-result-btn:hover {
  background: #111111;
}

@media (max-width: 1024px) {
  .kimi-grid-2 > * {
    border-right: none !important;
  }
  .kimi-grid-2 {
    display: block;
  }
  .kimi-section-dark > div[style*="grid-template-columns: 2fr 3fr"] {
    display: block;
  }
}
</style>
