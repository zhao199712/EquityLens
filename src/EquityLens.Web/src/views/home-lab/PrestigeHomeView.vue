<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import TechChart from '../../components/tech/TechChart.vue'
import {
  aiFeed,
  allocation,
  equityCurves,
  equityRanges,
  heroKpis,
  riskRadar,
  tickerItems,
  varMetrics,
  type AiFeedItem,
  type AllocationSlice,
  type EquityPoint,
  type EquityRange,
  type HeroKpi,
  type TickerItem,
  type VarMetric,
} from '../../data/homeTechData'
import {
  getPortfolios,
  getPortfolioRisk,
  getPortfolioValuation,
  getPortfolioValuationHistory,
  type PortfolioRiskResponse,
  type PortfolioValuationResponse,
} from '../../services/risk'
import { getMarketTicker } from '../../services/marketPrices'
import { listResearchRuns } from '../../services/research'
import { listAgentRuns } from '../../services/agentRuns'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const authStore = useAuthStore()

// Prestige Banking 色票
const GOLD = '#c9a86a'
const IVORY = '#f5efe0'
const MUTED = '#9a917c'
const DOWN = '#b05c5c'
const ALLOC_COLORS = ['#c9a86a', '#3d5470', '#a8905e', '#2a3d55', '#8a7348', '#1d2c42']

const formatKpiValue = (value: number, decimals: number) =>
  value.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })

// ---- 即時資料狀態(任一區塊 API 失敗時,該區塊保留 mock)----
const dataMode = ref<'live' | 'demo'>('demo')
const primaryPortfolioId = ref<string | null>(null)

// ---- KPI 四卡 ----
const kpis = ref<HeroKpi[]>(heroKpis.map((kpi) => ({ ...kpi })))

const setKpi = (index: number, kpi: HeroKpi) => {
  kpis.value = kpis.value.map((current, i) => (i === index ? kpi : current))
}

// ---- 行情跑馬燈(複製一份達成無縫循環)----
const liveTickerItems = ref<TickerItem[] | null>(null)
const tickerSource = computed(() => liveTickerItems.value ?? tickerItems)
const tickerLoop = computed(() => [...tickerSource.value, ...tickerSource.value])

// ---- 淨值曲線 ----
const activeRange = ref<EquityRange>('6M')
const liveEquityCurves = ref<Partial<Record<EquityRange, EquityPoint[]>>>({})
const equityLoading = ref(false)
const equityPoints = computed(
  () => liveEquityCurves.value[activeRange.value] ?? equityCurves[activeRange.value],
)

// ---- 配置 donut ----
const allocSlices = ref<AllocationSlice[]>(allocation)
const allocCenterText = ref('NT$12.58M')

// ---- 持股風險佔比 bar(live 載入前以 mock 因子數據渲染)----
const riskShareBars = ref<{ label: string; value: number }[]>(
  riskRadar.indicators.map((name, i) => ({ label: name, value: riskRadar.scores[i] ?? 0 })),
)

// ---- VaR 水平 bar ----
const varItems = ref<VarMetric[]>(varMetrics)

// ---- 風險數字卡 ----
const riskData = ref<PortfolioRiskResponse | null>(null)
// undefined = 尚未載入 live 歷史(顯示 mock);null = live 但無 beta(顯示 —)
const liveBeta = ref<number | null | undefined>(undefined)

const statMaxDrawdown = computed(() =>
  riskData.value ? `${(riskData.value.maxDrawdown * 100).toFixed(1)}%` : '-8.4%',
)
const statVolatility = computed(() =>
  riskData.value ? `${(riskData.value.historicalAnnualizedVolatility * 100).toFixed(1)}%` : '14.2%',
)
const statBeta = computed(() => {
  if (liveBeta.value === undefined) return '1.08'
  return liveBeta.value === null ? '—' : liveBeta.value.toFixed(2)
})

// ---- AI Research Feed ----
const feedItems = ref<AiFeedItem[]>(aiFeed)
const feedEmpty = ref(false)

