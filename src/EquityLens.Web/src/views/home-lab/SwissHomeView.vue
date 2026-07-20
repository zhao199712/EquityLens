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

// ---- 行情跑馬燈(內容兩份達成無縫循環)----
const tickerLoop = computed(() => [...tickerItems, ...tickerItems])

// ---- KPI 數字格式 ----
function formatKpiValue(value: number, decimals: number): string {
  return value.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })
}

// ---- 淨值曲線 ----
const activeRange = ref<EquityRange>('6M')

// TechChart 的 base option 為深色主題,以下每張圖都完整覆寫成淺色紙面配色
const swissTooltip = {
  backgroundColor: '#ffffff',
  borderColor: '#111111',
  borderWidth: 1,
  textStyle: { color: '#111111', fontSize: 12 },
}

const equityOption = computed(() => {
  const points = equityCurves[activeRange.value]
  const last = points[points.length - 1]
  return {
    textStyle: { color: '#111111' },
    grid: { left: 56, right: 20, top: 24, bottom: 32 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date.slice(5)),
      axisLine: { lineStyle: { color: '#111111' } },
      axisTick: { show: false },
      axisLabel: { color: '#6b665c', fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: '#ddd8cb' } },
      axisLabel: {
        color: '#6b665c',
        fontSize: 11,
        formatter: (v: number) => `NT$${(v / 1_000_000).toFixed(1)}M`,
      },
    },
    tooltip: { trigger: 'axis', ...swissTooltip },
    series: [
      {
        type: 'line',
        data: points.map((p) => p.value),
        smooth: false,
        showSymbol: false,
        lineStyle: { width: 2, color: '#111111' },
        markPoint: {
          symbol: 'circle',
          symbolSize: 7,
          itemStyle: { color: '#e30613' },
          label: { show: false },
          data: last ? [{ coord: [last.date.slice(5), last.value] }] : [],
        },
      },
    ],
  }
})

// ---- 配置 donut(黑 / 灰 / 紅階調)----
const swissAllocationColors = ['#111111', '#55534d', '#8a867c', '#c9c4b8', '#e30613', '#6e6a60']

const allocationOption = computed(() => ({
  textStyle: { color: '#111111' },
  tooltip: { trigger: 'item', formatter: '{b}: {c}%', ...swissTooltip },
  title: {
    text: 'NT$12.58M',
    subtext: '總資產 TOTAL ASSETS',
    left: 'center',
    top: '40%',
    textStyle: { color: '#111111', fontSize: 20, fontWeight: 700 },
    subtextStyle: { color: '#6b665c', fontSize: 10 },
  },
  series: [
    {
      type: 'pie',
      radius: ['58%', '80%'],
      center: ['50%', '50%'],
      avoidLabelOverlap: true,
      label: { color: '#111111', fontSize: 11, formatter: '{b} {c}%' },
      labelLine: { lineStyle: { color: '#8a867c' } },
      itemStyle: { borderColor: '#f5f2ec', borderWidth: 2 },
      data: allocation.map((slice, i) => ({
        ...slice,
        itemStyle: { color: swissAllocationColors[i % swissAllocationColors.length] },
      })),
    },
  ],
}))

// ---- 風險雷達 ----
const radarOption = computed(() => ({
  textStyle: { color: '#111111' },
  tooltip: { ...swissTooltip },
  radar: {
    indicator: riskRadar.indicators.map((name) => ({ name, max: 100 })),
    radius: '68%',
    axisName: { color: '#6b665c', fontSize: 11 },
    splitLine: { lineStyle: { color: 'rgba(17,17,17,0.15)' } },
    splitArea: { areaStyle: { color: ['rgba(17,17,17,0.02)', 'rgba(17,17,17,0.05)'] } },
    axisLine: { lineStyle: { color: 'rgba(17,17,17,0.25)' } },
  },
  series: [
    {
      type: 'radar',
      data: [
        {
          value: riskRadar.scores,
          name: '組合因子暴露',
          lineStyle: { color: '#111111', width: 2 },
          areaStyle: { color: 'rgba(17,17,17,0.08)' },
          itemStyle: { color: '#e30613' },
        },
      ],
    },
  ],
}))

