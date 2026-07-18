<script setup lang="ts">
import { computed, onMounted, ref, type Directive } from 'vue'
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
} from '../../data/homeTechData'

const router = useRouter()

// ---- 大地色票(本頁專用,不沿用 homeTechData 的霓虹色)----
const zenPalette = ['#b0654a', '#5c6650', '#d9c7a7', '#8a8071', '#2f2a24', '#e5dcc8']

// ---- Hero 進場:慢速柔和 fade-up ----
const heroRef = ref<HTMLElement>()

onMounted(() => {
  if (!heroRef.value) return
  const targets = Array.from(heroRef.value.querySelectorAll('.hero-fade'))
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    gsap.set(targets, { opacity: 1, y: 0 })
    return
  }
  gsap.fromTo(
    targets,
    { opacity: 0, y: 32 },
    { opacity: 1, y: 0, duration: 1.2, ease: 'power2.out', stagger: 0.16 }
  )
})

// ---- 區塊進場:IntersectionObserver 觸發的慢速 fade-up ----
const vZenReveal: Directive<HTMLElement> = {
  mounted(el) {
    el.classList.add('zen-reveal')
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            entry.target.classList.add('is-visible')
            io.unobserve(entry.target)
          }
        }
      },
      { threshold: 0.12 }
    )
    io.observe(el)
  },
}

// ---- KPI 數字格式 ----
const formatKpiValue = (value: number, decimals: number) =>
  value.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })

// ---- 行情慢速跑馬燈(內容兩份 + translateX -50% 無縫循環)----
const tickerLoop = computed(() => [...tickerItems, ...tickerItems])

// ---- 淨值曲線 ----
const activeRange = ref<EquityRange>('6M')

const equityOption = computed(() => {
  const points = equityCurves[activeRange.value]
  return {
    textStyle: { color: '#8a8071' },
    grid: { left: 56, right: 20, top: 24, bottom: 32 },
    xAxis: {
      type: 'category',
      data: points.map((p) => p.date.slice(5)),
      axisLine: { lineStyle: { color: 'rgba(47,42,36,0.18)' } },
      axisTick: { show: false },
      axisLabel: { color: '#8a8071', fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: 'rgba(47,42,36,0.06)' } },
      axisLabel: {
        color: '#8a8071',
        fontSize: 11,
        formatter: (v: number) => `NT$${(v / 1_000_000).toFixed(1)}M`,
      },
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: '#fffdf8',
      borderColor: 'rgba(47,42,36,0.12)',
      textStyle: { color: '#2f2a24', fontSize: 12 },
    },
    series: [
      {
        type: 'line',
        data: points.map((p) => p.value),
        smooth: true,
        showSymbol: false,
        lineStyle: { width: 2.5, color: '#b0654a' },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0, y: 0, x2: 0, y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(176,101,74,0.2)' },
              { offset: 1, color: 'rgba(176,101,74,0)' },
            ],
          },
        },
      },
    ],
  }
})

// ---- 配置 donut(大地色,中心顯示總資產)----
const allocationOption = computed(() => ({
  textStyle: { color: '#8a8071' },
  tooltip: {
    trigger: 'item',
    formatter: '{b}: {c}%',
    backgroundColor: '#fffdf8',
    borderColor: 'rgba(47,42,36,0.12)',
    textStyle: { color: '#2f2a24', fontSize: 12 },
  },
  title: {
    text: 'NT$12.58M',
    subtext: '總資產',
    left: 'center',
    top: '40%',
    textStyle: {
      color: '#2f2a24',
      fontSize: 20,
      fontWeight: 500,
      fontFamily: "Georgia, 'Noto Serif TC', serif",
    },
    subtextStyle: { color: '#8a8071', fontSize: 11 },
  },
  series: [
    {
      type: 'pie',
      radius: ['58%', '80%'],
      center: ['50%', '50%'],
      avoidLabelOverlap: true,
      label: { color: '#8a8071', fontSize: 11, formatter: '{b} {c}%' },
      labelLine: { lineStyle: { color: 'rgba(47,42,36,0.25)' } },
      itemStyle: { borderColor: '#fffdf8', borderWidth: 3, borderRadius: 8 },
      emphasis: {
        scaleSize: 5,
        itemStyle: { shadowBlur: 16, shadowColor: 'rgba(47,42,36,0.18)' },
      },
      data: allocation.map((slice, i) => ({
        ...slice,
        itemStyle: { color: zenPalette[i % zenPalette.length] },
      })),
    },
  ],
}))

