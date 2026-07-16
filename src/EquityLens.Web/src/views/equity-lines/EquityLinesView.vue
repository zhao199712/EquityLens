<template>
  <div class="equity-lines-page">
    <!-- Hero / Header -->
    <div class="relative">
      <ParticleCanvas theme="warm" />
      <div class="relative z-10 equity-hero">
        <ScrollReveal>
          <div class="page-heading">
            <div>
              <p class="eyebrow">EQUITY LINES</p>
              <h1>股票走勢比較</h1>
              <p class="hero-subtitle">
                追蹤多檔標的的價格走勢，以標準化報酬比較相對強弱。
              </p>
            </div>
            <div class="hero-actions">
              <button class="kimi-btn kimi-btn-solid" @click="loadSampleSecurities">
                載入範例標的
              </button>
              <button class="kimi-btn" @click="refreshAllPrices" :disabled="isSyncing">
                {{ isSyncing ? '同步中...' : '同步價格' }}
              </button>
            </div>
          </div>
        </ScrollReveal>
      </div>
    </div>

    <div class="kimi-content equity-content">
      <!-- KPI -->
      <ScrollReveal>
        <div class="kimi-kpi-grid">
          <div v-for="(kpi, i) in kpis" :key="i" class="kimi-kpi-cell glass-card">
            <span class="kimi-caption">{{ kpi.label }}</span>
            <span class="kimi-data">{{ kpi.value }}</span>
            <span class="text-xs" style="color: var(--text-muted)">{{ kpi.sub }}</span>
            <div class="accent-bar" />
          </div>
        </div>
      </ScrollReveal>

      <!-- Main comparison chart -->
      <ScrollReveal class="mt-20">
        <div class="glass-panel">
          <div class="section-header-row">
            <div>
              <h2>多標的標準化走勢</h2>
              <span class="kimi-caption">NORMALIZED PRICE COMPARISON</span>
            </div>
            <div class="kimi-time-range">
              <button
                v-for="r in timeRanges"
                :key="r.value"
                :class="['kimi-time-btn', timeRange === r.value && 'active']"
                @click="timeRange = r.value"
              >
                {{ r.label }}
              </button>
            </div>
          </div>
          <div class="chart-wrapper">
            <EquityLinesChart :series="normalizedSeries" :loading="isLoading" />
          </div>
          <div class="legend-row">
            <div
              v-for="s in normalizedSeries"
              :key="s.securityId"
              class="legend-item"
              @mouseenter="highlightedSecurity = s.securityId"
              @mouseleave="highlightedSecurity = null"
            >
              <span class="legend-dot" :style="{ backgroundColor: s.color }" />
              <span class="legend-label">{{ s.ticker }}</span>
              <span class="legend-value" :style="{ color: s.totalReturn >= 0 ? '#34d399' : '#f87171' }">
                {{ s.totalReturn >= 0 ? '+' : '' }}{{ s.totalReturn.toFixed(2) }}%
              </span>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Security selector + table -->
      <div class="mt-20 grid grid-cols-1 lg:grid-cols-3 gap-6">
        <ScrollReveal class="lg:col-span-1">
          <div class="glass-panel h-full">
            <div class="section-header-row mb-4">
              <div>
                <h2>選擇標的</h2>
                <span class="kimi-caption">SELECT SECURITIES</span>
              </div>
            </div>
            <div class="security-search">
              <input
                v-model="searchQuery"
                type="text"
                placeholder="搜尋代號或名稱..."
                class="glass-input w-full"
              />
            </div>
            <div class="security-list">
              <div
                v-for="s in filteredAvailableSecurities"
                :key="s.id"
                :class="['security-item', selectedIds.includes(s.id) && 'selected']"
                @click="toggleSecurity(s.id)"
              >
                <div class="flex items-center gap-2">
                  <span class="ticker-badge">{{ s.ticker }}</span>
                  <span class="security-name">{{ s.name }}</span>
                </div>
                <span class="exchange-tag">{{ s.exchange }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <ScrollReveal class="lg:col-span-2">
          <div class="glass-panel">
            <div class="section-header-row mb-4">
              <div>
                <h2>標的詳情</h2>
                <span class="kimi-caption">SECURITY DETAILS</span>
              </div>
            </div>
            <DataTable
              :headers="['代號', '名稱', '交易所', '產業', '期間漲跌', '最高', '最低', '成交量']"
              :rows="securityTableRows"
              :dark="true"
            />
          </div>
        </ScrollReveal>
      </div>

      <!-- Hand-drawn style mini charts -->
      <div class="mt-20">
        <ScrollReveal>
          <div class="section-header-row mb-6">
            <div>
              <h2>個股走勢速覽</h2>
              <span class="kimi-caption">HAND-DRAWN PRICE SKETCHES</span>
            </div>
          </div>
        </ScrollReveal>
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <ScrollReveal v-for="s in normalizedSeries" :key="s.securityId" :delay="0.1">
            <HandDrawnSparkline
              :title="s.ticker"
              :subtitle="s.name"
              :data="s.values"
              :change="s.totalReturn"
              :color="s.color"
            />
          </ScrollReveal>
        </div>
      </div>

      <!-- Correlation matrix -->
      <ScrollReveal class="mt-20 mb-20">
        <div class="glass-panel">
          <div class="section-header-row mb-4">
            <div>
              <h2>報酬相關性矩陣</h2>
              <span class="kimi-caption">RETURN CORRELATION MATRIX</span>
            </div>
          </div>
          <CorrelationMatrix :series="normalizedSeries" />
        </div>
      </ScrollReveal>

      <div class="h-10" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import ParticleCanvas from '../../components/kimi/ParticleCanvas.vue'
import DataTable from '../../components/kimi/DataTable.vue'
import EquityLinesChart from './components/EquityLinesChart.vue'
import HandDrawnSparkline from './components/HandDrawnSparkline.vue'
import CorrelationMatrix from './components/CorrelationMatrix.vue'
import { getSecurities, getSecurityPrices, syncSecurityPrices, type Security, type MarketPrice } from '../../services/equityLines'

const timeRanges = [
  { label: '1M', value: '1M' },
  { label: '3M', value: '3M' },
  { label: '6M', value: '6M' },
  { label: '1Y', value: '1Y' },
]
const timeRange = ref('1Y')

const availableSecurities = ref<Security[]>([])
const selectedIds = ref<string[]>([])
const priceMap = ref<Record<string, MarketPrice[]>>({})
const isLoading = ref(false)
const isSyncing = ref(false)
const searchQuery = ref('')
const highlightedSecurity = ref<string | null>(null)

const palette = ['#60a5fa', '#c084fc', '#34d399', '#fbbf24', '#f87171', '#818cf8']

function getFromDate(range: string): string {
  const now = new Date()
  const d = new Date(now)
  switch (range) {
    case '1M': d.setMonth(d.getMonth() - 1); break
    case '3M': d.setMonth(d.getMonth() - 3); break
    case '6M': d.setMonth(d.getMonth() - 6); break
    case '1Y': d.setFullYear(d.getFullYear() - 1); break
  }
  return d.toISOString().split('T')[0]
}

async function fetchSecurities() {
  try {
    availableSecurities.value = await getSecurities()
  } catch (e) {
    console.error('Failed to load securities', e)
  }
}

async function loadPricesForSelection() {
  if (selectedIds.value.length === 0) return
  isLoading.value = true
  const from = getFromDate(timeRange.value)
  const to = new Date().toISOString().split('T')[0]
  try {
    const entries = await Promise.all(
      selectedIds.value.map(async (id) => {
        const prices = await getSecurityPrices(id, from, to)
        return [id, prices] as const
      })
    )
    priceMap.value = Object.fromEntries(entries)
  } catch (e) {
    console.error('Failed to load prices', e)
  } finally {
    isLoading.value = false
  }
}

async function loadSampleSecurities() {
  const sampleTickers = ['2330', '0050', '2317', '00878']
  const sample = availableSecurities.value.filter((s: Security) => sampleTickers.includes(s.ticker))
  if (sample.length > 0) {
    selectedIds.value = sample.map((s: Security) => s.id)
    await loadPricesForSelection()
  }
}

async function refreshAllPrices() {
  if (selectedIds.value.length === 0) return
  isSyncing.value = true
  try {
    await Promise.all(selectedIds.value.map((id) => syncSecurityPrices(id, 365, true)))
    await loadPricesForSelection()
  } catch (e) {
    console.error('Failed to sync prices', e)
  } finally {
    isSyncing.value = false
  }
}

function toggleSecurity(id: string) {
  const idx = selectedIds.value.indexOf(id)
  if (idx >= 0) {
    selectedIds.value.splice(idx, 1)
  } else if (selectedIds.value.length < 6) {
    selectedIds.value.push(id)
  }
  loadPricesForSelection()
}

const filteredAvailableSecurities = computed(() => {
  const q = searchQuery.value.trim().toLowerCase()
  if (!q) return availableSecurities.value
  return availableSecurities.value.filter(
    (s: Security) =>
      s.ticker.toLowerCase().includes(q) ||
      s.name.toLowerCase().includes(q) ||
      (s.sector && s.sector.toLowerCase().includes(q))
  )
})

interface NormalizedSeries {
  securityId: string
  ticker: string
  name: string
  color: string
  labels: string[]
  values: number[]
  totalReturn: number
  high: number
  low: number
  volume: number
}

const normalizedSeries = computed<NormalizedSeries[]>(() => {
  return selectedIds.value
    .map((id: string, idx: number) => {
      const sec = availableSecurities.value.find((s: Security) => s.id === id)
      const prices = priceMap.value[id] || []
      if (!sec || prices.length === 0) return null

      const sorted = [...prices].sort((a, b) =>
        new Date(a.priceTime).getTime() - new Date(b.priceTime).getTime()
      )
      const base = Number(sorted[0].close)
      const values = sorted.map((p) => (Number(p.close) / base - 1) * 100)
      const labels = sorted.map((p) => new Date(p.priceTime).toLocaleDateString('zh-TW', { month: 'short', day: 'numeric' }))
      const closeValues = sorted.map((p) => Number(p.close))
      const high = Math.max(...closeValues)
      const low = Math.min(...closeValues)
      const totalVolume = sorted.reduce((sum, p) => sum + (p.volume || 0), 0)

      return {
        securityId: id,
        ticker: sec.ticker,
        name: sec.name,
        color: palette[idx % palette.length],
        labels,
        values,
        totalReturn: values[values.length - 1] || 0,
        high,
        low,
        volume: totalVolume,
      }
    })
    .filter((x): x is NormalizedSeries => x !== null)
})

const kpis = computed(() => {
  const series = normalizedSeries.value
  if (series.length === 0) {
    return [
      { label: '追蹤標的', value: '0', sub: '最多 6 檔' },
      { label: '最強標的', value: '-', sub: '選擇標的後顯示' },
      { label: '最弱標的', value: '-', sub: '選擇標的後顯示' },
      { label: '平均波動', value: '-', sub: '標準差' },
    ]
  }
  const sorted = [...series].sort((a, b) => b.totalReturn - a.totalReturn)
  const returns = series.map((s) => s.totalReturn)
  const mean = returns.reduce((a, b) => a + b, 0) / returns.length
  const variance = returns.reduce((sum, r) => sum + Math.pow(r - mean, 2), 0) / returns.length
  return [
    { label: '追蹤標的', value: String(series.length), sub: '最多 6 檔' },
    { label: '最強標的', value: sorted[0].ticker, sub: `+${sorted[0].totalReturn.toFixed(2)}%` },
    { label: '最弱標的', value: sorted[sorted.length - 1].ticker, sub: `${sorted[sorted.length - 1].totalReturn.toFixed(2)}%` },
    { label: '報酬標準差', value: `${Math.sqrt(variance).toFixed(2)}%`, sub: '分散程度' },
  ]
})

const securityTableRows = computed(() => {
  return normalizedSeries.value.map((s: NormalizedSeries) => [
    s.ticker,
    s.name,
    availableSecurities.value.find((x: Security) => x.id === s.securityId)?.exchange ?? '-',
    availableSecurities.value.find((x: Security) => x.id === s.securityId)?.sector ?? '-',
    `${s.totalReturn >= 0 ? '+' : ''}${s.totalReturn.toFixed(2)}%`,
    s.high.toFixed(2),
    s.low.toFixed(2),
    s.volume.toLocaleString(),
  ])
})

watch(timeRange, loadPricesForSelection)

onMounted(() => {
  fetchSecurities()
})
</script>

<style scoped>
.equity-lines-page {
  min-height: 100vh;
  background: var(--bg-primary);
  color: var(--text-primary);
}

.equity-hero {
  padding: 60px clamp(20px, 4vw, 80px) 120px;
}

.hero-subtitle {
  color: var(--text-muted);
  font-size: 15px;
  max-width: 520px;
  margin-top: 12px;
  line-height: 1.6;
}

.hero-actions {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.equity-content {
  margin-top: -80px;
}

.section-header-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.section-header-row h2 {
  margin-bottom: 4px;
}

.chart-wrapper {
  margin-top: 24px;
  min-height: 360px;
}

.legend-row {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  margin-top: 20px;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  padding: 6px 10px;
  border: 1px solid var(--border-subtle);
  transition: all 0.2s ease;
}

.legend-item:hover {
  border-color: var(--border-medium);
  background: var(--bg-glass-hover);
}

.legend-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
}

.legend-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}

