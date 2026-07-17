<script setup lang="ts">
import { computed, ref } from 'vue'
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
  type EquityRange,
} from '../../data/homeTechData'

const router = useRouter()

// ---- 頁首日期(展示用,載入時取一次)----
const todayLabel = new Intl.DateTimeFormat('zh-TW', {
  year: 'numeric',
  month: 'long',
  day: 'numeric',
  weekday: 'long',
}).format(new Date())

function formatValue(value: number, decimals: number): string {
  return value.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })
}

// TechChart 的 base option 是深色主題,以下每個 option 都覆寫成淺色版
const lightTooltip = {
  backgroundColor: '#ffffff',
  borderColor: '#e2e8f0',
  textStyle: { color: '#1e293b', fontSize: 12 },
}

// ---- 淨值曲線 ----
const activeRange = ref<EquityRange>('6M')

const equityOption = computed(() => {
  const points = equityCurves[activeRange.value]
  return {
    textStyle: { color: '#475569' },
    grid: { left: 56, right: 20, top: 24, bottom: 32 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date.slice(5)),
      axisLine: { lineStyle: { color: '#cbd5e1' } },
      axisTick: { show: false },
      axisLabel: { color: '#64748b', fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: '#eef2f7' } },
      axisLabel: {
        color: '#64748b',
        fontSize: 11,
        formatter: (v: number) => `NT$${(v / 1_000_000).toFixed(1)}M`,
      },
    },
    tooltip: { ...lightTooltip, trigger: 'axis' },
    series: [
      {
        type: 'line',
        data: points.map((p) => p.value),
        smooth: true,
        showSymbol: false,
        lineStyle: { width: 2, color: '#2563eb' },
        itemStyle: { color: '#2563eb' },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0, y: 0, x2: 0, y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(37, 99, 235, 0.14)' },
              { offset: 1, color: 'rgba(37, 99, 235, 0)' },
            ],
          },
        },
      },
    ],
  }
})

// ---- 配置 donut(專業藍階 + 灰階)----
const allocationPalette = ['#1e3a5f', '#2563eb', '#60a5fa', '#93c5fd', '#64748b', '#cbd5e1']

const allocationOption = computed(() => ({
  textStyle: { color: '#475569' },
  tooltip: { ...lightTooltip, trigger: 'item', formatter: '{b}: {c}%' },
  title: {
    text: 'NT$12.58M',
    subtext: '總資產',
    left: 'center',
    top: '42%',
    textStyle: { color: '#1e3a5f', fontSize: 20, fontWeight: 700 },
    subtextStyle: { color: '#64748b', fontSize: 11 },
  },
  series: [
    {
      type: 'pie',
      radius: ['58%', '80%'],
      center: ['50%', '50%'],
      avoidLabelOverlap: true,
      label: { color: '#475569', fontSize: 11, formatter: '{b} {c}%' },
      labelLine: { lineStyle: { color: '#cbd5e1' } },
      itemStyle: { borderColor: '#ffffff', borderWidth: 2 },
      emphasis: { scaleSize: 4 },
      data: allocation.map((slice, i) => ({
        ...slice,
        itemStyle: { color: allocationPalette[i % allocationPalette.length] },
      })),
    },
  ],
}))

// ---- 風險雷達 ----
const radarOption = computed(() => ({
  textStyle: { color: '#475569' },
  tooltip: { ...lightTooltip },
  radar: {
    indicator: riskRadar.indicators.map((name) => ({ name, max: 100 })),
    radius: '68%',
    axisName: { color: '#64748b', fontSize: 11 },
    splitLine: { lineStyle: { color: '#e2e8f0' } },
    splitArea: { areaStyle: { color: ['#ffffff', '#f4f7fb'] } },
    axisLine: { lineStyle: { color: '#e2e8f0' } },
  },
  series: [
    {
      type: 'radar',
      data: [
        {
          value: riskRadar.scores,
          name: '組合因子暴露',
          lineStyle: { color: '#2563eb', width: 2 },
          areaStyle: { color: 'rgba(37, 99, 235, 0.15)' },
          itemStyle: { color: '#2563eb' },
        },
      ],
    },
  ],
}))

// ---- VaR 水平 bar(負值,紅)----
const varOption = computed(() => ({
  textStyle: { color: '#475569' },
  grid: { left: 80, right: 44, top: 10, bottom: 26 },
  xAxis: {
    type: 'value',
    max: 0,
    splitLine: { lineStyle: { color: '#eef2f7' } },
    axisLabel: { color: '#64748b', fontSize: 11, formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    data: varMetrics.map((m) => m.label),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: '#475569', fontSize: 11 },
  },
  tooltip: { ...lightTooltip, trigger: 'axis', formatter: '{b}: {c}%' },
  series: [
    {
      type: 'bar',
      barWidth: 14,
      data: varMetrics.map((m) => m.value),
      itemStyle: {
        borderRadius: [7, 0, 0, 7],
        color: {
          type: 'linear',
          x: 0, y: 0, x2: 1, y2: 0,
          colorStops: [
            { offset: 0, color: 'rgba(220, 38, 38, 0.9)' },
            { offset: 1, color: 'rgba(220, 38, 38, 0.35)' },
          ],
        },
      },
      label: { show: true, position: 'right', color: '#dc2626', fontSize: 11, formatter: '{c}%' },
    },
  ],
}))

