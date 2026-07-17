<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import gsap from 'gsap'
import TechParticleField from '../../components/tech/TechParticleField.vue'
import TechStatCard from '../../components/tech/TechStatCard.vue'
import TechChart from '../../components/tech/TechChart.vue'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import {
  aiFeed,
  allocation,
  allocationColors,
  equityCurves,
  equityRanges,
  heroKpis,
  riskRadar,
  tickerItems,
  varMetrics,
  type EquityRange,
} from '../../data/homeTechData'

const router = useRouter()

// ---- Hero 進場動畫 ----
const heroTitleRef = ref<HTMLElement>()
const heroSubRef = ref<HTMLElement>()
const heroCtaRef = ref<HTMLElement>()
const heroKpiRef = ref<HTMLElement>()

onMounted(() => {
  if (!heroTitleRef.value) return
  gsap.fromTo(
    heroTitleRef.value,
    { opacity: 0, filter: 'blur(14px)', y: 24 },
    { opacity: 1, filter: 'blur(0px)', y: 0, duration: 1.2, ease: 'power3.out' }
  )
  gsap.fromTo(
    [heroSubRef.value, heroCtaRef.value, heroKpiRef.value].filter((el): el is HTMLElement => !!el),
    { opacity: 0, y: 20 },
    { opacity: 1, y: 0, duration: 0.9, ease: 'power2.out', stagger: 0.18, delay: 0.5 }
  )
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
      axisLine: { lineStyle: { color: 'rgba(56,189,248,0.25)' } },
      axisTick: { show: false },
      axisLabel: { color: '#7d8aa8', fontSize: 11 },
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: 'rgba(56,189,248,0.08)' } },
      axisLabel: {
        color: '#7d8aa8',
        fontSize: 11,
        formatter: (v: number) => `NT$${(v / 1_000_000).toFixed(1)}M`,
      },
    },
    tooltip: { trigger: 'axis' },
    series: [
      {
        type: 'line',
        data: points.map((p) => p.value),
        smooth: true,
        showSymbol: false,
        lineStyle: {
          width: 2.5,
          color: '#22d3ee',
          shadowColor: 'rgba(34,211,238,0.6)',
          shadowBlur: 14,
        },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0, y: 0, x2: 0, y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(34,211,238,0.28)' },
              { offset: 1, color: 'rgba(34,211,238,0)' },
            ],
          },
        },
      },
    ],
  }
})

// ---- 配置 donut ----
const allocationOption = computed(() => ({
  tooltip: { trigger: 'item', formatter: '{b}: {c}%' },
  title: {
    text: 'NT$12.58M',
    subtext: 'TOTAL ASSETS',
    left: 'center',
    top: '42%',
    textStyle: { color: '#e6f1ff', fontSize: 20, fontFamily: 'JetBrains Mono, Consolas, monospace' },
    subtextStyle: { color: '#7d8aa8', fontSize: 10 },
  },
  series: [
    {
      type: 'pie',
      radius: ['58%', '80%'],
      center: ['50%', '50%'],
      avoidLabelOverlap: true,
      label: { color: '#7d8aa8', fontSize: 11, formatter: '{b} {c}%' },
      labelLine: { lineStyle: { color: 'rgba(56,189,248,0.35)' } },
      itemStyle: { borderColor: '#050a14', borderWidth: 2 },
      emphasis: { scaleSize: 6, itemStyle: { shadowBlur: 18, shadowColor: 'rgba(34,211,238,0.45)' } },
      data: allocation.map((slice, i) => ({
        ...slice,
        itemStyle: { color: allocationColors[i % allocationColors.length] },
      })),
    },
  ],
}))