// ---- VaR 水平 bar(負值,紅色)----
const varOption = computed(() => ({
  textStyle: { color: '#111111' },
  grid: { left: 80, right: 40, top: 10, bottom: 10 },
  xAxis: {
    type: 'value',
    max: 0,
    splitLine: { lineStyle: { color: '#ddd8cb' } },
    axisLabel: { color: '#6b665c', fontSize: 11, formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    data: varMetrics.map((m) => m.label),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: '#6b665c', fontSize: 11 },
  },
  tooltip: { trigger: 'axis', formatter: '{b}: {c}%', ...swissTooltip },
  series: [
    {
      type: 'bar',
      barWidth: 14,
      data: varMetrics.map((m) => m.value),
      itemStyle: { color: '#e30613' },
      label: { show: true, position: 'left', color: '#e30613', fontSize: 11, formatter: '{c}%' },
    },
  ],
}))

const feedStatusLabel: Record<string, string> = {
  completed: 'COMPLETED',
  running: 'RUNNING',
  failed: 'FAILED',
}
</script>

<template>
  <div class="swiss-page">
    <!-- ============ Hero ============ -->
    <section class="swiss-hero">
      <div class="swiss-hero-inner">
        <p class="swiss-eyebrow swiss-rise">
          <span class="swiss-red-square" />AI-ASSISTED INVESTMENT ANALYTICS
        </p>
        <h1 class="swiss-hero-title swiss-rise" style="animation-delay: 0.08s">EQUITYLENS</h1>
        <div class="swiss-hero-rule swiss-rise" style="animation-delay: 0.16s" />
        <div class="swiss-hero-row swiss-rise" style="animation-delay: 0.22s">
          <p class="swiss-hero-sub">
            整合投資組合帳務、量化風險與 AI 研究工作流,<br />讓每一個投資決策都有證據可循
          </p>
          <div class="swiss-hero-cta">
            <button class="swiss-btn swiss-btn-solid" @click="router.push({ name: 'dashboard' })">
              進入儀表板
            </button>
            <button class="swiss-btn swiss-btn-text" @click="router.push({ name: 'research' })">
              查看 AI 研究
            </button>
          </div>
        </div>

        <div class="swiss-kpis swiss-rise" style="animation-delay: 0.3s">
          <div v-for="kpi in heroKpis" :key="kpi.label" class="swiss-kpi">
            <span class="swiss-kpi-label">{{ kpi.label }}</span>
            <span :class="['swiss-kpi-value', kpi.tone === 'negative' && 'neg']">
              {{ kpi.prefix }}{{ formatKpiValue(kpi.value, kpi.decimals) }}{{ kpi.suffix }}
            </span>
            <span class="swiss-kpi-sub">{{ kpi.sub }}</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ 行情跑馬燈 ============ -->
    <div class="swiss-ticker">
      <div class="swiss-ticker-track">
        <span v-for="(item, i) in tickerLoop" :key="i" class="swiss-ticker-item">
          <span class="swiss-ticker-code">{{ item.code }} {{ item.name }}</span>
          <span class="swiss-ticker-price">{{ item.price }}</span>
          <span :class="['swiss-ticker-change', item.changePct >= 0 ? 'up' : 'down']">
            {{ item.changePct >= 0 ? '▲' : '▼' }} {{ Math.abs(item.changePct).toFixed(2) }}%
          </span>
        </span>
      </div>
    </div>

    <!-- ============ 01 Portfolio Pulse ============ -->
    <section class="swiss-section">
      <div class="swiss-sec-head">
        <span class="swiss-sec-no">01</span>
        <span class="swiss-sec-label">PORTFOLIO</span>
        <h2 class="swiss-sec-title">組合淨值走勢</h2>
        <div class="swiss-range-switch">
          <button
            v-for="range in equityRanges"
            :key="range"
            :class="['swiss-range-btn', activeRange === range && 'active']"
            @click="activeRange = range"
          >
            {{ range }}
          </button>
        </div>
      </div>

      <div class="swiss-pulse-grid">
        <div class="swiss-cell">
          <TechChart :option="equityOption" height="360px" />
        </div>
        <div class="swiss-cell swiss-cell-border">
          <span class="swiss-cell-label">ASSET ALLOCATION / 資產配置</span>
          <TechChart :option="allocationOption" height="330px" />
        </div>
      </div>
    </section>

    <!-- ============ 02 Risk Matrix ============ -->
    <section class="swiss-section">
      <div class="swiss-sec-head">
        <span class="swiss-sec-no">02</span>
        <span class="swiss-sec-label">RISK MATRIX</span>
        <h2 class="swiss-sec-title">風險矩陣</h2>
      </div>

      <div class="swiss-risk-grid">
        <div class="swiss-cell">
          <span class="swiss-cell-label">FACTOR EXPOSURE / 因子暴露</span>
          <TechChart :option="radarOption" height="300px" />
        </div>
        <div class="swiss-cell swiss-cell-border">
          <span class="swiss-cell-label">VALUE AT RISK(日,1 年歷史模擬)</span>
          <TechChart :option="varOption" height="300px" />
        </div>
        <div class="swiss-cell swiss-cell-border swiss-risk-side">
          <div class="swiss-risk-num">
            <span class="swiss-risk-num-label">MAX DRAWDOWN</span>
            <span class="swiss-risk-num-value neg">-8.4%</span>
            <span class="swiss-risk-num-sub">2025.09 – 2025.11 區間</span>
          </div>
          <div class="swiss-risk-num">
            <span class="swiss-risk-num-label">VOLATILITY(年化)</span>
            <span class="swiss-risk-num-value">14.2%</span>
            <span class="swiss-risk-num-sub">滾動 90 日</span>
          </div>
          <div class="swiss-risk-num">
            <span class="swiss-risk-num-label">BETA(vs 加權指數)</span>
            <span class="swiss-risk-num-value">1.08</span>
            <span class="swiss-risk-num-sub">滾動 252 日</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ 03 AI Research Feed ============ -->
    <section class="swiss-section">
      <div class="swiss-sec-head">
        <span class="swiss-sec-no">03</span>
        <span class="swiss-sec-label">AI RESEARCH FEED</span>
        <h2 class="swiss-sec-title">AI 研究動態</h2>
      </div>

      <div class="swiss-feed">
        <div v-for="(item, i) in aiFeed" :key="i" class="swiss-feed-row">
          <span class="swiss-feed-index">{{ String(i + 1).padStart(2, '0') }}</span>
          <span class="swiss-feed-title">{{ item.title }}</span>
          <span class="swiss-feed-kind">{{ item.kind }}</span>
          <span :class="['swiss-feed-status', item.status]">{{ feedStatusLabel[item.status] }}</span>
          <span class="swiss-feed-time">{{ item.time }}</span>
        </div>
      </div>
    </section>

    <!-- ============ Footer ============ -->
    <footer class="swiss-footer">
      <span class="swiss-footer-brand">EQUITYLENS © 2026</span>
      <span>本頁數據為展示用途,不構成投資建議</span>
    </footer>
  </div>