const feedStatusLabel: Record<string, string> = {
  completed: '已完成',
  running: '進行中',
  failed: '失敗',
}
</script>

<template>
  <div class="analyst-page">
    <div class="analyst-wrap">
      <!-- ============ 1. 頁首工具列 + KPI ============ -->
      <header class="analyst-header">
        <div class="analyst-header-main">
          <h1 class="analyst-title">
            投資總覽
            <span class="analyst-title-en">EQUITYLENS</span>
          </h1>
          <span class="analyst-date">{{ todayLabel }}</span>
        </div>
        <div class="analyst-header-actions">
          <button class="analyst-btn analyst-btn-solid" @click="router.push({ name: 'dashboard' })">
            進入儀表板
          </button>
          <button class="analyst-btn analyst-btn-outline" @click="router.push({ name: 'research' })">
            查看 AI 研究
          </button>
        </div>
      </header>

      <section class="kpi-grid">
        <div v-for="kpi in heroKpis" :key="kpi.label" class="analyst-card kpi-card">
          <span class="kpi-label">{{ kpi.label }}</span>
          <span class="kpi-value">
            {{ kpi.prefix }}{{ formatValue(kpi.value, kpi.decimals) }}{{ kpi.suffix }}
          </span>
          <span :class="['kpi-sub', kpi.tone]">{{ kpi.sub }}</span>
        </div>
      </section>

      <!-- ============ 2. 市場速覽 ============ -->
      <section class="analyst-section">
        <div class="section-head">
          <div class="section-titles">
            <span class="section-kicker">MARKET SNAPSHOT</span>
            <h2 class="section-title">市場速覽</h2>
          </div>
          <span class="section-note">台股即時行情(展示用)</span>
        </div>
        <div class="analyst-card market-card">
          <div class="market-scroll">
            <div v-for="item in tickerItems" :key="item.code" class="market-chip">
              <span class="chip-name">{{ item.code }} {{ item.name }}</span>
              <span class="chip-price">{{ item.price }}</span>
              <span :class="['chip-change', item.changePct >= 0 ? 'up' : 'down']">
                {{ item.changePct >= 0 ? '▲' : '▼' }} {{ Math.abs(item.changePct).toFixed(2) }}%
              </span>
            </div>
          </div>
        </div>
      </section>

      <!-- ============ 3. Portfolio Pulse ============ -->
      <section class="analyst-section">
        <div class="section-head">
          <div class="section-titles">
            <span class="section-kicker">PORTFOLIO PULSE</span>
            <h2 class="section-title">組合淨值走勢</h2>
          </div>
          <div class="seg">
            <button
              v-for="range in equityRanges"
              :key="range"
              :class="['seg-btn', activeRange === range && 'active']"
              @click="activeRange = range"
            >
              {{ range }}
            </button>
          </div>
        </div>
        <div class="pulse-grid">
          <div class="analyst-card chart-card">
            <TechChart :option="equityOption" height="320px" />
          </div>
          <div class="analyst-card chart-card">
            <span class="card-caption">資產配置</span>
            <TechChart :option="allocationOption" height="290px" />
          </div>
        </div>
      </section>

      <!-- ============ 4. Risk Matrix ============ -->
      <section class="analyst-section">
        <div class="section-head">
          <div class="section-titles">
            <span class="section-kicker">RISK MATRIX</span>
            <h2 class="section-title">風險矩陣</h2>
          </div>
          <span class="section-note">1 年歷史模擬</span>
        </div>
        <div class="risk-grid">
          <div class="analyst-card chart-card">
            <span class="card-caption">因子暴露</span>
            <TechChart :option="radarOption" height="280px" />
          </div>
          <div class="analyst-card chart-card">
            <span class="card-caption">Value at Risk(日)</span>
            <TechChart :option="varOption" height="280px" />
          </div>
          <div class="risk-side">
            <div class="analyst-card risk-stat">
              <span class="risk-label">Max Drawdown</span>
              <span class="risk-value down">-8.4%</span>
              <span class="risk-sub">2025.09 – 2025.11 區間</span>
            </div>
            <div class="analyst-card risk-stat">
              <span class="risk-label">Volatility(年化)</span>
              <span class="risk-value">14.2%</span>
              <span class="risk-sub">滾動 90 日</span>
            </div>
            <div class="analyst-card risk-stat">
              <span class="risk-label">Beta(vs 加權指數)</span>
              <span class="risk-value">1.08</span>
              <span class="risk-sub">滾動 252 日</span>
            </div>
          </div>
        </div>
      </section>

      <!-- ============ 5. AI Research Feed ============ -->
      <section class="analyst-section">
        <div class="section-head">
          <div class="section-titles">
            <span class="section-kicker">AI RESEARCH FEED</span>
            <h2 class="section-title">研究動態</h2>
          </div>
          <button class="analyst-btn analyst-btn-outline analyst-btn-sm" @click="router.push({ name: 'research' })">
            全部研究
          </button>
        </div>
        <div class="analyst-card feed-card">
          <div class="feed-row feed-head">
            <span>標題</span>
            <span>類型</span>
            <span>狀態</span>
            <span class="feed-head-time">時間</span>
          </div>
          <div v-for="(item, i) in aiFeed" :key="i" class="feed-row">
            <span class="feed-title">{{ item.title }}</span>
            <span class="feed-kind">{{ item.kind }}</span>
            <span class="feed-status">
              <span :class="['feed-badge', item.status]">{{ feedStatusLabel[item.status] }}</span>
            </span>
            <span class="feed-time">{{ item.time }}</span>
          </div>
        </div>
      </section>

      <!-- ============ 6. Footer ============ -->
      <footer class="analyst-footer">
        <span class="analyst-mono">EQUITYLENS © 2026</span>
        <span>本頁數據為展示用途,不構成投資建議</span>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.analyst-page {
  --bg: #eef2f7;
  --card: #ffffff;
  --line: #e2e8f0;
  --navy: #1e3a5f;
  --blue: #2563eb;
  --ink: #0f172a;
  --body: #475569;
  --muted: #64748b;
  --up: #059669;
  --down: #dc2626;

  min-height: calc(100vh - 60px);
  background: var(--bg);
  color: var(--body);
  font-size: 14px;
}