const equityOption = computed(() => {
  const points = equityPoints.value
  return {
    textStyle: { color: MUTED },
    grid: { left: 64, right: 20, top: 24, bottom: 32 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date.slice(5)),
      axisLine: { lineStyle: { color: 'rgba(201,168,106,0.25)' } },
      axisTick: { show: false },
      axisLabel: { color: '#6e6757', fontSize: 11, interval: 14 },
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: 'rgba(201,168,106,0.08)' } },
      axisLabel: {
        color: '#6e6757',
        fontSize: 11,
        formatter: (v: number) => formatCompactMoney(v),
      },
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'rgba(11, 18, 32, 0.95)',
      borderColor: 'rgba(201,168,106,0.4)',
      textStyle: { color: IVORY, fontSize: 12 },
    },
    series: [
      {
        type: 'line',
        data: points.map((p) => p.value),
        smooth: true,
        showSymbol: false,
        lineStyle: { width: 2, color: GOLD },
        itemStyle: { color: GOLD },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0, y: 0, x2: 0, y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(201,168,106,0.18)' },
              { offset: 1, color: 'rgba(201,168,106,0)' },
            ],
          },
        },
      },
    ],
  }
})

const allocationOption = computed(() => ({
  textStyle: { color: MUTED },
  tooltip: {
    trigger: 'item',
    formatter: '{b}: {c}%',
    backgroundColor: 'rgba(11, 18, 32, 0.95)',
    borderColor: 'rgba(201,168,106,0.4)',
    textStyle: { color: IVORY, fontSize: 12 },
  },
  title: {
    text: allocCenterText.value,
    subtext: 'TOTAL ASSETS',
    left: 'center',
    top: '40%',
    textStyle: { color: IVORY, fontSize: 20, fontFamily: "Georgia, 'Noto Serif TC', serif" },
    subtextStyle: { color: MUTED, fontSize: 10 },
  },
  series: [
    {
      type: 'pie',
      radius: ['58%', '80%'],
      center: ['50%', '50%'],
      avoidLabelOverlap: true,
      label: { show: false },
      labelLine: { show: false },
      itemStyle: { borderColor: '#0b1220', borderWidth: 2 },
      emphasis: { scaleSize: 4 },
      data: allocSlices.value.map((slice, i) => ({
        ...slice,
        itemStyle: { color: ALLOC_COLORS[i % ALLOC_COLORS.length] },
      })),
    },
  ],
}))

// ---- 持股風險佔比水平 bar ----
const riskShareOption = computed(() => ({
  textStyle: { color: MUTED },
  grid: { left: 70, right: 56, top: 10, bottom: 24 },
  xAxis: {
    type: 'value',
    splitLine: { lineStyle: { color: 'rgba(201,168,106,0.08)' } },
    axisLabel: { color: '#6e6757', fontSize: 11, formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    inverse: true,
    data: riskShareBars.value.map((bar) => bar.label),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: MUTED, fontSize: 11 },
  },
  tooltip: {
    trigger: 'axis',
    formatter: '{b}: {c}%',
    backgroundColor: 'rgba(11, 18, 32, 0.95)',
    borderColor: 'rgba(201,168,106,0.4)',
    textStyle: { color: IVORY, fontSize: 12 },
  },
  series: [
    {
      type: 'bar',
      barWidth: 12,
      data: riskShareBars.value.map((bar) => Number(bar.value.toFixed(1))),
      itemStyle: {
        color: {
          type: 'linear',
          x: 0, y: 0, x2: 1, y2: 0,
          colorStops: [
            { offset: 0, color: 'rgba(201,168,106,0.35)' },
            { offset: 1, color: 'rgba(201,168,106,0.9)' },
          ],
        },
      },
      label: { show: true, position: 'right', color: GOLD, fontSize: 11, formatter: '{c}%' },
    },
  ],
}))

// ---- VaR 水平 bar ----
const varOption = computed(() => ({
  textStyle: { color: MUTED },
  grid: { left: 80, right: 48, top: 10, bottom: 24 },
  xAxis: {
    type: 'value',
    max: 0,
    splitLine: { lineStyle: { color: 'rgba(201,168,106,0.08)' } },
    axisLabel: { color: '#6e6757', fontSize: 11, formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    data: varItems.value.map((m) => m.label),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: MUTED, fontSize: 11 },
  },
  tooltip: {
    trigger: 'axis',
    formatter: '{b}: {c}%',
    backgroundColor: 'rgba(11, 18, 32, 0.95)',
    borderColor: 'rgba(201,168,106,0.4)',
    textStyle: { color: IVORY, fontSize: 12 },
  },
  series: [
    {
      type: 'bar',
      barWidth: 12,
      data: varItems.value.map((m) => Number(m.value.toFixed(2))),
      itemStyle: {
        color: {
          type: 'linear',
          x: 0, y: 0, x2: 1, y2: 0,
          colorStops: [
            { offset: 0, color: 'rgba(176,92,92,0.85)' },
            { offset: 1, color: 'rgba(176,92,92,0.3)' },
          ],
        },
      },
      label: { show: true, position: 'right', color: DOWN, fontSize: 11, formatter: '{c}%' },
    },
  ],
}))

