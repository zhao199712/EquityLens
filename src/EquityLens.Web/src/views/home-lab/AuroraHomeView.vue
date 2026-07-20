<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import gsap from 'gsap'
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
  type HeroKpi,
} from '../../data/homeTechData'

const router = useRouter()

// ---- Aurora 專用圖表配色(紫 / 青 / 粉,線條柔和不加強烈 glow)----
const auroraPalette = ['#a78bfa', '#67e8f9', '#f0abfc', '#818cf8', '#5eead4', '#c7d2fe']
const axisLabelColor = '#a6a8d0'
const axisLineSoft = 'rgba(241, 242, 255, 0.16)'
const splitLineSoft = 'rgba(241, 242, 255, 0.08)'
const tooltipSkin = {
  backgroundColor: 'rgba(20, 21, 58, 0.94)',
  borderColor: 'rgba(167, 139, 250, 0.4)',
  textStyle: { color: '#f1f2ff', fontSize: 12 },
}

// ---- KPI 數值格式化 ----
const formatKpiValue = (kpi: HeroKpi) =>
  kpi.value.toLocaleString('en-US', {
    minimumFractionDigits: kpi.decimals,
    maximumFractionDigits: kpi.decimals,
  })

// ---- Hero 進場 + 區塊滾動淡入 ----
const pageRef = ref<HTMLElement>()
const heroEyebrowRef = ref<HTMLElement>()
const heroTitleRef = ref<HTMLElement>()
const heroSubRef = ref<HTMLElement>()
const heroCtaRef = ref<HTMLElement>()
const heroKpiRef = ref<HTMLElement>()

let revealObserver: IntersectionObserver | null = null

onMounted(() => {
  const heroEls = [
    heroEyebrowRef.value,
    heroTitleRef.value,
    heroSubRef.value,
    heroCtaRef.value,
    heroKpiRef.value,
  ].filter((el): el is HTMLElement => !!el)
  if (heroEls.length) {
    gsap.fromTo(
      heroEls,
      { opacity: 0, y: 26, filter: 'blur(10px)' },
      { opacity: 1, y: 0, filter: 'blur(0px)', duration: 1.05, ease: 'power2.out', stagger: 0.14 }
    )
  }

  revealObserver = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible')
          revealObserver?.unobserve(entry.target)
        }
      }
    },
    { threshold: 0.1 }
  )
  pageRef.value?.querySelectorAll('.aurora-reveal').forEach((el) => revealObserver?.observe(el))
})

onBeforeUnmount(() => {
  revealObserver?.disconnect()
})

// ---- 行情跑馬燈(複製一份達成無縫循環)----
const tickerLoop = computed(() => [...tickerItems, ...tickerItems])

// ---- 淨值曲線 ----
const activeRange = ref<EquityRange>('6M')

const equityOption = computed(() => {
  const points = equityCurves[activeRange.value]
  return {
    grid: { left: 56, right: 20, top: 24, bottom: 32 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date.slice(5)),
      axisLine: { lineStyle: { color: axisLineSoft } },
      axisTick: { show: false },
      axisLabel: { color: axisLabelColor, fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: splitLineSoft } },
      axisLabel: {
        color: axisLabelColor,
        fontSize: 11,
        formatter: (v: number) => `NT$${(v / 1_000_000).toFixed(1)}M`,
      },
    },
    tooltip: { trigger: 'axis', ...tooltipSkin },
    series: [
      {
        type: 'line',
        data: points.map((p) => p.value),
        smooth: true,
        showSymbol: false,
        lineStyle: { width: 2.5, color: '#a78bfa' },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0, y: 0, x2: 0, y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(167, 139, 250, 0.32)' },
              { offset: 1, color: 'rgba(167, 139, 250, 0)' },
            ],
          },
        },
      },
    ],
  }
})

// ---- 配置 donut ----
const allocationOption = computed(() => ({
  tooltip: { trigger: 'item', formatter: '{b}: {c}%', ...tooltipSkin },
  title: {
    text: 'NT$12.58M',
    subtext: '總資產',
    left: 'center',
    top: '42%',
    textStyle: { color: '#f1f2ff', fontSize: 20, fontWeight: 600 },
    subtextStyle: { color: '#a6a8d0', fontSize: 11 },
  },
  series: [
    {
      type: 'pie',
      radius: ['58%', '80%'],
      center: ['50%', '50%'],
      avoidLabelOverlap: true,
      label: { color: '#a6a8d0', fontSize: 11, formatter: '{b} {c}%' },
      labelLine: { lineStyle: { color: 'rgba(241, 242, 255, 0.28)' } },
      itemStyle: { borderColor: 'rgba(14, 15, 46, 0.9)', borderWidth: 2 },
      emphasis: { scaleSize: 6 },
      data: allocation.map((slice, i) => ({
        ...slice,
        itemStyle: { color: auroraPalette[i % auroraPalette.length] },
      })),
    },
  ],
}))