/* 全域 h1/h2 預設白色,覆寫為藏青 */
.analyst-page h1,
.analyst-page h2 {
  margin: 0;
  color: var(--navy);
}

.analyst-wrap {
  width: min(1240px, calc(100% - 48px));
  margin: 0 auto;
  padding: 28px 0 44px;
}

/* ---- 卡片基底 ---- */
.analyst-card {
  background: var(--card);
  border: 1px solid var(--line);
  border-radius: 10px;
  box-shadow: 0 1px 2px rgba(15, 23, 42, 0.05);
}

/* ---- 頁首工具列 ---- */
.analyst-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.analyst-title {
  display: flex;
  align-items: baseline;
  gap: 10px;
  font-size: 24px;
  font-weight: 700;
  letter-spacing: -0.01em;
}

.analyst-title-en {
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.16em;
  color: var(--blue);
}

.analyst-date {
  display: block;
  margin-top: 6px;
  font-size: 13px;
  color: var(--muted);
}

.analyst-header-actions {
  display: flex;
  gap: 10px;
}

.analyst-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 9px 18px;
  border: 1px solid transparent;
  border-radius: 8px;
  background: transparent;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
}

.analyst-btn-solid {
  background: var(--navy);
  border-color: var(--navy);
  color: #ffffff;
}

.analyst-btn-solid:hover {
  background: #16304f;
  border-color: #16304f;
}

.analyst-btn-outline {
  background: #ffffff;
  border-color: #cbd5e1;
  color: var(--navy);
}

.analyst-btn-outline:hover {
  border-color: var(--blue);
  color: var(--blue);
}

.analyst-btn-sm {
  padding: 6px 14px;
  font-size: 12px;
}

/* ---- KPI ---- */
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 14px;
  margin-top: 22px;
}

.kpi-card {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 16px 18px;
}

.kpi-label {
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  color: var(--muted);
}

.kpi-value {
  font-size: 26px;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--navy);
  font-variant-numeric: tabular-nums;
}

.kpi-sub {
  font-size: 12px;
  color: var(--muted);
  font-variant-numeric: tabular-nums;
}

.kpi-sub.positive {
  color: var(--up);
}

.kpi-sub.negative {
  color: var(--down);
}

/* ---- 區塊標題列 ---- */
.analyst-section {
  margin-top: 30px;
}

.section-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.section-kicker {
  display: block;
  margin-bottom: 2px;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.14em;
  color: var(--blue);
}

.section-title {
  font-size: 17px;
  font-weight: 700;
}

.section-note {
  font-size: 12px;
  color: var(--muted);
}

/* ---- 市場速覽 ---- */
.market-card {
  padding: 12px 16px;
}