</template>

<style scoped>
.swiss-page {
  --swiss-paper: #f5f2ec;
  --swiss-ink: #111111;
  --swiss-muted: #6b665c;
  --swiss-red: #e30613;
  --swiss-grid: #ddd8cb;
  --swiss-mono: 'JetBrains Mono', 'SFMono-Regular', Consolas, 'Liberation Mono', monospace;

  background: var(--swiss-paper);
  color: var(--swiss-ink);
  font-family: 'Helvetica Neue', Helvetica, Arial, 'Noto Sans TC', sans-serif;
  min-height: 100vh;
}

/* 全域 h1/h2 預設白字,本頁一律覆寫為黑 */
.swiss-page h1,
.swiss-page h2 {
  color: var(--swiss-ink);
}

/* ---- 進場動效(極克制:fade + translateY)---- */
@keyframes swiss-in {
  from {
    opacity: 0;
    transform: translateY(14px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.swiss-rise {
  animation: swiss-in 0.6s ease both;
}

.swiss-section {
  animation: swiss-in 0.6s ease both;
}

/* ---- Hero ---- */
.swiss-hero {
  min-height: calc(100vh - 60px);
  display: flex;
  align-items: center;
  border-bottom: 3px solid var(--swiss-ink);
}

.swiss-hero-inner {
  width: 100%;
  padding: 48px;
}

.swiss-eyebrow {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 0 0 20px;
  font-size: 12px;
  letter-spacing: 0.28em;
  color: var(--swiss-muted);
}

.swiss-red-square {
  width: 10px;
  height: 10px;
  background: var(--swiss-red);
  flex: none;
}

.swiss-hero-title {
  margin: 0;
  font-size: clamp(60px, 12vw, 160px);
  font-weight: 800;
  letter-spacing: -0.03em;
  line-height: 0.95;
  white-space: nowrap;
}

.swiss-hero-rule {
  height: 1px;
  background: var(--swiss-ink);
  margin: 28px 0 24px;
}

.swiss-hero-row {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 32px;
  flex-wrap: wrap;
}

.swiss-hero-sub {
  margin: 0;
  font-size: 15px;
  line-height: 1.9;
  color: var(--swiss-muted);
  max-width: 520px;
}

.swiss-hero-cta {
  display: flex;
  gap: 16px;
  align-items: center;
}

.swiss-btn {
  border-radius: 0;
  box-shadow: none;
  cursor: pointer;
  font-family: inherit;
  font-size: 14px;
  letter-spacing: 0.08em;
  padding: 14px 28px;
  transition: background 0.15s ease, color 0.15s ease;
}

.swiss-btn-solid {
  background: var(--swiss-ink);
  color: var(--swiss-paper);
  border: 1px solid var(--swiss-ink);
}

.swiss-btn-solid:hover {
  background: var(--swiss-red);
  border-color: var(--swiss-red);
}

.swiss-btn-text {
  background: transparent;
  color: var(--swiss-red);
  border: none;
  border-bottom: 1px solid var(--swiss-red);
  padding: 14px 4px;
}

.swiss-btn-text:hover {
  color: var(--swiss-ink);
  border-bottom-color: var(--swiss-ink);
}

/* ---- Hero KPI(hairline 格線分隔,非卡片)---- */
.swiss-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  margin-top: 56px;
  border-top: 1px solid var(--swiss-ink);
}

.swiss-kpi {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 20px 24px 0 24px;
  border-left: 1px solid var(--swiss-ink);
}

.swiss-kpi:first-child {
  border-left: none;
  padding-left: 0;
}

.swiss-kpi-label {
  font-size: 11px;
  letter-spacing: 0.2em;
  text-transform: uppercase;
  color: var(--swiss-muted);
}

.swiss-kpi-value {
  font-family: var(--swiss-mono);
  font-variant-numeric: tabular-nums;
  font-size: clamp(22px, 2.4vw, 32px);
  font-weight: 700;
  color: var(--swiss-ink);
}

.swiss-kpi-value.neg {
  color: var(--swiss-red);
}

.swiss-kpi-sub {
  font-size: 12px;
  color: var(--swiss-muted);
}

/* ---- 行情跑馬燈 ---- */
.swiss-ticker {
  overflow: hidden;
  border-top: 1px solid var(--swiss-ink);
  border-bottom: 1px solid var(--swiss-ink);
}

.swiss-ticker-track {
  display: flex;
  width: max-content;
  animation: swiss-marquee 36s linear infinite;
}

.swiss-ticker:hover .swiss-ticker-track {
  animation-play-state: paused;
}

@keyframes swiss-marquee {
  from {
    transform: translateX(0);
  }
  to {
    transform: translateX(-50%);
  }
}

.swiss-ticker-item {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 12px 28px;
  font-family: var(--swiss-mono);
  font-variant-numeric: tabular-nums;
  font-size: 12px;
  white-space: nowrap;
  border-right: 1px solid var(--swiss-ink);
}

.swiss-ticker-code {
  color: var(--swiss-muted);
  letter-spacing: 0.06em;
}

.swiss-ticker-price {
  font-weight: 700;
}

.swiss-ticker-change.up {
  color: var(--swiss-ink);
}

.swiss-ticker-change.down {
  color: var(--swiss-red);
}

/* ---- 區塊共通 ---- */
.swiss-section {
  padding: 56px 48px 0;
}

.swiss-sec-head {
  display: flex;
  align-items: baseline;
  gap: 16px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--swiss-ink);
  margin-bottom: 32px;
  flex-wrap: wrap;
}

.swiss-sec-no {
  font-family: var(--swiss-mono);
  font-size: 14px;
  font-weight: 700;
  color: var(--swiss-red);
}

.swiss-sec-no::after {
  content: '—';
  margin-left: 16px;
  color: var(--swiss-ink);
}

.swiss-sec-label {
  font-size: 12px;
  letter-spacing: 0.24em;
  color: var(--swiss-muted);
}

.swiss-sec-title {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 0.02em;
  flex: 1;
}

.swiss-cell-label {
  display: block;
  font-size: 11px;
  letter-spacing: 0.2em;
  color: var(--swiss-muted);
  margin-bottom: 12px;
  text-transform: uppercase;
}

/* ---- 01 Portfolio Pulse ---- */
.swiss-range-switch {
  display: flex;
}

.swiss-range-btn {
  border: 1px solid var(--swiss-ink);
  border-left: none;
  border-radius: 0;
  background: transparent;
  color: var(--swiss-ink);
  font-family: var(--swiss-mono);
  font-size: 12px;
  padding: 6px 14px;
  cursor: pointer;
  transition: background 0.15s ease, color 0.15s ease;
}

.swiss-range-btn:first-child {
  border-left: 1px solid var(--swiss-ink);
}

.swiss-range-btn:hover {
  color: var(--swiss-red);
}

.swiss-range-btn.active {
  background: var(--swiss-ink);
  color: var(--swiss-paper);
}

.swiss-pulse-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
}