// ---- 風險雷達(橄欖主線)----
const radarOption = computed(() => ({
  textStyle: { color: '#8a8071' },
  tooltip: {
    backgroundColor: '#fffdf8',
    borderColor: 'rgba(47,42,36,0.12)',
    textStyle: { color: '#2f2a24', fontSize: 12 },
  },
  radar: {
    indicator: riskRadar.indicators.map((name) => ({ name, max: 100 })),
    radius: '68%',
    axisName: { color: '#8a8071', fontSize: 11 },
    splitLine: { lineStyle: { color: 'rgba(47,42,36,0.1)' } },
    splitArea: { areaStyle: { color: ['rgba(217,199,167,0.1)', 'rgba(217,199,167,0.2)'] } },
    axisLine: { lineStyle: { color: 'rgba(47,42,36,0.15)' } },
  },
  series: [
    {
      type: 'radar',
      data: [
        {
          value: riskRadar.scores,
          name: '組合因子暴露',
          lineStyle: { color: '#5c6650', width: 2 },
          areaStyle: { color: 'rgba(92,102,80,0.16)' },
          itemStyle: { color: '#5c6650' },
        },
      ],
    },
  ],
}))

// ---- VaR 水平 bar(陶土紅)----
const varOption = computed(() => ({
  textStyle: { color: '#8a8071' },
  grid: { left: 80, right: 40, top: 10, bottom: 10 },
  xAxis: {
    type: 'value',
    max: 0,
    splitLine: { lineStyle: { color: 'rgba(47,42,36,0.06)' } },
    axisLabel: { color: '#8a8071', fontSize: 11, formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    data: varMetrics.map((m) => m.label),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: '#8a8071', fontSize: 11 },
  },
  tooltip: {
    trigger: 'axis',
    formatter: '{b}: {c}%',
    backgroundColor: '#fffdf8',
    borderColor: 'rgba(47,42,36,0.12)',
    textStyle: { color: '#2f2a24', fontSize: 12 },
  },
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
            { offset: 0, color: 'rgba(176,101,74,0.85)' },
            { offset: 1, color: 'rgba(176,101,74,0.3)' },
          ],
        },
      },
      label: { show: true, position: 'right', color: '#b0654a', fontSize: 11, formatter: '{c}%' },
    },
  ],
}))

// ---- AI 研究動態狀態(橄欖/暖沙/陶土)----
const feedStatusLabel: Record<string, string> = {
  completed: '已完成',
  running: '進行中',
  failed: '失敗',
}
</script>

