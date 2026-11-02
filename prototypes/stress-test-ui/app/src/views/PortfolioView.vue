<template>
  <div class="min-h-screen" style="background-color: #F5F5F5">
    <div class="relative">
      <ParticleCanvas theme="warm" />
      <div class="relative" style="z-index: 1">
        <NavigationHeader :is-dark="false" />
      </div>
    </div>

    <div class="page-padding relative" style="margin-top: -80px; z-index: 2">
      <!-- KPI -->
      <ScrollReveal>
        <div class="w-full border-t" style="border-color: #E0E0E0">
          <div class="grid grid-cols-2 lg:grid-cols-4">
            <div v-for="(kpi, i) in kpiData" :key="i"
              class="flex flex-col justify-center items-center py-6 transition-all duration-300 hover:scale-[1.02] group relative cursor-default"
              :style="{ borderLeft: i===0 ? 'none' : '1px solid #E0E0E0', borderRight: '1px solid #E0E0E0', borderBottom: '1px solid #E0E0E0', height: '120px' }"
            >
              <span class="text-caption uppercase mb-2" style="color: #666666">{{ kpi.label }}</span>
              <span class="text-data" style="color: #000000">{{ kpi.value }}</span>
              <span class="text-xs mt-1" style="color: #666666">{{ kpi.sub }}</span>
              <div class="absolute left-0 top-0 bottom-0 w-0 group-hover:w-0.5 transition-all duration-300" style="background-color: #FF6B00" />
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Portfolio Value Trend -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #E0E0E0">
          <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between p-5 border-b" style="border-color: #E0E0E0">
            <div>
              <h2 class="font-heading text-2xl font-semibold" style="color: #000000">投資組合價值走勢</h2>
              <span class="text-caption block mt-1" style="color: #666666">PORTFOLIO VALUE TREND</span>
              <span class="text-caption block mt-0.5" style="color: #666666">2024.01 — 2025.12</span>
            </div>
            <div class="flex gap-2 mt-4 sm:mt-0">
              <button v-for="r in timeRanges" :key="r" @click="timeRange = r"
                class="font-mono text-xs px-3 py-1 transition-all duration-200"
                :style="{ border: `1px solid ${timeRange===r ? '#000000' : '#E0E0E0'}`, backgroundColor: timeRange===r ? '#000000' : 'transparent', color: timeRange===r ? '#FFFFFF' : '#666666' }">
                {{ r }}
              </button>
            </div>
          </div>
          <div class="p-5">
            <LineChart :data="portfolioValueData.values" :labels="portfolioValueData.labels"
              :y-axis-labels="portfolioValueData.yAxisLabels" :height="400" line-color="#000000" :show-area="true" />
          </div>
          <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 p-5 border-t" style="border-color: #E0E0E0">
            <span class="text-sm" style="color: #666666">年初資產 {{ portfolioValueData.summary.start }}</span>
            <span class="text-sm" style="color: #666666">最高資產 {{ portfolioValueData.summary.high }}</span>
            <span class="text-sm" style="color: #666666">最低資產 {{ portfolioValueData.summary.low }}</span>
          </div>
        </div>
      </ScrollReveal>

      <!-- Asset Allocation -->
      <ScrollReveal class="mt-20">
        <div class="w-full border grid grid-cols-1 lg:grid-cols-5" style="border-color: #E0E0E0">
          <div class="lg:col-span-2 flex flex-col items-center justify-center py-10" style="border-right: 1px solid #E0E0E0">
            <DonutChart :segments="allocationData.segments" center-label="NT$12.58M" center-sub-label="4 類資產"
              :active-index="hoveredSegment" @segment-hover="hoveredSegment = $event" />
          </div>
          <div class="lg:col-span-3 p-5">
            <div class="mb-4">
              <h2 class="font-heading text-2xl font-semibold" style="color: #000000">持倉明細</h2>
              <span class="text-caption" style="color: #666666">HOLDINGS</span>
            </div>
            <DataTable :headers="['代碼','名稱','類別','持有股數','現價','市值','占比','損益']"
              :rows="allocationData.holdings.map(h => [h.code, h.name, h.category, h.shares, h.price, h.value, h.ratio, h.pnl])"
              :highlight-row="hoveredSegment" :on-row-hover="(idx) => hoveredSegment = idx" />
          </div>
        </div>
      </ScrollReveal>

      <!-- Performance Attribution -->
      <div class="mt-20">
        <ScrollReveal>
          <div class="mb-6">
            <h2 class="font-heading text-2xl font-semibold" style="color: #000000">績效歸因分析</h2>
            <span class="text-caption" style="color: #666666">PERFORMANCE ATTRIBUTION</span>
          </div>
        </ScrollReveal>
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
          <ScrollReveal v-for="(card, i) in perfCards" :key="i" :delay="i * 0.15">
            <div class="border p-5 h-full" style="border-color: #E0E0E0">
              <h3 class="font-heading text-lg font-medium mb-4" style="color: #000000">{{ card.title }}</h3>
              <component :is="card.component" v-bind="card.props" />
            </div>
          </ScrollReveal>
        </div>
      </div>

      <!-- Risk Analysis -->
      <div class="mt-20 border grid grid-cols-1 lg:grid-cols-2 gap-0" style="border-color: #E0E0E0">
        <ScrollReveal class="p-5" style="border-right: 1px solid #E0E0E0">
          <h2 class="font-heading text-2xl font-semibold mb-4" style="color: #000000">風險矩陣</h2>
          <ScatterPlot :data="riskScatterData" x-axis-label="波動率（標準差）" y-axis-label="預期報酬率"
            :x-range="[0, 30]" :y-range="[-5, 25]" :frontier-curve="[[5,2],[8,5],[10,7],[12,9],[15,11],[18,13],[22,15],[25,16]]" />
        </ScrollReveal>
        <ScrollReveal class="p-5">
          <h2 class="font-heading text-2xl font-semibold mb-4" style="color: #000000">歷史回撤</h2>
          <svg width="100%" height="250" viewBox="0 0 500 250">
            <polygon :points="drawdownAreaPoints" fill="rgba(0,0,0,0.06)" />
            <polyline :points="drawdownLinePoints" fill="none" stroke="#000000" stroke-width="1.5" />
            <line :x1="60 + (2/11)*420" :y1="20 + (1 - (-8.2)/15)*200" :x2="60 + (2/11)*420" :y2="220"
              stroke="#000000" stroke-width="1" stroke-dasharray="4 4" />
            <text :x="60 + (2/11)*420 + 5" :y="20 + (1 - (-8.2)/15)*200 - 5" fill="#000000" font-size="11" font-weight="600">-8.2%</text>
            <text v-for="v in [0,-5,-10,-15]" :key="v" x="55" :y="20 + (1 - v/15)*200 + 4" text-anchor="end" fill="#666666" font-size="10">{{ v }}%</text>
            <text v-for="(l, i) in drawdownData.labels" :key="l" :x="60 + (i/11)*420" y="240" text-anchor="middle" fill="#666666" font-size="9">{{ l }}</text>
          </svg>
        </ScrollReveal>
      </div>
      <div class="h-20" />
    </div>
    <Footer />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, h } from 'vue'