.swiss-cell {
  min-width: 0;
}

.swiss-cell-border {
  border-left: 1px solid var(--swiss-ink);
  padding-left: 32px;
}

.swiss-pulse-grid .swiss-cell:first-child {
  padding-right: 32px;
}

/* ---- 02 Risk Matrix ---- */
.swiss-risk-grid {
  display: grid;
  grid-template-columns: 1fr 1.2fr 0.8fr;
}

.swiss-risk-side {
  display: flex;
  flex-direction: column;
}

.swiss-risk-num {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 16px 0;
  border-bottom: 1px solid var(--swiss-ink);
}

.swiss-risk-num:first-child {
  padding-top: 0;
}

.swiss-risk-num:last-child {
  border-bottom: none;
  flex: 1;
}

.swiss-risk-num-label {
  font-size: 11px;
  letter-spacing: 0.18em;
  color: var(--swiss-muted);
}

.swiss-risk-num-value {
  font-family: var(--swiss-mono);
  font-variant-numeric: tabular-nums;
  font-size: 30px;
  font-weight: 700;
}

.swiss-risk-num-value.neg {
  color: var(--swiss-red);
}

.swiss-risk-num-sub {
  font-size: 12px;
  color: var(--swiss-muted);
}

/* ---- 03 AI Research Feed ---- */
.swiss-feed {
  border-top: 1px solid var(--swiss-ink);
}