<template>
  <div class="zen-page">
    <!-- ============ Hero ============ -->
    <section ref="heroRef" class="hero">
      <div class="hero-content">
        <span class="eyebrow hero-fade">AI-Assisted Investment Analytics</span>
        <h1 class="hero-title hero-fade">EQUITYLENS</h1>
        <p class="hero-sub hero-fade">
          整合投資組合帳務、量化風險與 AI 研究工作流<br />
          讓每一個投資決策都有證據可循
        </p>
        <div class="hero-cta hero-fade">
          <button class="zen-btn zen-btn-solid" @click="router.push({ name: 'dashboard' })">
            進入儀表板
          </button>
          <button class="zen-btn zen-btn-ghost" @click="router.push({ name: 'research' })">
            查看 AI 研究
          </button>
        </div>

        <div class="hero-kpis hero-fade">
          <div v-for="kpi in heroKpis" :key="kpi.label" class="zen-card kpi-card">
            <span class="kpi-label">{{ kpi.label }}</span>
            <span :class="['kpi-value', kpi.tone]">
              {{ kpi.prefix }}{{ formatKpiValue(kpi.value, kpi.decimals) }}{{ kpi.suffix }}
            </span>
            <span class="kpi-sub">{{ kpi.sub }}</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ 行情慢速跑馬燈 ============ -->
    <div class="zen-ticker">
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
    <section v-zen-reveal class="zen-section">
      <div class="section-head">
        <div>
          <span class="eyebrow">Portfolio Pulse</span>
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
        <div class="zen-card chart-card">
          <TechChart :option="equityOption" height="360px" />
        </div>
        <div class="zen-card chart-card">
          <span class="card-label">資產配置</span>
          <TechChart :option="allocationOption" height="330px" />
        </div>
      </div>
    </section>

    <!-- ============ Risk Matrix ============ -->
    <section v-zen-reveal class="zen-section">
      <div class="section-head">
        <div>
          <span class="eyebrow">Risk Matrix</span>
          <h2 class="section-title">風險矩陣</h2>
        </div>
      </div>

      <div class="risk-grid">
        <div class="zen-card chart-card">
          <span class="card-label">因子暴露</span>
          <TechChart :option="radarOption" height="300px" />
        </div>
        <div class="zen-card chart-card">
          <span class="card-label">Value at Risk(日,1 年歷史模擬)</span>
          <TechChart :option="varOption" height="300px" />
        </div>
        <div class="risk-side">
          <div class="zen-card risk-stat">
            <span class="kpi-label">Max Drawdown</span>
            <span class="kpi-value negative">-8.4%</span>
            <span class="kpi-sub">2025.09 – 2025.11 區間</span>
          </div>
          <div class="zen-card risk-stat">
            <span class="kpi-label">Volatility(年化)</span>
            <span class="kpi-value">14.2%</span>
            <span class="kpi-sub">滾動 90 日</span>
          </div>
          <div class="zen-card risk-stat">
            <span class="kpi-label">Beta(vs 加權指數)</span>
            <span class="kpi-value">1.08</span>
            <span class="kpi-sub">滾動 252 日</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ============ AI Research Feed ============ -->
    <section v-zen-reveal class="zen-section">
      <div class="section-head">
        <div>
          <span class="eyebrow">AI Research Feed</span>
          <h2 class="section-title">AI 研究動態</h2>
        </div>
        <button class="zen-btn zen-btn-ghost zen-btn-sm" @click="router.push({ name: 'research' })">
          全部研究
        </button>
      </div>

      <div class="zen-card feed-card">
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
    <footer class="zen-footer">
      <span class="footer-brand">EQUITYLENS © 2026</span>
      <span>本頁數據為展示用途,不構成投資建議</span>
    </footer>
  </div>
</template>

<style scoped>
.zen-page {
  --zen-bg: #f7f3ec;
  --zen-ink: #2f2a24;
  --zen-muted: #8a8071;
  --zen-terra: #b0654a;
  --zen-olive: #5c6650;
  --zen-sand: #d9c7a7;
  --zen-card: #fffdf8;
  --zen-cream: #e5dcc8;
  --zen-serif: Georgia, 'Noto Serif TC', serif;
  --zen-sans: 'Inter', 'Noto Sans TC', sans-serif;

  position: relative;
  min-height: 100vh;
  background: var(--zen-bg);
  color: var(--zen-ink);
  font-family: var(--zen-sans);
  font-weight: 300;
}

/* 角落極淡陶土 / 橄欖暈染 */
.zen-page::before,
.zen-page::after {
  content: '';
  position: absolute;
  z-index: 0;
  pointer-events: none;
  border-radius: 50%;
}

.zen-page::before {
  top: -180px;
  right: -140px;
  width: 560px;
  height: 560px;
  background: radial-gradient(circle, rgba(176, 101, 74, 0.1), transparent 68%);
}

.zen-page::after {
  top: 320px;
  left: -200px;
  width: 520px;
  height: 520px;
  background: radial-gradient(circle, rgba(92, 102, 80, 0.08), transparent 68%);
}

.zen-page > * {
  position: relative;
  z-index: 1;
}

/* ---- 共用元素 ---- */
.eyebrow {
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.28em;
  text-transform: uppercase;
  color: var(--zen-terra);
}