// ---- 風險雷達 ----
const radarOption = computed(() => ({
  tooltip: { ...tooltipSkin },
  radar: {
    indicator: riskRadar.indicators.map((name) => ({ name, max: 100 })),
    radius: '68%',
    axisName: { color: '#a6a8d0', fontSize: 11 },
    splitLine: { lineStyle: { color: 'rgba(241, 242, 255, 0.12)' } },
    splitArea: { areaStyle: { color: ['rgba(255, 255, 255, 0.02)', 'rgba(255, 255, 255, 0.05)'] } },
    axisLine: { lineStyle: { color: axisLineSoft } },
  },
  series: [
    {
      type: 'radar',
      data: [
        {
          value: riskRadar.scores,
          name: '組合因子暴露',
          lineStyle: { color: '#67e8f9', width: 2 },
          areaStyle: { color: 'rgba(103, 232, 249, 0.16)' },
          itemStyle: { color: '#67e8f9' },
        },
      ],
    },
  ],
}))

// ---- VaR 水平 bar ----
const varOption = computed(() => ({
  grid: { left: 80, right: 40, top: 10, bottom: 10 },
  xAxis: {
    type: 'value',
    max: 0,
    splitLine: { lineStyle: { color: splitLineSoft } },
    axisLabel: { color: axisLabelColor, fontSize: 11, formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    data: varMetrics.map((m) => m.label),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: axisLabelColor, fontSize: 11 },
  },
  tooltip: { trigger: 'axis', formatter: '{b}: {c}%', ...tooltipSkin },
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
            { offset: 0, color: 'rgba(240, 171, 252, 0.85)' },
            { offset: 1, color: 'rgba(240, 171, 252, 0.2)' },
          ],
        },
      },
      label: { show: true, position: 'right', color: '#f0abfc', fontSize: 11, formatter: '{c}%' },
    },
  ],
}))

const feedStatusLabel: Record<string, string> = {
  completed: '已完成',
  running: '執行中',
  failed: '失敗',
}
</script>

<template>
  <div ref="pageRef" class="aurora-page">
    <!-- 極光光球背景 -->
    <div class="aurora-orb orb-1" />
    <div class="aurora-orb orb-2" />
    <div class="aurora-orb orb-3" />
    <div class="aurora-orb orb-4" />

    <!-- ============ Hero ============ -->
    <section class="aurora-hero">
      <div class="hero-content">
        <span ref="heroEyebrowRef" class="hero-eyebrow">AI-ASSISTED INVESTMENT ANALYTICS</span>
        <h1 ref="heroTitleRef" class="hero-title">EQUITYLENS</h1>
        <p ref="heroSubRef" class="hero-sub">
          整合投資組合帳務、量化風險與 AI 研究工作流<br />
          讓每一個投資決策都有證據可循
        </p>
        <div ref="heroCtaRef" class="hero-cta">
          <button class="aurora-btn aurora-btn-primary" @click="router.push({ name: 'dashboard' })">
            進入儀表板
          </button>
          <button class="aurora-btn aurora-btn-ghost" @click="router.push({ name: 'research' })">
            查看 AI 研究
          </button>
        </div>

        <div ref="heroKpiRef" class="hero-kpis">
          <div v-for="kpi in heroKpis" :key="kpi.label" class="aurora-glass kpi-card">
            <span class="kpi-label">{{ kpi.label }}</span>
            <span :class="['kpi-value', `tone-${kpi.tone}`]">
              {{ kpi.prefix }}{{ formatKpiValue(kpi) }}{{ kpi.suffix }}
            </span>
            <span class="kpi-sub">{{ kpi.sub }}</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ 行情跑馬燈 ============ -->
    <div class="aurora-ticker">
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
    <section class="aurora-section aurora-reveal">
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
        <div class="aurora-glass panel-pad">
          <TechChart :option="equityOption" height="340px" />
        </div>
        <div class="aurora-glass panel-pad">
          <span class="panel-label">ASSET ALLOCATION</span>
          <TechChart :option="allocationOption" height="330px" />
        </div>
      </div>
    </section>

    <!-- ============ Risk Matrix ============ -->
    <section class="aurora-section aurora-reveal">
      <div class="section-head">
        <div>
          <span class="section-label">RISK MATRIX</span>
          <h2 class="section-title">風險矩陣</h2>
        </div>
      </div>

      <div class="risk-grid">
        <div class="aurora-glass panel-pad">
          <span class="panel-label">FACTOR EXPOSURE</span>
          <TechChart :option="radarOption" height="300px" />
        </div>
        <div class="aurora-glass panel-pad">
          <span class="panel-label">VALUE AT RISK(日,1 年歷史模擬)</span>
          <TechChart :option="varOption" height="300px" />
        </div>
        <div class="risk-side">
          <div class="aurora-glass risk-stat">
            <span class="kpi-label">Max Drawdown</span>
            <span class="kpi-value tone-negative">-8.4%</span>
            <span class="kpi-sub">2025.09 – 2025.11 區間</span>
          </div>
          <div class="aurora-glass risk-stat">
            <span class="kpi-label">Volatility(年化)</span>
            <span class="kpi-value">14.2%</span>
            <span class="kpi-sub">滾動 90 日</span>
          </div>
          <div class="aurora-glass risk-stat">
            <span class="kpi-label">Beta(vs 加權指數)</span>
            <span class="kpi-value">1.08</span>
            <span class="kpi-sub">滾動 252 日</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ AI Research Feed ============ -->
    <section class="aurora-section aurora-reveal">
      <div class="section-head">
        <div>
          <span class="section-label">AI RESEARCH FEED</span>
          <h2 class="section-title">AI 研究動態</h2>
        </div>
      </div>

      <div class="aurora-glass feed-panel">
        <div v-for="(item, i) in aiFeed" :key="i" class="feed-item">
          <span class="feed-dot" :data-status="item.status" />
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
    <footer class="aurora-footer">
      <span>EQUITYLENS © 2026</span>
      <span>本頁數據為展示用途,不構成投資建議</span>
    </footer>
  </div>