.legend-value {
  font-size: 12px;
  font-weight: 600;
}

.security-search {
  margin-bottom: 16px;
}

.security-list {
  max-height: 360px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.security-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px;
  border: 1px solid var(--border-subtle);
  cursor: pointer;
  transition: all 0.2s ease;
}

.security-item:hover {
  background: var(--bg-glass-hover);
}

.security-item.selected {
  border-color: var(--accent-primary);
  background: rgba(96, 165, 250, 0.1);
}

.ticker-badge {
  font-family: var(--kimi-font-mono);
  font-size: 12px;
  font-weight: 700;
  padding: 2px 6px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
}

.security-name {
  font-size: 13px;
  color: var(--text-secondary);
}

.exchange-tag {
  font-size: 11px;
  color: var(--text-muted);
}

.glass-input {
  padding: 10px 14px;
  font-size: 14px;
}

.mt-20 {
  margin-top: 80px;
}

.mb-20 {
  margin-bottom: 80px;
}

.grid {
  display: grid;
}

.gap-4 {
  gap: 16px;
}

.gap-6 {
  gap: 24px;
}

@media (min-width: 768px) {
  .md\:grid-cols-2 {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (min-width: 1024px) {
  .lg\:grid-cols-2 {
    grid-template-columns: repeat(2, 1fr);
  }
  .lg\:grid-cols-3 {
    grid-template-columns: repeat(3, 1fr);
  }
  .lg\:col-span-1 {
    grid-column: span 1 / span 1;
  }
  .lg\:col-span-2 {
    grid-column: span 2 / span 2;
  }
}
</style>