.zen-card {
  background: var(--zen-card);
  border: 1px solid rgba(47, 42, 36, 0.05);
  border-radius: 24px;
  box-shadow: 0 20px 40px -20px rgba(47, 42, 36, 0.15);
}

.zen-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 14px 34px;
  border: 1px solid transparent;
  border-radius: 999px;
  background: transparent;
  font-family: var(--zen-sans);
  font-size: 14px;
  font-weight: 400;
  letter-spacing: 0.08em;
  cursor: pointer;
  transition: background 0.35s ease, color 0.35s ease, border-color 0.35s ease, transform 0.35s ease,
    box-shadow 0.35s ease;
}

.zen-btn-solid {
  background: var(--zen-terra);
  color: #fffdf8;
  box-shadow: 0 14px 28px -14px rgba(176, 101, 74, 0.55);
}

.zen-btn-solid:hover {
  background: #9c563d;
  transform: translateY(-2px);
}

.zen-btn-ghost {
  color: var(--zen-ink);
  border-color: rgba(47, 42, 36, 0.22);
}

.zen-btn-ghost:hover {
  border-color: var(--zen-ink);
  transform: translateY(-2px);
}

.zen-btn-sm {
  padding: 10px 24px;
  font-size: 13px;
}

/* ---- 區塊進場(慢速柔和 fade-up)---- */
.zen-reveal {
  opacity: 0;
  transform: translateY(28px);
  transition: opacity 1.05s ease, transform 1.05s ease;
}

.zen-reveal.is-visible {
  opacity: 1;
  transform: translateY(0);
}

/* ---- Hero ---- */
.hero {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: calc(100vh - 60px);
  padding: 72px 24px 56px;
}

.hero-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 22px;
  width: min(1080px, 100%);
  text-align: center;
}

.hero-title {
  margin: 0;
  font-family: var(--zen-serif);
  font-size: clamp(40px, 8vw, 88px);
  font-weight: 500;
  letter-spacing: 0.08em;
  line-height: 1.05;
  color: var(--zen-ink);
}

.hero-sub {
  margin: 0;
  color: var(--zen-muted);
  font-size: 15px;
  line-height: 2;
}

.hero-cta {
  display: flex;
  gap: 14px;
  margin-top: 10px;
}

.hero-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 18px;
  width: 100%;
  margin-top: 52px;
}

.kpi-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
  padding: 26px 24px 22px;
  text-align: left;
}

.kpi-label {
  font-size: 11px;
  font-weight: 400;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--zen-muted);
}

.kpi-value {
  font-family: var(--zen-serif);
  font-size: 30px;
  font-weight: 500;
  line-height: 1.1;
  color: var(--zen-ink);
}

.kpi-value.positive {
  color: var(--zen-olive);
}

.kpi-value.negative {
  color: var(--zen-terra);
}

.kpi-sub {
  font-size: 12px;
  color: var(--zen-muted);
}

/* ---- 行情慢速跑馬燈 ---- */
.zen-ticker {
  overflow: hidden;
  background: rgba(255, 253, 248, 0.6);
  border-top: 1px solid rgba(47, 42, 36, 0.07);
  border-bottom: 1px solid rgba(47, 42, 36, 0.07);
}

.ticker-track {
  display: inline-flex;
  white-space: nowrap;
  will-change: transform;
  animation: zen-marquee 60s linear infinite;
}

@keyframes zen-marquee {
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
  gap: 10px;
  padding: 14px 30px;
  font-size: 12px;
  white-space: nowrap;
  border-right: 1px solid rgba(47, 42, 36, 0.06);
}

.ticker-code {
  color: var(--zen-muted);
  letter-spacing: 0.06em;
}

.ticker-price {
  color: var(--zen-ink);
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.ticker-change.up {
  color: var(--zen-olive);
}

.ticker-change.down {
  color: var(--zen-terra);
}

/* ---- 區塊排版 ---- */
.zen-section {
  width: min(1200px, calc(100% - 48px));
  margin: 0 auto;
  padding: 80px 0 16px;
}

.section-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 28px;
}

.section-title {
  margin: 10px 0 0;
  font-family: var(--zen-serif);
  font-size: clamp(26px, 3.4vw, 36px);
  font-weight: 500;
  letter-spacing: 0.02em;
  color: var(--zen-ink);
}