.market-scroll {
  display: flex;
  gap: 10px;
  overflow-x: auto;
  padding-bottom: 2px;
}

.market-chip {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  background: #f8fafc;
  border: 1px solid var(--line);
  border-radius: 8px;
  font-size: 12px;
  white-space: nowrap;
}

.chip-name {
  font-weight: 600;
  color: var(--muted);
}

.chip-price {
  font-weight: 700;
  color: var(--ink);
  font-variant-numeric: tabular-nums;
}

.chip-change {
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.chip-change.up {
  color: var(--up);
}

.chip-change.down {
  color: var(--down);
}

/* ---- Segmented control ---- */
.seg {
  display: inline-flex;
  gap: 2px;
  padding: 3px;
  background: #e2e8f0;
  border-radius: 9px;
}

.seg-btn {
  padding: 5px 14px;
  border: none;
  border-radius: 7px;
  background: transparent;
  font-size: 12px;
  font-weight: 600;
  color: var(--muted);
  cursor: pointer;
  font-variant-numeric: tabular-nums;
  transition: background 0.15s ease, color 0.15s ease;
}

.seg-btn:hover {
  color: var(--navy);
}

.seg-btn.active {
  background: var(--navy);
  color: #ffffff;
}

/* ---- 圖表卡 ---- */
.pulse-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 14px;
}

.chart-card {
  min-width: 0;
  display: flex;
  flex-direction: column;
  padding: 16px 16px 8px;
}

.card-caption {
  margin-bottom: 6px;
  font-size: 12px;
  font-weight: 600;
  color: var(--muted);
}

/* ---- Risk ---- */
.risk-grid {
  display: grid;
  grid-template-columns: 1fr 1.2fr 0.8fr;
  gap: 14px;
}

.risk-side {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.risk-stat {
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 4px;
  padding: 14px 18px;
}

.risk-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--muted);
}

.risk-value {
  font-size: 24px;
  font-weight: 700;
  color: var(--navy);
  font-variant-numeric: tabular-nums;
}

.risk-value.down {
  color: var(--down);
}

.risk-sub {
  font-size: 11px;
  color: var(--muted);
}

/* ---- AI Research Feed ---- */
.feed-card {
  overflow: hidden;
}

.feed-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 110px 104px 90px;
  align-items: center;
  gap: 8px 14px;
  padding: 11px 18px;
  border-bottom: 1px solid var(--line);
  font-size: 13px;
}

.feed-row:last-child {
  border-bottom: none;
}

.feed-head {
  padding-top: 9px;
  padding-bottom: 9px;
  background: #f8fafc;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.08em;
  color: var(--muted);
}

.feed-head-time {
  text-align: right;
}

.feed-title {
  overflow: hidden;
  font-weight: 500;
  color: var(--ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-kind {
  font-size: 12px;
  color: var(--muted);
}

.feed-badge {
  display: inline-flex;
  align-items: center;
  padding: 3px 10px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
}

.feed-badge.completed {
  background: #ecfdf5;
  color: #047857;
}

.feed-badge.running {
  background: #eff6ff;
  color: #1d4ed8;
}

.feed-badge.failed {
  background: #fef2f2;
  color: #b91c1c;
}

.feed-time {
  font-size: 12px;
  color: var(--muted);
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

/* ---- Footer ---- */
.analyst-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 36px;
  padding-top: 18px;
  border-top: 1px solid #dbe3ec;
  font-size: 12px;
  color: var(--muted);
}

.analyst-mono {
  font-weight: 600;
  letter-spacing: 0.08em;
  font-variant-numeric: tabular-nums;
}

/* ---- Responsive ---- */
@media (max-width: 960px) {
  .kpi-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .pulse-grid,
  .risk-grid {
    grid-template-columns: 1fr;
  }

  .risk-side {
    flex-direction: row;
  }
}

@media (max-width: 640px) {
  .analyst-wrap {
    width: calc(100% - 32px);
    padding-top: 20px;
  }

  .analyst-header-actions {
    width: 100%;
  }

  .analyst-header-actions .analyst-btn {
    flex: 1;
  }

  .feed-head {
    display: none;
  }

  .feed-row {
    grid-template-columns: minmax(0, 1fr) auto;
    padding: 12px 14px;
  }

  .feed-title {
    grid-column: 1;
    grid-row: 1;
  }

  .feed-status {
    grid-column: 2;
    grid-row: 1;
    justify-self: end;
  }

  .feed-kind {
    grid-column: 1;
    grid-row: 2;
  }

  .feed-time {
    grid-column: 2;
    grid-row: 2;
  }
}

@media (max-width: 560px) {
  .kpi-grid {
    grid-template-columns: 1fr;
  }

  .risk-side {
    flex-direction: column;
  }
}
</style>
