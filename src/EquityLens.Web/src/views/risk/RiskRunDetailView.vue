<script setup lang="ts">
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import LineChart from '../../components/kimi/LineChart.vue'
import Footer from '../../components/kimi/Footer.vue'

const router = useRouter()

const runInfo = {
  id: 'RR-2026-0603-001',
  portfolio: '科技成長型投資組合',
  model: 'Historical VaR',
  confidence: '95%',
  lookback: '252 天',
  date: '2026-06-03',
  duration: '12.3s',
}

const metrics = [
  { label: 'VaR 95%', value: '-2.3%', sub: '1-day horizon' },
  { label: 'VaR 99%', value: '-3.8%', sub: '1-day horizon' },
  { label: 'ES 95%', value: '-3.1%', sub: 'Expected Shortfall' },
  { label: 'Volatility', value: '12.4%', sub: 'Annualized' },
]

// Loss distribution data (histogram simulation)
const lossDistribution = Array.from({ length: 30 }, (_, i) => {
  const x = -5 + i * 0.33
  const y = Math.exp(-0.5 * Math.pow((x + 1.5) / 1.2, 2)) * 100
  return { x, y }
})

// VaR trend
const var95Trend = [-1.8, -2.1, -2.3, -1.9, -2.5, -2.2, -2.0, -2.3, -2.4, -2.1, -1.9, -2.3, -2.5, -2.2, -2.0, -1.8, -2.1, -2.3, -2.4, -2.2, -2.0, -1.9, -2.3, -2.1, -2.3]
const var99Trend = [-3.0, -3.5, -3.8, -3.2, -4.0, -3.6, -3.3, -3.8, -3.9, -3.5, -3.2, -3.8, -4.0, -3.6, -3.3, -3.0, -3.5, -3.8, -3.9, -3.6, -3.3, -3.2, -3.8, -3.5, -3.8]
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px">
    <!-- Back + Header -->
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn kimi-btn-dark" style="margin-bottom: 24px" @click="router.push({ name: 'risk-runs' })">
        ← BACK TO RISK RUNS
      </button>

      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">{{ runInfo.portfolio }}</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">RISK ANALYSIS RUN — {{ runInfo.id }}</span>
        </div>
        <span class="kimi-tag" style="border-color: #34d399; color: #34d399">COMPLETED</span>
      </div>

      <!-- Run Info -->
      <ScrollReveal>
        <div class="kimi-section-dark" style="margin-bottom: 40px">
          <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 0">
            <div v-for="(item, i) in [
              { label: 'MODEL', value: runInfo.model },
              { label: 'CONFIDENCE', value: runInfo.confidence },
              { label: 'LOOKBACK', value: runInfo.lookback },
              { label: 'DURATION', value: runInfo.duration },
            ]" :key="i"
              style="padding: 20px; border-right: 1px solid #333333; border-bottom: 1px solid #333333"
            >
              <span class="kimi-caption" style="margin-bottom: 4px; display: block; color: #666666">{{ item.label }}</span>
              <span style="font-size: 18px; font-weight: 600; color: #FFFFFF">{{ item.value }}</span>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Metrics -->
      <ScrollReveal :delay="0.1">
        <div class="kimi-section-dark">
          <div class="kimi-kpi-grid" style="display: grid; grid-template-columns: repeat(4, 1fr)">
            <div v-for="(m, i) in metrics" :key="i"
              style="padding: 24px; border-right: 1px solid #333333; border-bottom: 1px solid #333333; text-align: center; position: relative"
            >
              <span class="kimi-caption" style="color: #666666; margin-bottom: 8px; display: block">{{ m.label }}</span>
              <span style="font-size: 28px; font-weight: 600; color: #FFFFFF">{{ m.value }}</span>
              <span style="font-size: 12px; color: #666666; margin-top: 4px; display: block">{{ m.sub }}</span>
              <div class="accent-bar" style="background-color: #8B1A2B" />
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Loss Distribution + VaR Trend -->
      <div class="kimi-grid-2" style="margin-top: 40px">
        <!-- Loss Distribution -->
        <ScrollReveal style="padding: 20px; border: 1px solid #333333; background: #0A0A0A">
          <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600; color: #FFFFFF">損失分佈</h2>
          <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 16px">LOSS DISTRIBUTION</span>
          <svg width="100%" height="280" viewBox="0 0 500 280">
            <!-- Grid -->
            <line v-for="i in 5" :key="'g-' + i" x1="60" :y1="20 + (i - 1) * 50" x2="480" :y2="20 + (i - 1) * 50" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />

            <!-- Bars -->
            <rect
              v-for="(bar, i) in lossDistribution"
              :key="'b-' + i"
              :x="60 + (i / 30) * 420"
              :y="220 - bar.y * 1.8"
              :width="420 / 30 - 2"
              :height="bar.y * 1.8"
              :fill="bar.x < -2.3 ? (bar.x < -3.8 ? '#f87171' : '#fbbf24') : '#333333'"
            />

            <!-- VaR 95% line -->
            <line :x1="60 + ((-2.3 + 5) / 10) * 420" y1="20" :x2="60 + ((-2.3 + 5) / 10) * 420" y2="220" stroke="#fbbf24" stroke-width="1.5" stroke-dasharray="4 4" />
            <text :x="60 + ((-2.3 + 5) / 10) * 420 + 5" y="15" fill="#fbbf24" font-size="10" font-weight="600">VaR 95%</text>

            <!-- VaR 99% line -->
            <line :x1="60 + ((-3.8 + 5) / 10) * 420" y1="20" :x2="60 + ((-3.8 + 5) / 10) * 420" y2="220" stroke="#f87171" stroke-width="1.5" stroke-dasharray="4 4" />
            <text :x="60 + ((-3.8 + 5) / 10) * 420 + 5" y="30" fill="#f87171" font-size="10" font-weight="600">VaR 99%</text>
          </svg>
        </ScrollReveal>

        <!-- VaR Trend -->
        <ScrollReveal :delay="0.1" style="padding: 20px; border: 1px solid #333333; background: #0A0A0A">
          <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600; color: #FFFFFF">VaR 歷史趨勢</h2>
          <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 16px">HISTORICAL VaR TREND</span>
          <LineChart
            :data="var95Trend"
            :labels="var95Trend.map((_, i) => String(i + 1))"
            :y-axis-labels="['-5%', '-4%', '-3%', '-2%', '-1%', '0%']"
            :height="280"
            line-color="#fbbf24"
            grid-color="#333333"
            text-color="#666666"
            :dark="true"
            :show-area="false"
            :second-line="var99Trend"
            second-line-color="#f87171"
          />
        </ScrollReveal>
      </div>

      <div style="height: 80px" />
    </div>

    <Footer :dark="true" label="RISK" />
  </div>
</template>

<style scoped>
@media (max-width: 1024px) {
  .kimi-grid-2 > * {
    border-right: none !important;
  }
  .kimi-grid-2 {
    display: block;
  }
}
</style>