.card-label {
  display: block;
  margin-bottom: 8px;
  font-size: 11px;
  font-weight: 400;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--zen-muted);
}

.chart-card {
  padding: 28px;
}

/* ---- Portfolio Pulse ---- */
.pulse-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 20px;
}

.range-switch {
  display: flex;
  gap: 6px;
  padding: 5px;
  background: rgba(255, 253, 248, 0.75);
  border: 1px solid rgba(47, 42, 36, 0.06);
  border-radius: 999px;
}

.range-btn {
  padding: 8px 18px;
  border: none;
  border-radius: 999px;
  background: transparent;
  color: var(--zen-muted);
  font-family: var(--zen-sans);
  font-size: 12px;
  font-weight: 400;
  letter-spacing: 0.08em;
  cursor: pointer;
  transition: background 0.3s ease, color 0.3s ease, box-shadow 0.3s ease;
}

.range-btn:hover {
  color: var(--zen-ink);
}

.range-btn.active {
  background: var(--zen-terra);
  color: #fffdf8;
  box-shadow: 0 8px 16px -8px rgba(176, 101, 74, 0.6);
}

/* ---- Risk Matrix ---- */
.risk-grid {
  display: grid;
  grid-template-columns: 1fr 1.15fr 0.85fr;
  gap: 20px;
}

.risk-side {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.risk-stat {
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 6px;
  padding: 22px 24px;
}

/* ---- AI Research Feed ---- */
.feed-card {
  padding: 10px 28px;
}

.feed-item {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 20px 0;
  border-bottom: 1px solid rgba(47, 42, 36, 0.06);
}

.feed-item:last-child {
  border-bottom: none;
}

.feed-dot {
  flex-shrink: 0;
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.feed-dot.completed {
  background: var(--zen-olive);
}

.feed-dot.running {
  background: var(--zen-sand);
}

.feed-dot.failed {
  background: var(--zen-terra);
}

.feed-body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.feed-title {
  font-size: 14px;
  font-weight: 400;
  color: var(--zen-ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-meta {
  font-size: 12px;
  color: var(--zen-muted);
}

.feed-kind {
  padding: 4px 12px;
  border: 1px solid rgba(47, 42, 36, 0.1);
  border-radius: 999px;
  font-size: 11px;
  letter-spacing: 0.08em;
  color: var(--zen-muted);
  white-space: nowrap;
}

.feed-status {
  padding: 5px 14px;
  border-radius: 999px;
  font-size: 12px;
  white-space: nowrap;
}

.feed-status.completed {
  color: var(--zen-olive);
  background: rgba(92, 102, 80, 0.1);
}

.feed-status.running {
  color: #7a6a45;
  background: rgba(217, 199, 167, 0.4);
}

.feed-status.failed {
  color: var(--zen-terra);
  background: rgba(176, 101, 74, 0.1);
}

/* ---- Footer ---- */
.zen-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  width: min(1200px, calc(100% - 48px));
  margin: 72px auto 0;
  padding: 26px 0 36px;
  border-top: 1px solid rgba(47, 42, 36, 0.08);
  color: var(--zen-muted);
  font-size: 12px;
}

.footer-brand {
  font-family: var(--zen-serif);
  font-weight: 500;
  letter-spacing: 0.2em;
  color: var(--zen-ink);
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

  .zen-section {
    padding: 56px 0 12px;
  }
}

@media (max-width: 560px) {
  .hero-kpis {
    grid-template-columns: 1fr;
    margin-top: 36px;
  }

  .hero-cta {
    flex-direction: column;
    width: 100%;
  }

  .hero-cta .zen-btn {
    width: 100%;
  }

  .section-head {
    flex-direction: column;
    align-items: flex-start;
  }

  .chart-card {
    padding: 18px;
  }

  .feed-kind {
    display: none;
  }

  .zen-footer {
    flex-direction: column;
    gap: 6px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .ticker-track {
    animation: none;
  }

  .zen-reveal {
    opacity: 1;
    transform: none;
    transition: none;
  }
}
</style>
