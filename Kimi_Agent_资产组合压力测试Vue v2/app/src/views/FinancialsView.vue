<template>
  <div class="min-h-screen" style="background-color: #0A0A0A">
    <div class="relative">
      <ParticleCanvas theme="red" />
      <div class="relative" style="z-index: 1">
        <NavigationHeader :is-dark="true" />
      </div>
    </div>

    <div class="page-padding relative" style="margin-top: -80px; z-index: 2">
      <!-- KPI -->
      <ScrollReveal>
        <div class="w-full border-t" style="border-color: #333333">
          <div class="grid grid-cols-2 lg:grid-cols-5">
            <div v-for="(kpi, i) in financialKPIData" :key="i"
              class="flex flex-col justify-center items-center py-6 group relative cursor-default"
              :style="{ borderLeft: i===0 ? 'none' : '1px solid #333333', borderRight: '1px solid #333333', borderBottom: '1px solid #333333', height: '130px', backgroundColor: '#0A0A0A' }"
            >
              <span class="text-caption uppercase mb-2" style="color: #666666">{{ kpi.label }}</span>
              <span class="text-data" style="color: #FFFFFF">{{ kpi.value }}</span>
              <span class="text-xs mt-1" :style="{ color: kpi.positive ? '#8B1A2B' : '#666666' }">{{ kpi.sub }}</span>
              <div class="absolute left-0 top-0 bottom-0 w-0 group-hover:w-0.5 transition-all duration-300" style="background-color: #8B1A2B" />
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Revenue & Profit -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #333333; backgroundColor: #0A0A0A">
          <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between p-5 border-b" style="border-color: #333333">
            <div>
              <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">營收與獲利趨勢</h2>
              <span class="text-caption block mt-1" style="color: #666666">REVENUE & PROFIT TREND</span>
            </div>
            <div class="flex gap-3 mt-4 sm:mt-0">
              <span class="text-xs cursor-pointer" style="color: #FFFFFF">百萬</span>
              <span class="text-xs cursor-pointer" style="color: #666666">十億</span>
            </div>
          </div>
          <div class="p-5">
            <svg width="100%" height="420" viewBox="0 0 900 420" class="w-full">
              <line v-for="i in 6" :key="`g-${i}`"
                :x1="80" :y1="40 + ((i-1)/5)*320" :x2="860" :y2="40 + ((i-1)/5)*320"
                stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
              <text v-for="(l, i) in ['100B','80B','60B','40B','20B','0']" :key="`y-${i}`"
                x="75" :y="40 + (i/5)*320 + 4" text-anchor="end" fill="#666666" font-size="11">{{ l }}</text>
              <g v-for="(d, i) in revCombined" :key="`bg-${i}`">
                <rect v-if="d.r23" class="bar-anim" :x="80 + (i/12)*780 - 16" :y="360 - (d.r23/100)*320" width="16" :height="(d.r23/100)*320" fill="#333333" />
                <rect v-if="d.r24" class="bar-anim" :x="80 + (i/12)*780" :y="360 - (d.r24/100)*320" width="16" :height="(d.r24/100)*320" fill="#666666" />
                <rect v-if="d.r25" class="bar-anim" :x="80 + (i/12)*780 + 18" :y="360 - (d.r25/100)*320" width="16" :height="(d.r25/100)*320" fill="#FFFFFF" />
                <text :x="80 + (i/12)*780" y="385" text-anchor="middle" fill="#666666" font-size="10">{{ d.q }}</text>
              </g>
              <polyline :points="revCombined.map((d,i) => `${i===0?'M':'L'} ${80 + (i/12)*780} ${360 - (d.ni/100)*320}`).join(' ')"
                fill="none" stroke="#8B1A2B" stroke-width="2" />
              <circle v-for="(d, i) in revCombined" :key="'ni-'+i" :cx="80 + (i/12)*780" :cy="360 - (d.ni/100)*320" r="4" fill="#8B1A2B" />
              <g transform="translate(350, 410)">
                <rect x="0" y="-8" width="12" height="10" fill="#333333" />
                <text x="18" y="0" fill="#666666" font-size="10">2023營收</text>
                <rect x="90" y="-8" width="12" height="10" fill="#666666" />
                <text x="108" y="0" fill="#666666" font-size="10">2024營收</text>
                <rect x="180" y="-8" width="12" height="10" fill="#FFFFFF" />
                <text x="198" y="0" fill="#666666" font-size="10">2025營收</text>
                <circle cx="285" cy="-3" r="4" fill="#8B1A2B" />
                <text x="295" y="0" fill="#666666" font-size="10">淨利</text>
              </g>
            </svg>
          </div>
        </div>
      </ScrollReveal>

      <!-- Profitability -->
      <div class="mt-20 grid grid-cols-1 md:grid-cols-3 gap-3">
        <ScrollReveal v-for="(card, i) in profitCards" :key="i" :delay="i * 0.15">
          <div class="border p-5" style="border-color: #333333; background-color: #0A0A0A">
            <h3 class="font-heading text-lg font-medium mb-4" style="color: #FFFFFF">{{ card.title }}</h3>
            <svg width="100%" :height="160" :viewBox="`0 0 300 160`" class="w-full">
              <polyline :points="card.data.map((v: number, i: number) => `${20 + (i/11)*260},${140 - ((v - card.base) / card.range) * 120}`).join(' ')"
                fill="none" :stroke="card.color" stroke-width="2" />
              <circle v-for="(v, idx) in card.data" :key="idx"
                :cx="20 + (idx/11)*260" :cy="140 - ((v - card.base) / card.range) * 120" r="3" :fill="card.color" />
              <text x="280" :y="140 - ((card.data[card.data.length-1] - card.base) / card.range) * 120 + 4"
                text-anchor="end" fill="#FFFFFF" font-size="14" font-weight="600">{{ card.data[card.data.length-1] }}%</text>
            </svg>
          </div>
        </ScrollReveal>
      </div>

      <!-- Cash Flow -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">現金流量分析</h2>
            <span class="text-caption block mt-1" style="color: #666666">CASH FLOW STATEMENT</span>
          </div>
          <div class="p-5">
            <StackedBarChart :data="cashFlowData.labels.map((label, i) => ({ label, values: [cashFlowData.operating[i], cashFlowData.investing[i], cashFlowData.financing[i]] }))"
              :colors="['#FFFFFF', '#666666', '#333333']"
              :labels="['營業活動', '投資活動', '籌資活動', '自由現金流']"
              :y-axis-labels="['-10B', '0', '10B', '20B', '30B']"
              :line-data="cashFlowData.freeCashFlow" />
          </div>
        </div>
      </ScrollReveal>

      <!-- Radar + Ratio -->
      <div class="mt-20 border grid grid-cols-1 lg:grid-cols-2 gap-0" style="border-color: #333333">
        <ScrollReveal class="p-5 flex flex-col items-center" style="border-right: 1px solid #333333">
          <h2 class="font-heading text-2xl font-semibold mb-6" style="color: #FFFFFF">財務健康度</h2>
          <RadarChart :dimensions="radarData" />
        </ScrollReveal>
        <ScrollReveal class="p-5">
          <h2 class="font-heading text-2xl font-semibold mb-4" style="color: #FFFFFF">關鍵財務比率</h2>
          <DataTable :headers="['比率名稱','當期','上期','變動','趨勢']"
            :rows="ratioTableData.map(r => [r.name, r.current, r.prev, r.change, r.trend === 'up' ? '↑' : '↓'])"
            :dark-mode="true" :compact="true" />
        </ScrollReveal>
      </div>

      <!-- Balance Sheet -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">資產負債結構</h2>
            <span class="text-caption block mt-1" style="color: #666666">BALANCE SHEET OVERVIEW</span>
          </div>
          <div class="grid grid-cols-1 lg:grid-cols-2">
            <div class="p-5" style="border-right: 1px solid #333333">
              <h3 class="text-sm font-medium mb-4" style="color: #FFFFFF">資產結構</h3>
              <div class="flex h-10 w-full mb-4">
                <div class="h-full" style="width: 55%; background-color: #FFFFFF" />
                <div class="h-full" style="width: 30%; background-color: #666666" />
                <div class="h-full" style="width: 15%; background-color: #333333" />
              </div>
              <div v-for="(a, i) in balanceSheetData.assets" :key="i" class="flex items-center gap-2">
                <div class="w-3 h-3" :style="{ backgroundColor: a.color }" />
                <span class="text-sm flex-1" :style="{ color: a.color === '#FFFFFF' ? '#FFFFFF' : '#999999' }">{{ a.label }}</span>
                <span class="text-sm" style="color: #FFFFFF">{{ a.value }}</span>
              </div>
            </div>
            <div class="p-5">
              <h3 class="text-sm font-medium mb-4" style="color: #FFFFFF">負債與權益</h3>
              <div class="flex h-10 w-full mb-4">
                <div class="h-full" style="width: 35%; background-color: #666666" />
                <div class="h-full" style="width: 25%; background-color: #333333" />
                <div class="h-full" style="width: 40%; background-color: #FFFFFF" />
              </div>
              <div v-for="(l, i) in balanceSheetData.liabilities" :key="i" class="flex items-center gap-2">
                <div class="w-3 h-3" :style="{ backgroundColor: l.color }" />
                <span class="text-sm flex-1" :style="{ color: l.color === '#FFFFFF' ? '#FFFFFF' : '#999999' }">{{ l.label }}</span>
                <span class="text-sm" style="color: #FFFFFF">{{ l.value }}</span>
              </div>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- DuPont -->
      <ScrollReveal class="mt-20 mb-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">杜邦分析</h2>
            <span class="text-caption block mt-1" style="color: #666666">DUPONT ANALYSIS</span>
          </div>
          <div class="p-5">
            <div class="flex flex-col md:flex-row items-center justify-center gap-4 md:gap-8 mb-8">
              <div class="border p-5 text-center" style="border-color: #8B1A2B; min-width: 140px">
                <span class="text-caption block mb-1" style="color: #666666">ROE</span>
                <span class="text-data" style="color: #FFFFFF">{{ dupontData.roe }}</span>
              </div>
              <span class="text-2xl" style="color: #666666">=</span>
              <div class="flex flex-col sm:flex-row items-center gap-3">
                <div v-for="(f, i) in dupontFactors" :key="i" class="flex items-center gap-3">
                  <div class="border p-4 text-center" style="border-color: #333333; min-width: 100px">
                    <span class="text-caption block mb-1" style="color: #666666">{{ f.label }}</span>
                    <span class="text-lg font-medium" style="color: #FFFFFF">{{ f.value }}</span>
                  </div>
                  <span v-if="i < 2" class="text-lg" style="color: #666666">×</span>
                </div>
              </div>
            </div>
            <DataTable :headers="['指標','當期','同期業平均','差異']"
              :rows="dupontData.table.map(r => [r.metric, r.current, r.industry, r.diff])"
              :dark-mode="true" />
          </div>
        </div>
      </ScrollReveal>
      <div class="h-10" />
    </div>
    <Footer />
  </div>
</template>

<script setup lang="ts">
import NavigationHeader from '@/components/NavigationHeader.vue'
import Footer from '@/components/Footer.vue'
import ScrollReveal from '@/components/ScrollReveal.vue'
import DataTable from '@/components/DataTable.vue'
import StackedBarChart from '@/components/StackedBarChart.vue'
import RadarChart from '@/components/RadarChart.vue'
import ParticleCanvas from '@/components/ParticleCanvas.vue'
import {
  financialKPIData, profitabilityData, cashFlowData, radarData,
  ratioTableData, balanceSheetData, dupontData, revCombined
} from '@/data/financialsData'

const profitCards = [
  { title: '毛利率趨勢', data: profitabilityData.grossMargin, base: 35, range: 10, color: '#FFFFFF' },
  { title: '營業利益率', data: profitabilityData.operatingMargin, base: 10, range: 10, color: '#8B1A2B' },
  { title: '淨利率', data: profitabilityData.netMargin, base: 15, range: 10, color: '#666666' },
]

const dupontFactors = [
  { label: '淨利率', value: dupontData.netMargin },
  { label: '資產周轉率', value: dupontData.assetTurnover },
  { label: '權益乘數', value: dupontData.equityMultiplier },
]
</script>
