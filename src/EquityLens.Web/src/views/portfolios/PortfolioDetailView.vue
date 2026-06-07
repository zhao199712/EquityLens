<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import LineChart from '../../components/kimi/LineChart.vue'
import DonutChart from '../../components/kimi/DonutChart.vue'
import BarChart from '../../components/kimi/BarChart.vue'
import DataTable from '../../components/kimi/DataTable.vue'
import ScatterPlot from '../../components/kimi/ScatterPlot.vue'
import Footer from '../../components/kimi/Footer.vue'
import {
  kpiData,
  portfolioValueData,
  allocationData,
  performanceAttribution,
  riskData,
  scenarioData,
} from '../../data/portfolioKimiData'

const router = useRouter()
const timeRange = ref('1Y')
const hoveredSegment = ref<number | null>(null)
const timeRanges = ['1Y', '6M', '3M', '1M', 'YTD']
</script>

<template>
  <div class="kimi-page-light" style="padding-top: 40px">
    <!-- Back + Header -->
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn" style="margin-bottom: 24px" @click="router.push({ name: 'portfolios' })">
        ← BACK TO PORTFOLIOS
      </button>

      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0">科技成長型投資組合</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">US GROWTH PORTFOLIO</span>
        </div>
        <div style="display: flex; gap: 8px">
          <span class="kimi-tag kimi-tag-accent">Growth</span>
          <span class="kimi-caption" style="align-self: center">Last updated: 2026-06-03</span>
        </div>
      </div>

      <!-- KPI Cards -->
      <ScrollReveal>
        <div class="kimi-section">
          <div class="kimi-kpi-grid">
            <div v-for="(kpi, i) in kpiData" :key="i" class="kimi-kpi-cell">
              <span class="kimi-caption" style="margin-bottom: 8px">{{ kpi.label }}</span>
              <span class="kimi-data">{{ kpi.value }}</span>
              <span style="font-size: 12px; color: var(--kimi-muted); margin-top: 4px">{{ kpi.sub }}</span>
              <div class="accent-bar" style="background-color: var(--kimi-accent-orange)" />
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Portfolio Value Trend -->
      <ScrollReveal :delay="0.1" style="margin-top: 60px">
        <div class="kimi-section">
          <div style="display: flex; align-items: center; justify-content: space-between; padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <div>
              <h2 style="margin: 0; font-size: 20px; font-weight: 600">投資組合價值走勢</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">PORTFOLIO VALUE TREND</span>
              <span class="kimi-caption" style="display: block">2024.01 — 2025.12</span>
            </div>
            <div class="kimi-time-range">
              <button
                v-for="r in timeRanges"
                :key="r"
                :class="['kimi-time-btn', timeRange === r && 'active']"
                @click="timeRange = r"
              >
                {{ r }}
              </button>
            </div>
          </div>
          <div style="padding: 20px">
            <LineChart
              :data="portfolioValueData.values"
              :labels="portfolioValueData.labels"
              :y-axis-labels="portfolioValueData.yAxisLabels"
              :height="400"
              line-color="#000000"
              :show-area="true"
            />
          </div>
          <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; padding: 20px; border-top: 1px solid var(--kimi-border-light)">
            <span style="font-size: 13px; color: var(--kimi-muted)">年初資產 {{ portfolioValueData.summary.start }}</span>
            <span style="font-size: 13px; color: var(--kimi-muted)">最高資產 {{ portfolioValueData.summary.high }}</span>
            <span style="font-size: 13px; color: var(--kimi-muted)">最低資產 {{ portfolioValueData.summary.low }}</span>
          </div>
        </div>
      </ScrollReveal>

      <!-- Asset Allocation -->
      <ScrollReveal :delay="0.15" style="margin-top: 60px">
        <div class="kimi-section" style="display: grid; grid-template-columns: 2fr 3fr">
          <!-- Donut -->
          <div style="display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 40px 20px; border-right: 1px solid var(--kimi-border-light)">
            <DonutChart
              :segments="allocationData.segments"
              center-label="NT$12.58M"
              center-sub-label="4 類資產"
              :active-index="hoveredSegment"
              @segment-hover="(i) => hoveredSegment = i"
            />
          </div>
          <!-- Holdings -->
          <div style="padding: 20px">
            <div style="margin-bottom: 16px">
              <h2 style="margin: 0; font-size: 20px; font-weight: 600">持倉明細</h2>
              <span class="kimi-caption">HOLDINGS</span>
            </div>
            <DataTable
              :headers="['代碼', '名稱', '類別', '持有股數', '現價', '市值', '占比', '損益']"
              :rows="allocationData.holdings.map((h) => [h.code, h.name, h.category, h.shares, h.price, h.value, h.ratio, h.pnl])"
              :highlight-row="hoveredSegment"
              @row-hover="(i) => hoveredSegment = i"
            />
          </div>
        </div>
      </ScrollReveal>

      <!-- Performance Attribution -->
      <div style="margin-top: 60px">
        <div style="margin-bottom: 24px">
          <h2 style="margin: 0; font-size: 20px; font-weight: 600">績效歸因分析</h2>
          <span class="kimi-caption">PERFORMANCE ATTRIBUTION</span>
        </div>
        <div class="kimi-grid-4">
          <!-- Sector Contribution -->
          <ScrollReveal :delay="0">
            <div class="kimi-panel">
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">產業別貢獻</h3>
              <BarChart
                :data="performanceAttribution.sector.map((s) => ({ label: s.label, value: s.value }))"
                :width="280"
              />
            </div>
          </ScrollReveal>

          <!-- Stock Alpha -->
          <ScrollReveal :delay="0.1">
            <div class="kimi-panel">
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">選股 Alpha</h3>
              <div>
                <div
                  v-for="(a, i) in performanceAttribution.alpha"
                  :key="i"
                  style="display: flex; justify-content: space-between; align-items: center; padding: 8px 0; border-bottom: 1px solid var(--kimi-border-light)"
                >
                  <span style="font-size: 14px">{{ a.name }}</span>
                  <span
                    style="font-size: 14px; font-weight: 500"
                    :style="{ color: a.value > 0 ? '#000000' : '#666666' }"
                  >
                    {{ a.value > 0 ? '+' : '' }}{{ a.value }}%
                  </span>
                </div>
              </div>
            </div>
          </ScrollReveal>

          <!-- Time-weighted return -->
          <ScrollReveal :delay="0.2">
            <div class="kimi-panel">
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">時間加權報酬</h3>
              <svg width="100%" height="120" viewBox="0 0 280 120">
                <line x1="10" y1="60" x2="270" y2="60" stroke="#E0E0E0" stroke-width="1" />
                <template v-for="(v, i) in performanceAttribution.monthlyReturns" :key="i">
                  <line
                    v-if="i > 0"
                    :x1="((i - 1) / 11) * 260 + 10"
                    :y1="60 - performanceAttribution.monthlyReturns[i - 1] * 8"
                    :x2="(i / 11) * 260 + 10"
                    :y2="60 - v * 8"
                    :stroke="v >= 0 ? '#000000' : '#999999'"
                    stroke-width="1.5"
                  />
                  <circle :cx="(i / 11) * 260 + 10" :cy="60 - v * 8" r="3" :fill="v >= 0 ? '#000000' : '#999999'" />
                </template>
              </svg>
            </div>
          </ScrollReveal>

          <!-- Risk Metrics -->
          <ScrollReveal :delay="0.3">
            <div class="kimi-panel">
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">風險指標</h3>
              <div>
                <div
                  v-for="(r, i) in [
                    { label: '波動率', value: performanceAttribution.risk.volatility },
                    { label: '最大回撤', value: performanceAttribution.risk.maxDrawdown },
                    { label: '索提諾比率', value: performanceAttribution.risk.sortino },
                    { label: '資訊比率', value: performanceAttribution.risk.infoRatio },
                  ]"
                  :key="i"
                  style="display: flex; justify-content: space-between; align-items: center; padding: 8px 0"
                >
                  <span class="kimi-caption">{{ r.label }}</span>
                  <span class="kimi-data-sm" style="font-size: 20px">{{ r.value }}</span>
                </div>
              </div>
            </div>
          </ScrollReveal>
        </div>
      </div>

      <!-- Risk Analysis -->
      <div class="kimi-grid-2" style="margin-top: 60px; border: 1px solid var(--kimi-border-light)">
        <ScrollReveal style="padding: 20px; border-right: 1px solid var(--kimi-border-light)">
          <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600">風險矩陣</h2>
          <ScatterPlot
            :data="riskData.scatter"
            x-axis-label="波動率（標準差）"
            y-axis-label="預期報酬率"
            :x-range="[0, 30]"
            :y-range="[-5, 25]"
            :frontier-curve="[[5, 2], [8, 5], [10, 7], [12, 9], [15, 11], [18, 13], [22, 15], [25, 16]]"
          />
        </ScrollReveal>

        <ScrollReveal :delay="0.1" style="padding: 20px">
          <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600">歷史回撤</h2>
          <svg width="100%" height="250" viewBox="0 0 500 250">
            <!-- Area -->
            <polygon
              :points="`60,${20 + (1 - (-8.2) / 15) * 200} ${riskData.drawdown.values.map((v, i) => {
                const x = 60 + (i / 11) * 420
                const y = 20 + (1 - v / 15) * 200
                return `${x},${y}`
              }).join(' ')} 480,${20 + 200}`"
              fill="rgba(0,0,0,0.06)"
            />
            <!-- Line -->
            <polyline
              :points="riskData.drawdown.values.map((v, i) => {
                const x = 60 + (i / 11) * 420
                const y = 20 + (1 - v / 15) * 200
                return `${x},${y}`
              }).join(' ')"
              fill="none"
              stroke="#000000"
              stroke-width="1.5"
            />
            <!-- Max drawdown line -->
            <line
              :x1="60 + (2 / 11) * 420"
              :y1="20 + (1 - (-8.2) / 15) * 200"
              :x2="60 + (2 / 11) * 420"
              :y2="220"
              stroke="#000000"
              stroke-width="1"
              stroke-dasharray="4 4"
            />
            <text
              :x="60 + (2 / 11) * 420 + 5"
              :y="20 + (1 - (-8.2) / 15) * 200 - 5"
              fill="#000000"
              font-size="11"
              font-weight="600"
            >
              -8.2%
            </text>
            <!-- Y labels -->
            <text v-for="(v, i) in [0, -5, -10, -15]" :key="'y-' + i"
              x="55"
              :y="20 + (1 - v / 15) * 200 + 4"
              text-anchor="end"
              fill="#666666"
              font-size="10"
            >
              {{ v }}%
            </text>
            <!-- X labels -->
            <text v-for="(l, i) in riskData.drawdown.labels" :key="'x-' + i"
              :x="60 + (i / 11) * 420"
              y="240"
              text-anchor="middle"
              fill="#666666"
              font-size="9"
            >
              {{ l }}
            </text>
          </svg>
        </ScrollReveal>
      </div>

      <!-- Scenario Simulation -->
      <ScrollReveal :delay="0.1" style="margin-top: 60px">
        <div class="kimi-section">
          <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">情境模擬</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">SCENARIO SIMULATION</span>
          </div>
          <div style="overflow-x: auto">
            <table class="kimi-table kimi-table-light">
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
                  <td style="font-weight: 600">{{ s.name }}</td>
                  <td style="font-weight: 600; color: #f87171">{{ s.impact }}</td>
                  <td>
                    <span class="kimi-tag">{{ s.probability }}</span>
                  </td>
                  <td style="color: var(--kimi-muted)">{{ s.description }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </ScrollReveal>

      <div style="height: 80px" />
    </div>

    <Footer label="PORTFOLIO" />
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
  .kimi-section > div[style*="grid-template-columns: 2fr 3fr"] {
    display: block;
  }
}
</style>