// ---- 風險雷達 ----
const radarOption = computed(() => ({
  radar: {
    indicator: riskRadar.indicators.map((name) => ({ name, max: 100 })),
    radius: '68%',
    axisName: { color: '#7d8aa8', fontSize: 11 },
    splitLine: { lineStyle: { color: 'rgba(56,189,248,0.15)' } },
    splitArea: { areaStyle: { color: ['rgba(56,189,248,0.02)', 'rgba(56,189,248,0.05)'] } },
    axisLine: { lineStyle: { color: 'rgba(56,189,248,0.2)' } },
  },
  series: [
    {
      type: 'radar',
      data: [
        {
          value: riskRadar.scores,
          name: '組合因子暴露',
          lineStyle: { color: '#22d3ee', width: 2, shadowColor: 'rgba(34,211,238,0.5)', shadowBlur: 10 },
          areaStyle: { color: 'rgba(34,211,238,0.18)' },
          itemStyle: { color: '#22d3ee' },
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
    splitLine: { lineStyle: { color: 'rgba(56,189,248,0.08)' } },
    axisLabel: { color: '#7d8aa8', fontSize: 11, formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    data: varMetrics.map((m) => m.label),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: '#7d8aa8', fontSize: 11 },
  },
  tooltip: { trigger: 'axis', formatter: '{b}: {c}%' },
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
            { offset: 0, color: 'rgba(251,113,133,0.9)' },
            { offset: 1, color: 'rgba(251,113,133,0.25)' },
          ],
        },
        shadowColor: 'rgba(251,113,133,0.4)',
        shadowBlur: 10,
      },
      label: { show: true, position: 'right', color: '#fb7185', fontSize: 11, formatter: '{c}%' },
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
  <div class="tech-page">
    <!-- ============ Hero ============ -->
    <section class="hero">
      <div class="tech-grid-overlay" />
      <TechParticleField />
      <div class="tech-scanline" />

      <div class="hero-content">
        <span class="tech-label hero-eyebrow">AI-Assisted Investment Analytics</span>
        <h1 ref="heroTitleRef" class="hero-title">
          <span class="tech-glow-text">EQUITYLENS</span>
        </h1>
        <p ref="heroSubRef" class="hero-sub">
          整合投資組合帳務、量化風險與 AI 研究工作流<br />
          讓每一個投資決策都有證據可循
        </p>
        <div ref="heroCtaRef" class="hero-cta">
          <button class="tech-btn tech-btn-solid" @click="router.push({ name: 'dashboard' })">
            進入儀表板
          </button>
          <button class="tech-btn" @click="router.push({ name: 'research' })">
            查看 AI 研究
          </button>
        </div>

        <div ref="heroKpiRef" class="hero-kpis">
          <TechStatCard
            v-for="kpi in heroKpis"
            :key="kpi.label"
            :label="kpi.label"
            :value="kpi.value"
            :prefix="kpi.prefix"
            :suffix="kpi.suffix"
            :decimals="kpi.decimals"
            :sub="kpi.sub"
            :tone="kpi.tone"
          />
        </div>
      </div>
    </section>

    <!-- ============ 行情跑馬燈 ============ -->
    <div class="tech-ticker">
      <div class="tech-ticker-track">
        <span v-for="(item, i) in tickerLoop" :key="i" class="ticker-item tech-mono">
          <span class="ticker-code">{{ item.code }} {{ item.name }}</span>
          <span class="ticker-price">{{ item.price }}</span>
          <span :class="['ticker-change', item.changePct >= 0 ? 'up' : 'down']">
            {{ item.changePct >= 0 ? '▲' : '▼' }} {{ Math.abs(item.changePct).toFixed(2) }}%
          </span>
        </span>
      </div>
    </div>

    <!-- ============ Portfolio Pulse ============ -->
    <section class="tech-section">
      <ScrollReveal>
        <div class="tech-section-head">
          <div>
            <span class="tech-label">Portfolio Pulse</span>
            <h2 class="tech-section-title">組合淨值走勢</h2>
          </div>
          <div class="range-switch">
            <button
              v-for="range in equityRanges"
              :key="range"
              :class="['range-btn tech-mono', activeRange === range && 'active']"
              @click="activeRange = range"
            >
              {{ range }}
            </button>
          </div>
        </div>

        <div class="pulse-grid">
          <div class="tech-panel tech-panel-corners tech-panel-pad">
            <TechChart :option="equityOption" height="360px" />
          </div>
          <div class="tech-panel tech-panel-pad">
            <span class="tech-label">Asset Allocation</span>
            <TechChart :option="allocationOption" height="330px" />
          </div>
        </div>
      </ScrollReveal>
    </section>

    <!-- ============ Risk Matrix ============ -->
    <section class="tech-section">
      <ScrollReveal>
        <div class="tech-section-head">
          <div>
            <span class="tech-label">Risk Matrix</span>
            <h2 class="tech-section-title">風險矩陣</h2>
          </div>
        </div>

        <div class="risk-grid">
          <div class="tech-panel tech-panel-pad">
            <span class="tech-label">Factor Exposure</span>
            <TechChart :option="radarOption" height="300px" />
          </div>
          <div class="tech-panel tech-panel-pad">
            <span class="tech-label">Value at Risk(日,1 年歷史模擬)</span>
            <TechChart :option="varOption" height="300px" />
          </div>
          <div class="risk-side">
            <TechStatCard
              label="Max Drawdown"
              :value="-8.4"
              suffix="%"
              :decimals="1"
              sub="2025.09 – 2025.11 區間"
              tone="negative"
            />
            <TechStatCard
              label="Volatility(年化)"
              :value="14.2"
              suffix="%"
              :decimals="1"
              sub="滾動 90 日"
            />
            <TechStatCard
              label="Beta(vs 加權指數)"
              :value="1.08"
              :decimals="2"
              sub="滾動 252 日"
            />
          </div>
        </div>
      </ScrollReveal>
    </section>

    <!-- ============ AI Research Feed ============ -->
    <section class="tech-section">
      <ScrollReveal>
        <div class="tech-section-head">
          <div>
            <span class="tech-label">AI Research Feed</span>
            <h2 class="tech-section-title">AI 研究動態</h2>
          </div>
          <button class="tech-btn" @click="router.push({ name: 'research' })">全部研究</button>
        </div>

        <div class="tech-panel tech-panel-corners feed-panel">
          <div v-for="(item, i) in aiFeed" :key="i" class="feed-item">
            <span :class="['tech-dot', item.status === 'running' && 'pulse']" :data-status="item.status" />
            <div class="feed-body">
              <span class="feed-title">{{ item.title }}</span>
              <span class="feed-meta tech-mono">{{ item.time }}</span>
            </div>
            <span class="tech-tag">{{ item.kind }}</span>
            <span :class="['feed-status tech-mono', item.status]">
              {{ feedStatusLabel[item.status] }}
            </span>
          </div>
        </div>
      </ScrollReveal>
    </section>

    <!-- ============ Footer ============ -->
    <footer class="tech-footer">
      <span class="tech-mono">EQUITYLENS © 2026</span>
      <span>本頁數據為展示用途,不構成投資建議</span>
    </footer>
  </div>
</template>

<style scoped>
/* ---- Hero ---- */
.hero {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: calc(100vh - 60px);
  overflow: hidden;
}

.hero-content {
  position: relative;
  z-index: 2;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 20px;
  padding: 48px 24px;
  width: min(1200px, 100%);
}

.hero-eyebrow {
  color: var(--tech-cyan);
  text-shadow: 0 0 12px rgba(34, 211, 238, 0.5);
}

.hero-title {
  margin: 0;
  font-size: clamp(44px, 9vw, 104px);
  font-weight: 800;
  letter-spacing: 0.04em;
  line-height: 1;
  text-align: center;
}

.hero-sub {
  margin: 0;
  text-align: center;
  color: var(--tech-muted);
  font-size: 15px;
  line-height: 1.8;
}

.hero-cta {
  display: flex;
  gap: 14px;
  margin-top: 8px;
}

.hero-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 14px;
  width: 100%;
  margin-top: 40px;
}

/* ---- Ticker ---- */
.ticker-item {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 12px 28px;
  font-size: 12px;
  white-space: nowrap;
  border-right: 1px solid var(--tech-border);
}

.ticker-code {
  color: var(--tech-muted);
  letter-spacing: 0.06em;
}

.ticker-price {
  color: var(--tech-text);
  font-weight: 600;
}

.ticker-change.up {
  color: var(--tech-green);
  text-shadow: 0 0 10px rgba(52, 211, 153, 0.5);
}

.ticker-change.down {
  color: var(--tech-red);
  text-shadow: 0 0 10px rgba(251, 113, 133, 0.5);
}

/* ---- Portfolio Pulse ---- */
.pulse-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 16px;
}

.range-switch {
  display: flex;
  gap: 6px;
}

.range-btn {
  padding: 6px 14px;
  background: transparent;
  border: 1px solid var(--tech-border);
  border-radius: 8px;
  color: var(--tech-muted);
  font-size: 12px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.range-btn:hover {
  color: var(--tech-cyan);
  border-color: var(--tech-border-strong);
}

.range-btn.active {
  color: #04121f;
  background: var(--tech-cyan);
  border-color: var(--tech-cyan);
  box-shadow: 0 0 14px rgba(34, 211, 238, 0.4);
}

/* ---- Risk ---- */
.risk-grid {
  display: grid;
  grid-template-columns: 1fr 1.2fr 0.8fr;
  gap: 16px;
}

.risk-side {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* ---- Feed ---- */
.feed-panel {
  padding: 8px 0;
}

.feed-item {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 24px;
  border-bottom: 1px solid var(--tech-border);
  transition: background 0.2s ease;
}

.feed-item:last-child {
  border-bottom: none;
}

.feed-item:hover {
  background: rgba(56, 189, 248, 0.04);
}

.tech-dot[data-status='failed'] {
  background: var(--tech-red);
  box-shadow: 0 0 8px var(--tech-red);
}

.tech-dot[data-status='running'] {
  background: var(--tech-violet);
  box-shadow: 0 0 8px var(--tech-violet);
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
  color: var(--tech-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-meta {
  font-size: 11px;
  color: var(--tech-muted);
}

.feed-status {
  font-size: 11px;
  letter-spacing: 0.12em;
}

.feed-status.completed {
  color: var(--tech-cyan);
}

.feed-status.running {
  color: var(--tech-violet);
}

.feed-status.failed {
  color: var(--tech-red);
}

/* ---- Footer ---- */
.tech-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  width: min(1200px, calc(100% - 48px));
  margin: 48px auto 0;
  padding: 20px 0 28px;
  border-top: 1px solid var(--tech-border);
  color: var(--tech-muted);
  font-size: 12px;
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
}

@media (max-width: 560px) {
  .hero-kpis {
    grid-template-columns: 1fr;
  }
  .hero-cta {
    flex-direction: column;
    width: 100%;
  }
  .hero-cta .tech-btn {
    justify-content: center;
  }
  .tech-footer {
    flex-direction: column;
    gap: 6px;
  }
}
</style>
