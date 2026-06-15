<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import LineChart from '../../components/kimi/LineChart.vue'
import DonutChart from '../../components/kimi/DonutChart.vue'
import BarChart from '../../components/kimi/BarChart.vue'
import DataTable from '../../components/kimi/DataTable.vue'
import ScatterPlot from '../../components/kimi/ScatterPlot.vue'
import Footer from '../../components/kimi/Footer.vue'
import {
  getKpiData,
  getAllocationData,
  getPerformanceAttribution,
  getRiskData,
  getScenarioData,
} from '../../data/portfolioKimiData'

const router = useRouter()
const { t } = useI18n()
const timeRange = ref('1Y')
const hoveredSegment = ref<number | null>(null)
const timeRanges = ['1Y', '6M', '3M', '1M', 'YTD']

const displayKpiData = computed(() => getKpiData(t))

const displayAllocationData = computed(() => getAllocationData(t))
const displayAllocationSegments = computed(() => displayAllocationData.value.segments)

const displayPerformanceData = computed(() => getPerformanceAttribution(t))
const displaySectorData = computed(() => displayPerformanceData.value.sector)
const displayAlphaData = computed(() => displayPerformanceData.value.alpha)

const displayScenarioData = computed(() => getScenarioData(t))
const displayRiskData = computed(() => getRiskData(t))

const holdingsHeaders = computed(() => [
  t('data.holdings.code'),
  t('data.holdings.name'),
  t('data.holdings.category'),
  t('data.holdings.shares'),
  t('data.holdings.price'),
  t('data.holdings.value'),
  t('data.holdings.ratio'),
  t('data.holdings.pnl'),
])

const riskMetrics = computed(() => [
  { label: t('data.risk.volatility'), value: displayPerformanceData.value.risk.volatility },
  { label: t('data.risk.maxDrawdown'), value: displayPerformanceData.value.risk.maxDrawdown },
  { label: t('data.risk.sortino'), value: displayPerformanceData.value.risk.sortino },
  { label: t('data.risk.infoRatio'), value: displayPerformanceData.value.risk.infoRatio },
])

const portfolioSummaryLabels = computed(() => ({
  start: t('portfolios.detail.startValue'),
  high: t('data.performance.highAsset'),
  low: t('data.performance.lowAsset'),
}))

const donutCenterLabels = computed(() => ({
  value: 'NT$12.58M',
  sub: t('data.allocation.assetCount'),
}))

const scenarioHeaders = computed(() => [
  t('data.scenario.name'),
  t('data.scenario.impact'),
  t('data.scenario.probability'),
  t('data.scenario.descriptionHeader'),
])

const monthKeys = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec']

const monthLabels = computed(() =>
  monthKeys.map((k) => t(`data.months.${k}`))
)

const drawdownMonthLabels = computed(() =>
  monthKeys.map((k) => t(`data.months.${k}`))
)
</script>