.swiss-feed-row {
  display: grid;
  grid-template-columns: 48px 1fr 110px 110px 90px;
  align-items: center;
  gap: 16px;
  padding: 16px 0;
  border-bottom: 1px solid var(--swiss-ink);
}

.swiss-feed-index {
  font-family: var(--swiss-mono);
  font-size: 12px;
  color: var(--swiss-muted);
}

.swiss-feed-title {
  font-size: 14px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.swiss-feed-kind {
  font-size: 10px;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--swiss-muted);
}

.swiss-feed-status {
  font-family: var(--swiss-mono);
  font-size: 11px;
  letter-spacing: 0.12em;
}

.swiss-feed-status.completed {
  color: var(--swiss-ink);
}

.swiss-feed-status.running {
  color: var(--swiss-red);
}

.swiss-feed-status.failed {
  color: #8a867c;
}

.swiss-feed-time {
  font-family: var(--swiss-mono);
  font-size: 11px;
  color: var(--swiss-muted);
  text-align: right;
}

/* ---- Footer ---- */
.swiss-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-top: 64px;
  padding: 20px 48px 28px;
  border-top: 3px solid var(--swiss-ink);
  color: var(--swiss-muted);
  font-size: 12px;
}

.swiss-footer-brand {
  font-weight: 800;
  letter-spacing: 0.16em;
  color: var(--swiss-ink);
}