const feedStatusLabel: Record<string, string> = {
  completed: 'COMPLETED',
  running: 'RUNNING',
  failed: 'FAILED',
}

// ================= 即時資料載入 =================

function formatLocalDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function rangeFromDate(range: EquityRange): string {
  const from = new Date()
  switch (range) {
    case '1M':
      from.setMonth(from.getMonth() - 1)
      break
    case '3M':
      from.setMonth(from.getMonth() - 3)
      break
    case '6M':
      from.setMonth(from.getMonth() - 6)
      break
    case '1Y':
      from.setFullYear(from.getFullYear() - 1)
      break
  }
  return formatLocalDate(from)
}

const formatMoney = (value: number) => `NT$${Math.round(value).toLocaleString('en-US')}`

const formatSignedMoney = (value: number) =>
  `${value < 0 ? '-' : ''}NT$${Math.round(Math.abs(value)).toLocaleString('en-US')}`

const formatCompactMoney = (value: number, digits = 1) => {
  const abs = Math.abs(value)
  if (abs >= 1_000_000) return `NT$${(value / 1_000_000).toFixed(digits)}M`
  if (abs >= 1_000) return `NT$${(value / 1_000).toFixed(0)}K`
  return `NT$${Math.round(value)}`
}

const truncate = (text: string, max = 42) => (text.length > max ? `${text.slice(0, max)}…` : text)

function relativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime()
  const minutes = Math.floor(diffMs / 60000)
  if (minutes < 1) return '剛剛'
  if (minutes < 60) return `${minutes} 分鐘前`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} 小時前`
  return `${Math.floor(hours / 24)} 天前`
}

function mapFeedStatus(status: string): AiFeedItem['status'] {
  const normalized = status.toLowerCase()
  if (normalized === 'completed' || normalized === 'succeeded') return 'completed'
  if (normalized === 'running' || normalized === 'pending') return 'running'
  return 'failed'
}

// KPI 1/2:所有組合估值加總
function applyValuationKpis(valuations: PortfolioValuationResponse[]) {
  const totalAssets = valuations.reduce((sum, v) => sum + v.totalAssetValue, 0)
  const totalCash = valuations.reduce((sum, v) => sum + v.cashBalance, 0)
  const totalPnl = valuations.reduce((sum, v) => sum + v.totalUnrealizedPnl, 0)
  const totalCost = valuations.reduce((sum, v) => sum + v.totalCostValue, 0)

  setKpi(0, {
    label: 'Total Assets',
    value: totalAssets,
    prefix: 'NT$',
    suffix: '',
    decimals: 0,
    sub: `${valuations.length} 個組合 · 現金 ${formatMoney(totalCash)}`,
    tone: 'neutral',
  })

  if (totalCost > 0) {
    const pct = (totalPnl / totalCost) * 100
    setKpi(1, {
      label: 'Total Return',
      value: pct,
      prefix: pct >= 0 ? '+' : '',
      suffix: '%',
      decimals: 2,
      sub: `未實現損益 ${formatSignedMoney(totalPnl)}`,
      tone: pct >= 0 ? 'positive' : 'negative',
    })
  }
}

// KPI 3/4 + 持股風險佔比 + VaR + 風險數字卡
function applyRisk(risk: PortfolioRiskResponse) {
  riskData.value = risk

  setKpi(2, {
    label: 'Sharpe Ratio',
    value: risk.sharpeRatio,
    prefix: '',
    suffix: '',
    decimals: 2,
    sub: '滾動 252 日',
    tone: 'neutral',
  })
  setKpi(3, {
    label: 'Max Drawdown',
    value: risk.maxDrawdown * 100,
    prefix: '',
    suffix: '%',
    decimals: 1,
    sub: '過去一年',
    tone: 'negative',
  })

  const topHoldings = [...risk.holdings]
    .sort((a, b) => b.componentRiskShare - a.componentRiskShare)
    .slice(0, 5)
  if (topHoldings.length > 0) {
    riskShareBars.value = topHoldings.map((h) => ({
      label: h.ticker,
      value: h.componentRiskShare * 100,
    }))
  }

  if (risk.horizons.length > 0) {
    varItems.value = [...risk.horizons]
      .sort((a, b) => a.horizonDays - b.horizonDays)
      .map((h) => ({ label: `VaR ${h.horizonDays}D`, value: h.historicalVaR * 100 }))
  }
}

// 配置 donut:primary 組合持股以產業分組
function applyAllocation(valuation: PortfolioValuationResponse) {
  const groups = new Map<string, number>()
  for (const holding of valuation.holdings) {
    if (holding.marketValue == null || holding.marketValue <= 0) continue
    const key = holding.industry ?? holding.sector ?? '其他'
    groups.set(key, (groups.get(key) ?? 0) + holding.marketValue)
  }
  const total = [...groups.values()].reduce((sum, v) => sum + v, 0)
  if (total <= 0) return

  allocSlices.value = [...groups.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([name, value]) => ({ name, value: Number(((value / total) * 100).toFixed(1)) }))
  allocCenterText.value = formatCompactMoney(valuation.totalAssetValue, 2)
}

// 行情跑馬燈:加權指數 + 0050 + 0050 前五大成分股(後端 /market/ticker)
async function loadTicker() {
  try {
    const entries = await getMarketTicker()
    if (entries.length === 0) return
    liveTickerItems.value = entries.map((entry) => ({
      code: entry.code,
      name: entry.name,
      price: entry.close.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
      changePct: entry.changePct ?? 0,
    }))
  } catch {
    // 失敗時保留 mock 跑馬燈
  }
}

// 淨值走勢:切換區間時重新打 API;失敗或空資料保留 mock 曲線
async function loadEquityHistory(range: EquityRange) {
  const portfolioId = primaryPortfolioId.value
  if (!portfolioId) return
  equityLoading.value = true
  try {
    const history = await getPortfolioValuationHistory(portfolioId, {
      from: rangeFromDate(range),
      to: formatLocalDate(new Date()),
    })
    const points = history.points
      .map((p) => ({ date: p.date, value: p.totalAssetValue ?? p.totalMarketValue }))
      .filter((p) => p.value > 0)
    if (points.length > 0) {
      liveEquityCurves.value = { ...liveEquityCurves.value, [range]: points }
    }
    liveBeta.value = history.beta
  } catch {
    // 保留 mock 曲線與 beta
  } finally {
    equityLoading.value = false
  }
}

const handleRangeChange = (range: EquityRange) => {
  activeRange.value = range
  void loadEquityHistory(range)
}

// AI 研究動態:Research + Agent Run 合併,按建立時間降冪取前 6
async function loadFeed() {
  const [researchResult, agentResult] = await Promise.allSettled([
    listResearchRuns({ limit: 5 }),
    listAgentRuns({ limit: 5 }),
  ])
  // 兩者皆失敗才保留 mock
  if (researchResult.status === 'rejected' && agentResult.status === 'rejected') return

  const merged: { item: AiFeedItem; createdAtUtc: string }[] = []
  if (researchResult.status === 'fulfilled') {
    for (const run of researchResult.value) {
      merged.push({
        item: {
          title: truncate(`${run.ticker} ${run.question}`),
          kind: 'Research',
          status: mapFeedStatus(run.status),
          time: relativeTime(run.createdAtUtc),
        },
        createdAtUtc: run.createdAtUtc,
      })
    }
  }
  if (agentResult.status === 'fulfilled') {
    for (const run of agentResult.value) {
      merged.push({
        item: {
          title: truncate(`${run.workflowType} / ${run.agentType}`),
          kind: 'Agent Run',
          status: mapFeedStatus(run.status),
          time: relativeTime(run.createdAtUtc),
        },
        createdAtUtc: run.createdAtUtc,
      })
    }
  }

  merged.sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
  const items = merged.slice(0, 6).map((entry) => entry.item)
  feedItems.value = items
  feedEmpty.value = items.length === 0
}

onMounted(async () => {
  if (!authStore.isAuthenticated) return

  const portfolios = await getPortfolios().catch(() => null)
  if (!portfolios || portfolios.length === 0) return
  dataMode.value = 'live'

  // 研究動態獨立載入,與組合資料互不影響
  void loadFeed()

  const valuationResults = await Promise.allSettled(
    portfolios.map((portfolio) => getPortfolioValuation(portfolio.id)),
  )
  const valuations = valuationResults.flatMap((result) =>
    result.status === 'fulfilled' ? [result.value] : [],
  )
  if (valuations.length === 0) return

  applyValuationKpis(valuations)

  // primary = 估值總資產最大的組合
  const primary = valuations.reduce((best, current) =>
    current.totalAssetValue > best.totalAssetValue ? current : best,
  )
  primaryPortfolioId.value = primary.portfolioId

  applyAllocation(primary)
  void loadTicker()

  await Promise.allSettled([
    getPortfolioRisk(primary.portfolioId, {
      from: rangeFromDate('1Y'),
      to: formatLocalDate(new Date()),
      horizonDays: 30,
      confidenceLevel: 0.95,
      simulations: 10000,
    }).then(applyRisk),
    loadEquityHistory(activeRange.value),
  ])
})
</script>

<template>
  <div class="prestige-page">
    <!-- ============ Hero ============ -->
    <section class="hero">
      <div class="hero-content">
        <span class="hero-eyebrow fade-in" style="--d: 0s">AI-ASSISTED INVESTMENT ANALYTICS</span>
        <h1 class="hero-title fade-in" style="--d: 0.15s">EQUITYLENS</h1>
        <p class="hero-sub fade-in" style="--d: 0.3s">
          整合投資組合帳務、量化風險與 AI 研究工作流<br />
          讓每一個投資決策都有證據可循
        </p>
        <div class="hero-cta fade-in" style="--d: 0.45s">
          <button class="btn-gold" @click="router.push({ name: 'dashboard' })">進入儀表板</button>
          <button class="btn-text" @click="router.push({ name: 'research' })">查看 AI 研究</button>
        </div>

        <div class="hero-badge fade-in" style="--d: 0.55s">
          <span class="prestige-tag data-badge" :class="{ live: dataMode === 'live' }">
            <span class="badge-dot" />{{ dataMode === 'live' ? 'LIVE DATA' : 'DEMO DATA' }}
          </span>
        </div>

        <div class="hero-kpis fade-in" style="--d: 0.6s">
          <div v-for="kpi in kpis" :key="kpi.label" class="kpi">
            <span class="kpi-label">{{ kpi.label }}</span>
            <span :class="['kpi-value', kpi.tone]">
              {{ kpi.prefix }}{{ formatKpiValue(kpi.value, kpi.decimals) }}{{ kpi.suffix }}
            </span>
            <span class="kpi-sub">{{ kpi.sub }}</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ 行情跑馬燈 ============ -->
    <div class="ticker">
      <div class="ticker-track">
        <span v-for="(item, i) in tickerLoop" :key="i" class="ticker-item">
          <span class="ticker-code">{{ item.code }} {{ item.name }}</span>
          <span class="ticker-price">{{ item.price }}</span>
          <span :class="['ticker-change', item.changePct >= 0 ? 'up' : 'down']">
            {{ item.changePct >= 0 ? '▲' : '▼' }} {{ Math.abs(item.changePct).toFixed(2) }}%
          </span>
        </span>
      </div>
    </div>

    <!-- ============ Portfolio Pulse ============ -->
    <section class="section">
      <div class="section-head">
        <div>
          <span class="section-label">PORTFOLIO PULSE</span>
          <h2 class="section-title">組合淨值走勢</h2>
        </div>
        <div class="range-switch">
          <span v-if="equityLoading" class="range-loading">載入中…</span>
          <button
            v-for="range in equityRanges"
            :key="range"
            :class="['range-btn', activeRange === range && 'active']"
            @click="handleRangeChange(range)"
          >
            {{ range }}
          </button>
        </div>
      </div>

      <div class="pulse-grid">
        <div class="panel panel-pad">
          <TechChart :option="equityOption" height="360px" />
        </div>
        <div class="panel panel-pad">
          <span class="panel-label">ASSET ALLOCATION</span>
          <TechChart :option="allocationOption" height="270px" />
          <div class="alloc-legend">
            <div v-for="(slice, i) in allocSlices" :key="slice.name" class="alloc-legend-item">
              <span class="alloc-dot" :style="{ background: ALLOC_COLORS[i % ALLOC_COLORS.length] }" />
              <span class="alloc-name">{{ slice.name }}</span>
              <span class="alloc-pct">{{ slice.value }}%</span>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ Risk Matrix ============ -->
    <section class="section">
      <div class="section-head">
        <div>
          <span class="section-label">RISK MATRIX</span>
          <h2 class="section-title">風險矩陣</h2>
        </div>
      </div>

      <div class="risk-grid">
        <div class="panel panel-pad">
          <span class="panel-label">HOLDINGS RISK SHARE / 持股風險佔比</span>
          <TechChart :option="riskShareOption" height="300px" />
        </div>
        <div class="panel panel-pad">
          <span class="panel-label">VALUE AT RISK(日,1 年歷史模擬)</span>
          <TechChart :option="varOption" height="300px" />
        </div>
        <div class="risk-side">
          <div class="panel risk-stat">
            <span class="kpi-label">Max Drawdown</span>
            <span class="kpi-value negative">{{ statMaxDrawdown }}</span>
            <span class="kpi-sub">{{ riskData ? '過去一年' : '2025.09 – 2025.11 區間' }}</span>
          </div>
          <div class="panel risk-stat">
            <span class="kpi-label">Volatility(年化)</span>
            <span class="kpi-value">{{ statVolatility }}</span>
            <span class="kpi-sub">{{ riskData ? '年化 · 近一年' : '滾動 90 日' }}</span>
          </div>
          <div class="panel risk-stat">
            <span class="kpi-label">Beta(vs 加權指數)</span>
            <span class="kpi-value">{{ statBeta }}</span>
            <span class="kpi-sub">滾動 252 日</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ AI Research Feed ============ -->
    <section class="section">
      <div class="section-head">
        <div>
          <span class="section-label">AI RESEARCH FEED</span>
          <h2 class="section-title">AI 研究動態</h2>
        </div>
        <button class="btn-text" @click="router.push({ name: 'research' })">全部研究</button>
      </div>

      <div class="panel feed-panel">
        <div v-if="feedEmpty" class="feed-empty">尚無研究紀錄</div>
        <template v-else>
          <div v-for="(item, i) in feedItems" :key="i" class="feed-item">
            <span :class="['feed-dot', item.status]" />
            <div class="feed-body">
              <span class="feed-title">{{ item.title }}</span>
              <span class="feed-meta">{{ item.time }}</span>
            </div>
            <span class="feed-kind">{{ item.kind }}</span>
            <span :class="['feed-status', item.status]">{{ feedStatusLabel[item.status] }}</span>
          </div>
        </template>
      </div>
    </section>

    <!-- ============ Footer ============ -->
    <footer class="footer">
      <span class="footer-brand">EQUITYLENS © 2026</span>
      <span>本頁數據為展示用途,不構成投資建議</span>
    </footer>
  </div>
</template>

<style scoped>
.prestige-page {
  --gold: #c9a86a;
  --gold-border: rgba(201, 168, 106, 0.25);
  --gold-border-soft: rgba(201, 168, 106, 0.14);
  --ivory: #f5efe0;
  --muted: #9a917c;
  --up: #7fa387;
  --down: #b05c5c;
  --panel-bg: rgba(201, 168, 106, 0.04);
  --serif: Georgia, 'Noto Serif TC', serif;

  min-height: 100vh;
  background: linear-gradient(180deg, #0b1220 0%, #101a2e 100%);
  color: var(--ivory);
  font-family: 'Inter', 'Noto Sans TC', sans-serif;
}

/* ---- 進場:慢速淡入 ---- */
.fade-in {
  opacity: 0;
  animation: prestige-fade 1s ease-out forwards;
  animation-delay: var(--d, 0s);
}

@keyframes prestige-fade {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* ---- Hero ---- */
.hero {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: calc(100vh - 60px);
  overflow: hidden;
}

/* 非常淡的同心紋理 */
.hero::before {
  content: '';
  position: absolute;
  inset: 0;
  background: repeating-radial-gradient(
    circle at 50% 42%,
    transparent 0,
    transparent 118px,
    rgba(201, 168, 106, 0.035) 119px,
    transparent 120px
  );
  pointer-events: none;
}

.hero-content {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 22px;
  padding: 56px 24px;
  width: min(1100px, 100%);
}

.hero-eyebrow {
  display: flex;
  align-items: center;
  gap: 16px;
  color: var(--gold);
  font-size: 11px;
  letter-spacing: 0.32em;
}

.hero-eyebrow::before,
.hero-eyebrow::after {
  content: '';
  width: 56px;
  height: 1px;
  background: var(--gold-border);
}

.hero-title {
  margin: 0;
  font-family: var(--serif);
  font-size: clamp(44px, 8vw, 96px);
  font-weight: 500;
  letter-spacing: 0.1em;
  line-height: 1.05;
  text-align: center;
  color: var(--ivory);
}

.hero-sub {
  margin: 0;
  text-align: center;
  color: var(--muted);
  font-size: 15px;
  line-height: 1.9;
}

.hero-cta {
  display: flex;
  align-items: center;
  gap: 28px;
  margin-top: 10px;
}

.btn-gold {
  padding: 12px 34px;
  background: transparent;
  border: 1px solid var(--gold);
  border-radius: 4px;
  color: var(--gold);
  font-size: 13px;
  letter-spacing: 0.18em;
  cursor: pointer;
  transition: background 0.3s ease, color 0.3s ease;
}

.btn-gold:hover {
  background: var(--gold);
  color: #0b1220;
}

.btn-text {
  padding: 12px 4px;
  background: transparent;
  border: none;
  border-bottom: 1px solid transparent;
  color: var(--ivory);
  font-size: 13px;
  letter-spacing: 0.18em;
  cursor: pointer;
  transition: color 0.3s ease, border-color 0.3s ease;
}

.btn-text:hover {
  color: var(--gold);
  border-bottom-color: var(--gold-border);
}

.hero-badge {
  display: flex;
  justify-content: flex-end;
  width: 100%;
}

.data-badge .badge-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--muted);
}

.data-badge.live .badge-dot {
  background: var(--gold);
}

.data-badge:not(.live) {
  border-color: var(--gold-border-soft);
  color: var(--muted);
}

.hero-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  width: 100%;
  margin-top: 22px;
  border-top: 1px solid var(--gold-border-soft);
  border-bottom: 1px solid var(--gold-border-soft);
}

.kpi {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 26px 16px;
  border-right: 1px solid var(--gold-border-soft);
}

.kpi:last-child {
  border-right: none;
}

.kpi-label {
  color: var(--muted);
  font-size: 11px;
  letter-spacing: 0.22em;
  text-transform: uppercase;
}

.kpi-value {
  font-family: var(--serif);
  font-size: 26px;
  font-variant-numeric: tabular-nums;
  color: var(--ivory);
}

.kpi-value.positive {
  color: var(--gold);
}

.kpi-value.negative {
  color: var(--down);
}

.kpi-sub {
  color: var(--muted);
  font-size: 11px;
}

/* ---- 行情跑馬燈 ---- */
.ticker {
  overflow: hidden;
  border-top: 1px solid var(--gold-border);
  border-bottom: 1px solid var(--gold-border);
}

.ticker-track {
  display: flex;
  width: max-content;
  animation: prestige-marquee 48s linear infinite;
}

@keyframes prestige-marquee {
  from {
    transform: translateX(0);
  }
  to {
    transform: translateX(-50%);
  }
}

.ticker-item {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 12px 30px;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  border-right: 1px solid var(--gold-border-soft);
}

.ticker-code {
  color: var(--muted);
  letter-spacing: 0.08em;
}

.ticker-price {
  color: var(--ivory);
}

.ticker-change.up {
  color: var(--up);
}

.ticker-change.down {
  color: var(--down);
}

/* ---- 共用 section ---- */
.section {
  width: min(1100px, calc(100% - 48px));
  margin: 0 auto;
  padding-top: 72px;
}

.section-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 24px;
}

.section-label {
  display: block;
  color: var(--gold);
  font-size: 11px;
  letter-spacing: 0.3em;
  margin-bottom: 10px;
}

.section-title {
  margin: 0;
  font-family: var(--serif);
  font-size: 28px;
  font-weight: 500;
  letter-spacing: 0.06em;
  color: var(--ivory);
}

.panel {
  background: var(--panel-bg);
  border: 1px solid var(--gold-border);
  border-radius: 5px;
}

.panel-pad {
  padding: 22px;
}

.panel-label {
  display: block;
  color: var(--muted);
  font-size: 11px;
  letter-spacing: 0.24em;
  margin-bottom: 12px;
}

/* ---- 配置圖例(donut 標籤改列於圖下,避免擁擠截斷) ---- */
.alloc-legend {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px 16px;
  margin-top: 8px;
}

.alloc-legend-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--muted);
}

.alloc-dot {
  width: 8px;
  height: 8px;
  border-radius: 2px;
  flex-shrink: 0;
}

.alloc-name {
  flex: 1;
  color: var(--ivory);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.alloc-pct {
  font-variant-numeric: tabular-nums;
}

/* ---- Portfolio Pulse ---- */
.pulse-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 16px;
}

.range-switch {
  display: flex;
  gap: 8px;
}

.range-loading {
  align-self: center;
  color: var(--muted);
  font-size: 11px;
  letter-spacing: 0.08em;
}

.range-btn {
  padding: 6px 16px;
  background: transparent;
  border: 1px solid var(--gold-border);
  border-radius: 4px;
  color: var(--muted);
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  letter-spacing: 0.08em;
  cursor: pointer;
  transition: background 0.25s ease, color 0.25s ease, border-color 0.25s ease;
}

.range-btn:hover {
  color: var(--gold);
  border-color: var(--gold);
}

.range-btn.active {
  background: var(--gold);
  border-color: var(--gold);
  color: #0b1220;
}

/* ---- Risk Matrix ---- */
.risk-grid {
  display: grid;
  grid-template-columns: 1fr 1.2fr 0.8fr;
  gap: 16px;
}

.risk-side {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.risk-stat {
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 8px;
  padding: 18px 22px;
}

.risk-stat .kpi-value {
  font-size: 24px;
}

/* ---- AI Research Feed ---- */
.feed-panel {
  padding: 4px 0;
}

.feed-empty {
  padding: 32px 24px;
  text-align: center;
  color: var(--muted);
  font-size: 13px;
  letter-spacing: 0.08em;
}

.feed-item {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
  border-bottom: 1px solid var(--gold-border-soft);
  transition: background 0.25s ease;
}

.feed-item:last-child {
  border-bottom: none;
}

.feed-item:hover {
  background: rgba(201, 168, 106, 0.05);
}

.feed-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
}

.feed-dot.completed {
  background: var(--gold);
}

.feed-dot.running {
  background: var(--ivory);
}

.feed-dot.failed {
  background: var(--down);
}

.feed-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1;
  min-width: 0;
}

.feed-title {
  font-size: 14px;
  color: var(--ivory);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-meta {
  font-size: 11px;
  color: var(--muted);
}

.feed-kind {
  padding: 3px 10px;
  border: 1px solid var(--gold-border-soft);
  border-radius: 3px;
  color: var(--muted);
  font-size: 10px;
  letter-spacing: 0.12em;
  white-space: nowrap;
}

.feed-status {
  font-size: 11px;
  letter-spacing: 0.14em;
  white-space: nowrap;
}

.feed-status.completed {
  color: var(--gold);
}

.feed-status.running {
  color: var(--ivory);
}

.feed-status.failed {
  color: var(--down);
}

/* ---- Footer ---- */
.footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  width: min(1100px, calc(100% - 48px));
  margin: 72px auto 0;
  padding: 22px 0 30px;
  border-top: 1px solid var(--gold-border);
  color: var(--muted);
  font-size: 12px;
}

.footer-brand {
  letter-spacing: 0.2em;
  color: var(--gold);
}

/* ---- Responsive ---- */
@media (max-width: 960px) {
  .pulse-grid,
  .risk-grid {
    grid-template-columns: 1fr;
  }

  .hero-kpis {
    grid-template-columns: repeat(2, 1fr);
  }

  .kpi:nth-child(2) {
    border-right: none;
  }

  .kpi:nth-child(1),
  .kpi:nth-child(2) {
    border-bottom: 1px solid var(--gold-border-soft);
  }
}

@media (max-width: 560px) {
  .hero-kpis {
    grid-template-columns: 1fr;
  }

  .kpi {
    border-right: none;
    border-bottom: 1px solid var(--gold-border-soft);
  }

  .kpi:last-child {
    border-bottom: none;
  }

  .hero-cta {
    flex-direction: column;
    gap: 14px;
  }

  .section-head {
    flex-direction: column;
    align-items: flex-start;
  }

  .feed-kind {
    display: none;
  }

  .footer {
    flex-direction: column;
    gap: 6px;
    text-align: center;
  }
}
</style>