</template>

<style scoped>
/* ---- 頁面基底與 CSS vars ---- */
.aurora-page {
  --aurora-text: #f1f2ff;
  --aurora-muted: #a6a8d0;
  --aurora-violet: #a78bfa;
  --aurora-cyan: #67e8f9;
  --aurora-pink: #f0abfc;
  --aurora-green: #6ee7b7;
  --aurora-rose: #fda4af;
  --aurora-glass-bg: rgba(255, 255, 255, 0.06);
  --aurora-glass-border: rgba(255, 255, 255, 0.12);
  position: relative;
  min-height: 100vh;
  overflow-x: hidden;
  background: linear-gradient(165deg, #16174a 0%, #12133c 48%, #0e0f2e 100%);
  color: var(--aurora-text);
}

/* ---- 極光光球 ---- */
.aurora-orb {
  position: fixed;
  border-radius: 50%;
  filter: blur(90px);
  pointer-events: none;
  z-index: 0;
  will-change: transform;
}

.orb-1 {
  width: 560px;
  height: 560px;
  top: -14%;
  left: -12%;
  background: radial-gradient(circle at 40% 40%, rgba(139, 92, 246, 0.5), rgba(139, 92, 246, 0) 68%);
  animation: orb-drift-1 26s ease-in-out infinite alternate;
}

.orb-2 {
  width: 480px;
  height: 480px;
  top: 8%;
  right: -14%;
  background: radial-gradient(circle at 60% 40%, rgba(34, 211, 238, 0.38), rgba(34, 211, 238, 0) 68%);
  animation: orb-drift-2 32s ease-in-out infinite alternate;
}

.orb-3 {
  width: 620px;
  height: 620px;
  bottom: -24%;
  left: 12%;
  background: radial-gradient(circle at 50% 50%, rgba(240, 171, 252, 0.32), rgba(240, 171, 252, 0) 70%);
  animation: orb-drift-3 38s ease-in-out infinite alternate;
}

.orb-4 {
  width: 420px;
  height: 420px;
  top: 44%;
  left: 54%;
  background: radial-gradient(circle at 50% 50%, rgba(129, 140, 248, 0.36), rgba(129, 140, 248, 0) 70%);
  animation: orb-drift-1 30s ease-in-out infinite alternate-reverse;
}

@keyframes orb-drift-1 {
  from { transform: translate3d(0, 0, 0) scale(1); }
  to { transform: translate3d(70px, 50px, 0) scale(1.12); }
}

@keyframes orb-drift-2 {
  from { transform: translate3d(0, 0, 0) scale(1.05); }
  to { transform: translate3d(-60px, 70px, 0) scale(0.95); }
}

@keyframes orb-drift-3 {
  from { transform: translate3d(0, 0, 0) scale(1); }
  to { transform: translate3d(50px, -60px, 0) scale(1.08); }
}

/* ---- 毛玻璃卡片 ---- */
.aurora-glass {
  background: var(--aurora-glass-bg);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border: 1px solid var(--aurora-glass-border);
  border-radius: 22px;
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.14),
    0 18px 44px rgba(6, 7, 26, 0.38);
  transition: transform 0.35s ease, box-shadow 0.35s ease, border-color 0.35s ease;
}