/* ---- Responsive ---- */
@media (max-width: 960px) {
  .swiss-hero-inner,
  .swiss-section {
    padding-left: 24px;
    padding-right: 24px;
  }

  .swiss-kpis {
    grid-template-columns: repeat(2, 1fr);
    row-gap: 24px;
  }

  .swiss-kpi:nth-child(3) {
    border-left: none;
    padding-left: 0;
  }

  .swiss-pulse-grid,
  .swiss-risk-grid {
    grid-template-columns: 1fr;
  }

  .swiss-cell-border {
    border-left: none;
    padding-left: 0;
    border-top: 1px solid var(--swiss-ink);
    padding-top: 32px;
    margin-top: 32px;
  }

  .swiss-pulse-grid .swiss-cell:first-child {
    padding-right: 0;
  }

  .swiss-feed-row {
    grid-template-columns: 36px 1fr 90px;
  }

  .swiss-feed-kind,
  .swiss-feed-time {
    display: none;
  }

  .swiss-footer {
    padding-left: 24px;
    padding-right: 24px;
  }
}

@media (max-width: 560px) {
  .swiss-hero-inner {
    padding: 32px 20px;
  }

  .swiss-hero-title {
    font-size: clamp(44px, 13vw, 80px);
  }

  .swiss-hero-cta {
    width: 100%;
    flex-direction: column;
    align-items: stretch;
  }

  .swiss-btn-text {
    text-align: center;
    border: 1px solid var(--swiss-red);
    padding: 14px 28px;
  }

  .swiss-kpis {
    grid-template-columns: 1fr;
  }

  .swiss-kpi {
    border-left: none;
    padding-left: 0;
    border-top: 1px solid var(--swiss-grid);
    padding-top: 16px;
  }

  .swiss-kpi:first-child {
    border-top: none;
    padding-top: 20px;
  }

  .swiss-section {
    padding: 40px 20px 0;
  }

  .swiss-sec-head {
    gap: 10px;
  }

  .swiss-feed-row {
    grid-template-columns: 28px 1fr 80px;
    gap: 10px;
  }

  .swiss-footer {
    flex-direction: column;
    align-items: flex-start;
    gap: 6px;
    padding: 16px 20px 24px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .swiss-ticker-track {
    animation: none;
  }

  .swiss-rise,
  .swiss-section {
    animation: none;
  }
}
</style>