import NavigationHeader from '@/components/NavigationHeader.vue'
import Footer from '@/components/Footer.vue'
import ScrollReveal from '@/components/ScrollReveal.vue'
import LineChart from '@/components/LineChart.vue'
import DonutChart from '@/components/DonutChart.vue'
import DataTable from '@/components/DataTable.vue'
import ScatterPlot from '@/components/ScatterPlot.vue'
import BarChart from '@/components/BarChart.vue'
import ParticleCanvas from '@/components/ParticleCanvas.vue'
import {
  kpiData, portfolioValueData, allocationData, performanceAttribution, riskScatterData, drawdownData
} from '@/data/portfolioData'

const timeRange = ref('1Y')
const timeRanges = ['1Y', '6M', '3M', '1M', 'YTD']
const hoveredSegment = ref<number | null>(null)

const drawdownAreaPoints = computed(() => {
  const pts = drawdownData.values.map((v, i) => `${60 + (i/11)*420},${20 + (1 - v/15)*200}`)
  return `${60},${20 + (1 - (-8.2)/15)*200} ${pts.join(' ')} 480,${20 + 200}`
})
const drawdownLinePoints = computed(() =>
  drawdownData.values.map((v, i) => `${60 + (i/11)*420},${20 + (1 - v/15)*200}`).join(' ')
)

const perfCards = [
  {
    title: '產業別貢獻',
    component: BarChart,
    props: { data: performanceAttribution.sector.map(s => ({ label: s.label, value: s.value, color: '#000000' })), width: 280 }
  },
  {
    title: '選股 Alpha',
    component: { setup() { return () => h('div', { class: 'flex flex-col' }, performanceAttribution.alpha.map((a, i) => h('div', {
      class: 'flex justify-between items-center py-2',
      style: { borderBottom: i < performanceAttribution.alpha.length - 1 ? '1px solid #E0E0E0' : 'none' }
    }, [h('span', { class: 'text-sm', style: { color: '#000000' } }, a.name), h('span', {
      class: 'text-sm font-medium', style: { color: a.value > 0 ? '#000000' : '#666666' }
    }, `${a.value > 0 ? '+' : ''}${a.value}%`)]))) } }
  },
  {
    title: '時間加權報酬',
    component: { setup() { return () => h('svg', { width: '100%', height: 120, viewBox: '0 0 280 120' }, [
      h('line', { x1: 10, y1: 60, x2: 270, y2: 60, stroke: '#E0E0E0', strokeWidth: 1 }),
      ...performanceAttribution.monthlyReturns.flatMap((v, i) => {
        const x = (i/11)*260 + 10, y = 60 - v*8
        const els = []
        if (i > 0) {
          const px = ((i-1)/11)*260 + 10, py = 60 - performanceAttribution.monthlyReturns[i-1]*8
          els.push(h('line', { x1: px, y1: py, x2: x, y2: y, stroke: v >= 0 ? '#000000' : '#999999', strokeWidth: 1.5 }))
        }
        els.push(h('circle', { cx: x, cy: y, r: 3, fill: v >= 0 ? '#000000' : '#999999' }))
        return els
      })
    ]) } }
  },
  {
    title: '風險指標',
    component: { setup() { return () => h('div', { class: 'flex flex-col gap-4' }, [
      { label: '波動率', value: performanceAttribution.risk.volatility },
      { label: '最大回撤', value: performanceAttribution.risk.maxDrawdown },
      { label: '索提諾比率', value: performanceAttribution.risk.sortino },
      { label: '資訊比率', value: performanceAttribution.risk.infoRatio },
    ].map(r => h('div', { class: 'flex justify-between items-center' }, [
      h('span', { class: 'text-caption uppercase', style: { color: '#666666' } }, r.label),
      h('span', { class: 'text-data', style: { color: '#000000', fontSize: '22px' } }, r.value)
    ]))) } }
  },
]
</script>