.aurora-glass:hover {
  transform: translateY(-4px);
  border-color: rgba(255, 255, 255, 0.2);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.18),
    0 26px 54px rgba(6, 7, 26, 0.5);
}

/* ---- 膠囊按鈕 ---- */
.aurora-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 13px 30px;
  border-radius: 999px;
  border: 1px solid transparent;
  font-size: 14px;
  font-weight: 600;
  letter-spacing: 0.06em;
  cursor: pointer;
  transition: transform 0.25s ease, box-shadow 0.25s ease, background 0.25s ease, border-color 0.25s ease;
}

.aurora-btn-primary {
  background: linear-gradient(120deg, var(--aurora-violet), var(--aurora-cyan));
  color: #15163f;
  box-shadow: 0 10px 28px rgba(167, 139, 250, 0.35);
}

.aurora-btn-primary:hover {
  transform: translateY(-2px);
  box-shadow: 0 14px 34px rgba(103, 232, 249, 0.35);
}

.aurora-btn-ghost {
  background: rgba(255, 255, 255, 0.05);
  border-color: rgba(255, 255, 255, 0.22);
  color: var(--aurora-text);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
}

.aurora-btn-ghost:hover {
  transform: translateY(-2px);
  background: rgba(255, 255, 255, 0.12);
  border-color: rgba(255, 255, 255, 0.32);
}

/* ---- Hero ---- */
.aurora-hero {
  position: relative;
  z-index: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: calc(100vh - 60px);
}

.hero-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 22px;
  width: min(1200px, 100%);
  padding: 48px 24px;
}

.hero-eyebrow {
  font-size: 11px;
  letter-spacing: 0.34em;
  color: var(--aurora-cyan);
  padding: 8px 18px;
  border-radius: 999px;
  border: 1px solid rgba(103, 232, 249, 0.3);
  background: rgba(103, 232, 249, 0.08);
}

.hero-title {
  margin: 0;
  font-size: clamp(48px, 10vw, 108px);
  font-weight: 700;
  letter-spacing: 0.06em;
  line-height: 1;
  text-align: center;
  background: linear-gradient(115deg, #f1f2ff 15%, #c4b5fd 50%, #67e8f9 88%);
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
  color: transparent;
}

.hero-sub {
  margin: 0;
  text-align: center;
  color: var(--aurora-muted);
  font-size: 15px;
  line-height: 1.9;
}

.hero-cta {
  display: flex;
  gap: 14px;
  margin-top: 6px;
}

.hero-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
  width: 100%;
  margin-top: 42px;
}

.kpi-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 20px 22px;
}

.kpi-label {
  font-size: 11px;
  letter-spacing: 0.18em;
  color: var(--aurora-muted);
  text-transform: uppercase;
}

.kpi-value {
  font-size: clamp(22px, 2.2vw, 28px);
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--aurora-text);
}

.kpi-value.tone-positive {
  color: var(--aurora-green);
}

.kpi-value.tone-negative {
  color: var(--aurora-rose);
}

.kpi-sub {
  font-size: 12px;
  color: var(--aurora-muted);
}

/* ---- 行情跑馬燈 ---- */
.aurora-ticker {
  position: relative;
  z-index: 1;
  overflow: hidden;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.03);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
}

.ticker-track {
  display: flex;
  width: max-content;
  animation: aurora-marquee 38s linear infinite;
}

.aurora-ticker:hover .ticker-track {
  animation-play-state: paused;
}

@keyframes aurora-marquee {
  from { transform: translateX(0); }
  to { transform: translateX(-50%); }
}

.ticker-item {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 13px 28px;
  font-size: 12px;
  white-space: nowrap;
  border-right: 1px solid rgba(255, 255, 255, 0.08);
}

.ticker-code {
  color: var(--aurora-muted);
  letter-spacing: 0.06em;
}