<template>
  <div class="kimi-page-light" style="padding-top: 40px">
    <!-- Back + Header -->
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn" style="margin-bottom: 24px" @click="router.push({ name: 'portfolios' })">
        ← {{ t('portfolios.detail.back') }}
      </button>

      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0">{{ t('portfolios.detail.title') }}</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('portfolios.detail.subtitle') }}</span>
        </div>
        <div style="display: flex; gap: 8px">
          <span class="kimi-tag kimi-tag-accent">Growth</span>
          <span class="kimi-caption" style="align-self: center">{{ t('portfolios.detail.lastUpdated') }}: 2026-06-03</span>
        </div>
      </div>

      <!-- KPI Cards -->
      <ScrollReveal>
        <div class="kimi-section">
          <div class="kimi-kpi-grid">
            <div v-for="(kpi, i) in displayKpiData" :key="i" class="kimi-kpi-cell">
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
              <h2 style="margin: 0; font-size: 20px; font-weight: 600">{{ t('portfolios.detail.valueTrend') }}</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('portfolios.detail.valueTrendEn') }}</span>
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
              :data="[10580000, 10720000, 10200000, 10850000, 11020000, 11180000, 11500000, 12850000, 12030000, 11890000, 12250000, 12580000]"
              :labels="monthLabels"
              :y-axis-labels="['NT$8M', 'NT$9M', 'NT$10M', 'NT$11M', 'NT$12M', 'NT$13M', 'NT$14M']"
              :height="400"
              line-color="#000000"
              :show-area="true"
            />
          </div>
          <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; padding: 20px; border-top: 1px solid var(--kimi-border-light)">
            <span style="font-size: 13px; color: var(--kimi-muted)">{{ portfolioSummaryLabels.start }} NT$10,580,000</span>
            <span style="font-size: 13px; color: var(--kimi-muted)">{{ portfolioSummaryLabels.high }} NT$12,850,000 (2025.08)</span>
            <span style="font-size: 13px; color: var(--kimi-muted)">{{ portfolioSummaryLabels.low }} NT$10,200,000 (2024.03)</span>
          </div>
        </div>
      </ScrollReveal>

      <!-- Asset Allocation -->
      <ScrollReveal :delay="0.15" style="margin-top: 60px">
        <div class="kimi-section" style="display: grid; grid-template-columns: 2fr 3fr">
          <!-- Donut -->
          <div style="display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 40px 20px; border-right: 1px solid var(--kimi-border-light)">
            <DonutChart
              :segments="displayAllocationSegments"
              :center-label="donutCenterLabels.value"
              :center-sub-label="donutCenterLabels.sub"
              :active-index="hoveredSegment"
              @segment-hover="(i: number | null) => hoveredSegment = i"
            />
          </div>
          <!-- Holdings -->
          <div style="padding: 20px">
            <div style="margin-bottom: 16px">
              <h2 style="margin: 0; font-size: 20px; font-weight: 600">{{ t('portfolios.detail.holdingsTitle') }}</h2>
              <span class="kimi-caption">HOLDINGS</span>
            </div>
            <DataTable
              :headers="holdingsHeaders"
              :rows="displayAllocationData.holdings.map((h: any) => [h.code, h.name, h.category, h.shares, h.price, h.value, h.ratio, h.pnl])"
              :highlight-row="hoveredSegment"
              @row-hover="(i: number | null) => hoveredSegment = i"
            />
          </div>
        </div>
      </ScrollReveal>

      <!-- Performance Attribution -->
      <div style="margin-top: 60px">
        <div style="margin-bottom: 24px">
          <h2 style="margin: 0; font-size: 20px; font-weight: 600">{{ t('portfolios.detail.performanceTitle') }}</h2>
          <span class="kimi-caption">PERFORMANCE ATTRIBUTION</span>
        </div>
        <div class="kimi-grid-4">
          <!-- Sector Contribution -->
          <ScrollReveal :delay="0">
            <div class="kimi-panel">
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">{{ t('portfolios.detail.sectorContribution') }}</h3>
              <BarChart
                :data="displaySectorData"
                :width="280"
              />
            </div>
          </ScrollReveal>

          <!-- Stock Alpha -->
          <ScrollReveal :delay="0.1">
            <div class="kimi-panel">
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">{{ t('portfolios.detail.stockAlpha') }}</h3>
              <div>
                <div
                  v-for="(a, i) in displayAlphaData"
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
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">{{ t('portfolios.detail.timeWeightedReturn') }}</h3>
              <svg width="100%" height="120" viewBox="0 0 280 120">
                <line x1="10" y1="60" x2="270" y2="60" stroke="#E0E0E0" stroke-width="1" />
                <template v-for="(v, i) in displayPerformanceData.monthlyReturns" :key="i">
                   <line
                     v-if="i > 0"
                     :x1="((i - 1) / 11) * 260 + 10"
                     :y1="60 - displayPerformanceData.monthlyReturns[i - 1] * 8"
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
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">{{ t('portfolios.detail.riskMetrics') }}</h3>
              <div>
                <div
                  v-for="(r, i) in riskMetrics"
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
          <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600">{{ t('portfolios.detail.riskMatrix') }}</h2>
           <ScatterPlot
             :data="displayRiskData.scatter"
            :x-axis-label="t('data.risk.volatilityAxis')"
            :y-axis-label="t('data.risk.expectedReturn')"
            :x-range="[0, 30]"
            :y-range="[-5, 25]"
            :frontier-curve="[[5, 2], [8, 5], [10, 7], [12, 9], [15, 11], [18, 13], [22, 15], [25, 16]]"
          />
        </ScrollReveal>

        <ScrollReveal :delay="0.1" style="padding: 20px">
          <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600">{{ t('portfolios.detail.historicalDrawdown') }}</h2>
          <svg width="100%" height="250" viewBox="0 0 500 250">
            <!-- Area -->
             <polygon
               :points="`60,${20 + (1 - (-8.2) / 15) * 200} ${displayRiskData.drawdown.values.map((v: number, i: number) => {
                 const x = 60 + (i / 11) * 420
                 const y = 20 + (1 - v / 15) * 200
                 return `${x},${y}`
               }).join(' ')} 480,${20 + 200}`"
              fill="rgba(0,0,0,0.06)"
            />
            <!-- Line -->
             <polyline
               :points="displayRiskData.drawdown.values.map((v: number, i: number) => {
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
            <text v-for="(l, i) in drawdownMonthLabels" :key="'x-' + i"
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
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">{{ t('portfolios.detail.scenarioTitle') }}</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">SCENARIO SIMULATION</span>
          </div>
          <div style="overflow-x: auto">
            <table class="kimi-table kimi-table-light">
              <thead>
                <tr>
                  <th v-for="(h, i) in scenarioHeaders" :key="i">{{ h }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="s in displayScenarioData" :key="s.name">
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
