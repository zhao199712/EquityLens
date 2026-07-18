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

// Prestige Banking 色票
const GOLD = '#c9a86a'
const IVORY = '#f5efe0'
const MUTED = '#9a917c'
const DOWN = '#b05c5c'
const ALLOC_COLORS = ['#c9a86a', '#a8905e', '#8a7348', '#3d5470', '#2a3d55', '#1d2c42']

const formatKpiValue = (value: number, decimals: number) =>
  value.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })

// ---- 行情跑馬燈(複製一份達成無縫循環)----
const tickerLoop = computed(() => [...tickerItems, ...tickerItems])

// ---- 淨值曲線 ----
const activeRange = ref<EquityRange>('6M')

const equityOption = computed(() => {
  const points = equityCurves[activeRange.value]
  return {
    textStyle: { color: MUTED },
    grid: { left: 64, right: 20, top: 24, bottom: 32 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date.slice(5)),
      axisLine: { lineStyle: { color: 'rgba(201,168,106,0.25)' } },
      axisTick: { show: false },
      axisLabel: { color: '#6e6757', fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: 'rgba(201,168,106,0.08)' } },
      axisLabel: {
        color: '#6e6757',
        fontSize: 11,
        formatter: (v: number) => `NT$${(v / 1_000_000).toFixed(1)}M`,
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

// ---- 配置 donut ----
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
    text: 'NT$12.58M',
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
      label: { color: MUTED, fontSize: 11, formatter: '{b} {c}%' },
      labelLine: { lineStyle: { color: 'rgba(201,168,106,0.3)' } },
      itemStyle: { borderColor: '#0b1220', borderWidth: 2 },
      emphasis: { scaleSize: 4 },
      data: allocation.map((slice, i) => ({
        ...slice,
        itemStyle: { color: ALLOC_COLORS[i % ALLOC_COLORS.length] },
      })),
    },
  ],
}))

// ---- 風險雷達 ----
const radarOption = computed(() => ({
  textStyle: { color: MUTED },
  tooltip: {
    backgroundColor: 'rgba(11, 18, 32, 0.95)',
    borderColor: 'rgba(201,168,106,0.4)',
    textStyle: { color: IVORY, fontSize: 12 },
  },
  radar: {
    indicator: riskRadar.indicators.map((name) => ({ name, max: 100 })),
    radius: '68%',
    axisName: { color: MUTED, fontSize: 11 },
    splitLine: { lineStyle: { color: 'rgba(201,168,106,0.15)' } },
    splitArea: { areaStyle: { color: ['rgba(201,168,106,0.02)', 'rgba(201,168,106,0.05)'] } },
    axisLine: { lineStyle: { color: 'rgba(201,168,106,0.2)' } },
  },
  series: [
    {
      type: 'radar',
      data: [
        {
          value: riskRadar.scores,
          name: '組合因子暴露',
          lineStyle: { color: GOLD, width: 2 },
          areaStyle: { color: 'rgba(201,168,106,0.15)' },
          itemStyle: { color: GOLD },
        },
      ],
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
    data: varMetrics.map((m) => m.label),
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
      data: varMetrics.map((m) => m.value),
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

        <div class="hero-kpis fade-in" style="--d: 0.6s">
          <div v-for="kpi in heroKpis" :key="kpi.label" class="kpi">
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
          <button
            v-for="range in equityRanges"
            :key="range"
            :class="['range-btn', activeRange === range && 'active']"
            @click="activeRange = range"
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
          <TechChart :option="allocationOption" height="330px" />
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
          <span class="panel-label">FACTOR EXPOSURE</span>
          <TechChart :option="radarOption" height="300px" />
        </div>
        <div class="panel panel-pad">
          <span class="panel-label">VALUE AT RISK(日,1 年歷史模擬)</span>
          <TechChart :option="varOption" height="300px" />
        </div>
        <div class="risk-side">
          <div class="panel risk-stat">
            <span class="kpi-label">Max Drawdown</span>
            <span class="kpi-value negative">-8.4%</span>
            <span class="kpi-sub">2025.09 – 2025.11 區間</span>
          </div>
          <div class="panel risk-stat">
            <span class="kpi-label">Volatility(年化)</span>
            <span class="kpi-value">14.2%</span>
            <span class="kpi-sub">滾動 90 日</span>
          </div>
          <div class="panel risk-stat">
            <span class="kpi-label">Beta(vs 加權指數)</span>
            <span class="kpi-value">1.08</span>
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
        <div v-for="(item, i) in aiFeed" :key="i" class="feed-item">
          <span :class="['feed-dot', item.status]" />
          <div class="feed-body">
            <span class="feed-title">{{ item.title }}</span>
            <span class="feed-meta">{{ item.time }}</span>
          </div>
          <span class="feed-kind">{{ item.kind }}</span>
          <span :class="['feed-status', item.status]">{{ feedStatusLabel[item.status] }}</span>
        </div>
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

.hero-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  width: 100%;
  margin-top: 44px;
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