.ticker-price {
  color: var(--aurora-text);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.ticker-change {
  font-variant-numeric: tabular-nums;
}

.ticker-change.up {
  color: var(--aurora-green);
}

.ticker-change.down {
  color: var(--aurora-rose);
}

/* ---- 通用區塊 ---- */
.aurora-section {
  position: relative;
  z-index: 1;
  width: min(1200px, calc(100% - 48px));
  margin: 0 auto;
  padding: 48px 0 6px;
}

.section-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 16px;
  margin-bottom: 20px;
}

.section-label {
  font-size: 11px;
  letter-spacing: 0.3em;
  color: var(--aurora-violet);
}

.section-title {
  margin: 8px 0 0;
  font-size: 26px;
  font-weight: 700;
}

.panel-pad {
  padding: 22px;
}

.panel-label {
  display: block;
  font-size: 11px;
  letter-spacing: 0.22em;
  color: var(--aurora-muted);
  margin-bottom: 8px;
}

/* 滾動淡入 */
.aurora-reveal {
  opacity: 0;
  transform: translateY(26px);
  transition: opacity 0.9s ease, transform 0.9s cubic-bezier(0.22, 0.61, 0.36, 1);
}

.aurora-reveal.is-visible {
  opacity: 1;
  transform: none;
}

/* ---- Portfolio Pulse ---- */
.pulse-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 18px;
}

.range-switch {
  display: flex;
  gap: 4px;
  padding: 4px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
}

.range-btn {
  padding: 7px 16px;
  border-radius: 999px;
  border: none;
  background: transparent;
  color: var(--aurora-muted);
  font-size: 12px;
  letter-spacing: 0.08em;
  cursor: pointer;
  transition: all 0.25s ease;
}

.range-btn:hover {
  color: var(--aurora-text);
}

.range-btn.active {
  background: linear-gradient(120deg, var(--aurora-violet), var(--aurora-cyan));
  color: #15163f;
  font-weight: 700;
}

/* ---- Risk Matrix ---- */
.risk-grid {
  display: grid;
  grid-template-columns: 1fr 1.2fr 0.8fr;
  gap: 18px;
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
  gap: 6px;
  padding: 18px 20px;
}

.risk-stat .kpi-value {
  font-size: 24px;
}

/* ---- AI Research Feed ---- */
.feed-panel {
  padding: 6px 0;
}

.feed-item {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  transition: background 0.25s ease;
}

.feed-item:last-child {
  border-bottom: none;
}

.feed-item:hover {
  background: rgba(255, 255, 255, 0.05);
}

.feed-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.feed-dot[data-status='completed'] {
  background: var(--aurora-cyan);
}

.feed-dot[data-status='running'] {
  background: var(--aurora-violet);
  animation: dot-pulse 1.6s ease-in-out infinite;
}

.feed-dot[data-status='failed'] {
  background: var(--aurora-rose);
}

@keyframes dot-pulse {
  0%, 100% { box-shadow: 0 0 0 0 rgba(167, 139, 250, 0.45); }
  50% { box-shadow: 0 0 0 6px rgba(167, 139, 250, 0); }
}

.feed-body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.feed-title {
  font-size: 14px;
  color: var(--aurora-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-meta {
  font-size: 11px;
  color: var(--aurora-muted);
}

.feed-kind {
  font-size: 11px;
  padding: 4px 12px;
  border-radius: 999px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  color: var(--aurora-muted);
  white-space: nowrap;
}

.feed-status {
  font-size: 12px;
  letter-spacing: 0.08em;
  white-space: nowrap;
}

.feed-status.completed {
  color: var(--aurora-cyan);
}

.feed-status.running {
  color: var(--aurora-violet);
}

.feed-status.failed {
  color: var(--aurora-rose);
}

/* ---- Footer ---- */
.aurora-footer {
  position: relative;
  z-index: 1;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  width: min(1200px, calc(100% - 48px));
  margin: 48px auto 0;
  padding: 22px 0 30px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  color: var(--aurora-muted);
  font-size: 12px;
  letter-spacing: 0.06em;
}

/* ---- Responsive ---- */
@media (max-width: 960px) {
  .hero-kpis {
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

@media (max-width: 560px) {
  .hero-kpis {
    grid-template-columns: 1fr;
  }
  .hero-cta {
    flex-direction: column;
    width: 100%;
  }
  .hero-cta .aurora-btn {
    width: 100%;
  }
  .risk-side {
    flex-direction: column;
  }
  .feed-kind {
    display: none;
  }
  .aurora-footer {
    flex-direction: column;
    gap: 6px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .aurora-orb,
  .ticker-track,
  .feed-dot {
    animation: none;
  }
  .aurora-reveal {
    opacity: 1;
    transform: none;
    transition: none;
  }
}
</style>
