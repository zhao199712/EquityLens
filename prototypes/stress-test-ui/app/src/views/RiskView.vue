<template>
  <div class="min-h-screen" style="background-color: #0A0A0A">
    <div class="relative">
      <ParticleCanvas theme="red" />
      <div class="relative" style="z-index: 1">
        <NavigationHeader :is-dark="true" />
      </div>
    </div>

    <div class="page-padding relative" style="margin-top: -80px; z-index: 2">
      <!-- KPI Cards -->
      <ScrollReveal>
        <div class="w-full border-t" style="border-color: #333333">
          <div class="grid grid-cols-2 lg:grid-cols-5">
            <div v-for="(kpi, i) in riskKPIData" :key="i"
              class="flex flex-col justify-center items-center py-6 group relative cursor-default"
              :style="{ borderLeft: i===0 ? 'none' : '1px solid #333333', borderRight: '1px solid #333333', borderBottom: '1px solid #333333', height: '130px', backgroundColor: '#0A0A0A' }"
            >
              <span class="text-caption uppercase mb-2" style="color: #666666">{{ kpi.label }}</span>
              <span class="text-data" :style="{ color: kpi.color }">{{ kpi.value }}</span>
              <span class="text-xs mt-1" style="color: #666666">{{ kpi.sub }}</span>
              <div class="absolute left-0 top-0 bottom-0 w-0 group-hover:w-0.5 transition-all duration-300" style="background-color: #8B1A2B" />
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- VaR Results Table -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">Value at Risk 分析</h2>
            <span class="text-caption block mt-1" style="color: #666666">VALUE AT RISK — 95% & 99% 信賴區間</span>
          </div>
          <div class="p-5">
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
              <!-- Table -->
              <div>
                <table class="w-full" style="border-collapse: collapse">
                  <thead>
                    <tr style="background-color: #333333">
                      <th class="text-caption uppercase text-left px-4" style="color: #FFFFFF; height: 44px; border-bottom: 1px solid #333333; font-size: 11px; letter-spacing: 0.1em">計算方法</th>
                      <th class="text-caption uppercase text-right px-4" style="color: #FFFFFF; height: 44px; font-size: 11px; letter-spacing: 0.1em">VaR 95%</th>
                      <th class="text-caption uppercase text-right px-4" style="color: #FFFFFF; height: 44px; font-size: 11px; letter-spacing: 0.1em">VaR 99%</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="(v, i) in varResults" :key="i" :style="{ backgroundColor: i % 2 === 0 ? '#0A0A0A' : '#111111', height: 48 }">
                      <td class="px-4 text-sm" style="color: #FFFFFF; border-bottom: 1px solid #333333">{{ v.method }}</td>
                      <td class="px-4 text-sm text-right font-mono" :style="{ color: v.var95 < -4.8 ? '#FF6B00' : '#FFFFFF', borderBottom: '1px solid #333333' }">{{ v.var95 }}%</td>
                      <td class="px-4 text-sm text-right font-mono" :style="{ color: '#8B1A2B', borderBottom: '1px solid #333333' }">{{ v.var99 }}%</td>
                    </tr>
                  </tbody>
                </table>
                <p class="text-xs mt-3" style="color: #666666">註：以上數值為單日 VaR，基於投資組合市值 NT$12,580,000 計算</p>
              </div>
              <!-- Histogram -->
              <div>
                <h3 class="text-sm font-medium mb-3" style="color: #FFFFFF">日報酬分布直方圖（250日）</h3>
                <svg width="100%" height="240" viewBox="0 0 400 240" class="w-full">
                  <!-- Grid -->
                  <line v-for="i in 5" :key="'g-'+i" x1="50" :y1="30 + (i-1)*40" x2="380" :y2="30 + (i-1)*40" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                  <text v-for="i in 5" :key="'gy-'+i" x="45" :y="30 + (i-1)*40 + 4" text-anchor="end" fill="#666666" font-size="10">{{ Math.round(50 - (i-1)*12) }}</text>
                  <!-- Bars -->
                  <g v-for="(bin, i) in varHistogram" :key="i">
                    <rect :x="55 + i*29" :y="210 - (bin.count/50)*180" width="24" :height="(bin.count/50)*180"
                      :fill="bin.isTail99 ? '#8B1A2B' : bin.isTail95 ? '#FF6B00' : '#333333'" class="transition-all" />
                    <text :x="55 + i*29 + 12" y="228" text-anchor="middle" fill="#666666" font-size="8">{{ bin.bin }}</text>
                  </g>
                  <!-- Legend -->
                  <g transform="translate(55, 12)">
                    <rect x="0" y="-6" width="10" height="8" fill="#333333" />
                    <text x="14" y="0" fill="#666666" font-size="9">正常區間</text>
                    <rect x="70" y="-6" width="10" height="8" fill="#FF6B00" />
                    <text x="84" y="0" fill="#666666" font-size="9">95% 尾部</text>
                    <rect x="145" y="-6" width="10" height="8" fill="#8B1A2B" />
                    <text x="159" y="0" fill="#666666" font-size="9">99% 尾部</text>
                  </g>
                </svg>
              </div>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- ES / CVaR Analysis -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">Expected Shortfall (ES) 分析</h2>
            <span class="text-caption block mt-1" style="color: #666666">CONDITIONAL VaR — 尾部損失期望值</span>
          </div>
          <div class="p-5">
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
              <!-- ES Table -->
              <div>
                <table class="w-full" style="border-collapse: collapse">
                  <thead>
                    <tr style="background-color: #333333">
                      <th class="text-caption uppercase text-left px-4" style="color: #FFFFFF; height: 44px; font-size: 11px; letter-spacing: 0.1em">計算方法</th>
                      <th class="text-caption uppercase text-right px-4" style="color: #FFFFFF; height: 44px; font-size: 11px; letter-spacing: 0.1em">ES 95%</th>
                      <th class="text-caption uppercase text-right px-4" style="color: #FFFFFF; height: 44px; font-size: 11px; letter-spacing: 0.1em">ES 99%</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="(e, i) in esResults" :key="i" :style="{ backgroundColor: i % 2 === 0 ? '#0A0A0A' : '#111111', height: 48 }">
                      <td class="px-4 text-sm" style="color: #FFFFFF; border-bottom: 1px solid #333333">{{ e.method }}</td>
                      <td class="px-4 text-sm text-right font-mono" style="color: #FF6B00; border-bottom: 1px solid #333333">{{ e.es95 }}%</td>
                      <td class="px-4 text-sm text-right font-mono" style="color: #8B1A2B; border-bottom: 1px solid #333333">{{ e.es99 }}%</td>
                    </tr>
                  </tbody>
                </table>
                <div class="mt-4 p-3 border" style="border-color: #333333">
                  <p class="text-xs" style="color: #666666">
                    ES（Expected Shortfall）衡量當損失超過 VaR 閾值時的<strong style="color: #FFFFFF">平均損失程度</strong>。
                    相較於 VaR 僅反映單一分位數點，ES 更完整地捕捉尾部風險，是巴塞爾協議 III 推薦的風險指標。
                  </p>
                </div>
              </div>
              <!-- VaR vs ES Comparison -->
              <div>
                <h3 class="text-sm font-medium mb-3" style="color: #FFFFFF">VaR vs ES 比較</h3>
                <svg width="100%" height="280" viewBox="0 0 400 280" class="w-full">
                  <line v-for="i in 6" :key="'g-'+i" x1="80" :y1="30 + (i-1)*40" x2="380" :y2="30 + (i-1)*40" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                  <text v-for="i in 6" :key="'y-'+i" x="75" :y="30 + (i-1)*40 + 4" text-anchor="end" fill="#666666" font-size="10">{{ -(i-1)*2 }}%</text>
                  <g v-for="(d, i) in varEsComparison" :key="i">
                    <!-- VaR bar -->
                    <rect :x="85 + i*58" :y="30 + ((-d.var)/12)*200" width="22" :height="(-d.var/12)*200" fill="#FF6B00" opacity="0.8" />
                    <!-- ES bar -->
                    <rect :x="110 + i*58" :y="30 + ((-d.es)/12)*200" width="22" :height="(-d.es/12)*200" fill="#8B1A2B" opacity="0.8" />
                    <text :x="85 + i*58 + 22" y="255" text-anchor="middle" fill="#666666" font-size="9">{{ d.confidence }}</text>
                  </g>
                  <g transform="translate(90, 270)">
                    <rect x="0" y="-6" width="12" height="8" fill="#FF6B00" opacity="0.8" />
                    <text x="16" y="0" fill="#666666" font-size="9">VaR</text>
                    <rect x="50" y="-6" width="12" height="8" fill="#8B1A2B" opacity="0.8" />
                    <text x="66" y="0" fill="#666666" font-size="9">ES</text>
                  </g>
                </svg>
              </div>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Stress Test Scenarios -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">壓力測試情境</h2>
            <span class="text-caption block mt-1" style="color: #666666">STRESS TESTING — 六大極端情境模擬</span>
          </div>
          <div class="p-5">
            <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              <div v-for="s in stressScenarios" :key="s.id"
                class="border p-5 cursor-pointer transition-all duration-300 hover:border-opacity-100"
                :class="selectedScenario === s.id ? 'border-opacity-100' : ''"
                :style="{ borderColor: selectedScenario === s.id ? '#8B1A2B' : '#333333', backgroundColor: selectedScenario === s.id ? '#111111' : '#0A0A0A' }"
                @click="selectedScenario = selectedScenario === s.id ? null : s.id"
              >
                <div class="flex items-center justify-between mb-3">
                  <span class="text-sm font-medium" style="color: #FFFFFF">{{ s.name }}</span>
                  <span class="text-caption px-2 py-0.5 border" :style="{ borderColor: severityColor(s.severity), color: severityColor(s.severity) }">{{ severityLabel(s.severity) }}</span>
                </div>
                <p class="text-xs mb-3" style="color: #666666">{{ s.description }}</p>
                <div class="flex items-baseline gap-2">
                  <span class="text-data" style="color: #8B1A2B; font-size: 22px">{{ s.impact }}%</span>
                  <span class="text-caption" style="color: #666666">投組衝擊</span>
                </div>
                <!-- Expanded details -->
                <div v-if="selectedScenario === s.id" class="mt-4 pt-4 border-t" style="border-color: #333333">
                  <div v-for="(d, i) in s.details" :key="i" class="flex justify-between items-center py-1.5">
                    <span class="text-xs" style="color: #666666">{{ d.metric }}</span>
                    <span class="text-xs font-mono" style="color: #FFFFFF">{{ d.value }}</span>
                  </div>
                  <div class="mt-3 flex flex-wrap gap-1">
                    <span v-for="sector in s.affectedSectors" :key="sector" class="text-caption px-2 py-0.5 border" style="border-color: #333333; color: #999999">{{ sector }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Backtest Results -->
      <ScrollReveal class="mt-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">VaR 回測驗證</h2>
            <span class="text-caption block mt-1" style="color: #666666">BACKTESTING — 例外事件統計</span>
          </div>
          <div class="p-5">
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
              <!-- Chart -->
              <svg width="100%" height="280" viewBox="0 0 500 280" class="w-full">
                <line v-for="i in 6" :key="'g-'+i" x1="50" :y1="30 + (i-1)*40" x2="480" :y2="30 + (i-1)*40" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                <text v-for="v in [5,0,-5,-10,-15,-20]" :key="v" x="45" :y="30 + ((5-v)/25)*40 + 4" text-anchor="end" fill="#666666" font-size="10">{{ v }}%</text>
                <!-- Zero line -->
                <line x1="50" y1="30 + (5/25)*200" x2="480" y2="30 + (5/25)*200" stroke="#666666" stroke-width="2" />
                <!-- VaR line -->
                <line x1="50" :y1="30 + ((5-(-4.82))/25)*200" x2="480" :y2="30 + ((5-(-4.82))/25)*200" stroke="#FF6B00" stroke-width="1" stroke-dasharray="4 4" />
                <text x="485" :y="30 + ((5-(-4.82))/25)*200 + 3" fill="#FF6B00" font-size="9">VaR 95%</text>
                <!-- Actual bars -->
                <g v-for="(d, i) in backtestData" :key="i">
                  <line :x1="55 + i*35" :y1="30 + ((5-0)/25)*200" :x2="55 + i*35" :y2="30 + ((5-d.actual)/25)*200"
                    :stroke="d.breached ? '#8B1A2B' : d.actual >= 0 ? '#FFFFFF' : '#666666'" stroke-width="4" />
                  <text v-if="d.breached" :x="55 + i*35" :y="30 + ((5-d.actual)/25)*200 - 6" text-anchor="middle" fill="#8B1A2B" font-size="8">!</text>
                </g>
                <!-- X labels -->
                <text v-for="(d, i) in backtestData" :key="'xl-'+i" :x="55 + i*35" y="260" text-anchor="middle" fill="#666666" font-size="8">{{ d.date.slice(5) }}</text>
              </svg>
              <!-- Stats -->
              <div class="flex flex-col justify-center">
                <div class="border p-5 mb-4" style="border-color: #333333">
                  <div class="grid grid-cols-2 gap-4">
                    <div>
                      <span class="text-caption block mb-1" style="color: #666666">觀察期間</span>
                      <span class="text-lg font-medium" style="color: #FFFFFF">250 交易日</span>
                    </div>
                    <div>
                      <span class="text-caption block mb-1" style="color: #666666">例外次數</span>
                      <span class="text-lg font-medium" style="color: #8B1A2B">12 次</span>
                    </div>
                    <div>
                      <span class="text-caption block mb-1" style="color: #666666">例外比率</span>
                      <span class="text-lg font-medium" style="color: #FF6B00">4.8%</span>
                    </div>
                    <div>
                      <span class="text-caption block mb-1" style="color: #666666">預期比率</span>
                      <span class="text-lg font-medium" style="color: #FFFFFF">5.0%</span>
                    </div>
                    <div>
                      <span class="text-caption block mb-1" style="color: #666666">Kupiec 檢定</span>
                      <span class="text-lg font-medium" style="color: #FFFFFF">p=0.82 通過</span>
                    </div>
                    <div>
                      <span class="text-caption block mb-1" style="color: #666666">Christoffersen</span>
                      <span class="text-lg font-medium" style="color: #FFFFFF">p=0.71 通過</span>
                    </div>
                  </div>
                </div>
                <p class="text-xs" style="color: #666666">
                  回測結果顯示實際例外比率（4.8%）接近理論預期（5%），
                  Kupiec 比例檢定與 Christoffersen 獨立性檢定均通過，
                  表示 VaR 模型在統計上是可靠的。
                </p>
              </div>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Monte Carlo Simulation -->
      <ScrollReveal class="mt-20 mb-20">
        <div class="w-full border" style="border-color: #333333; background-color: #0A0A0A">
          <div class="p-5 border-b" style="border-color: #333333">
            <h2 class="font-heading text-2xl font-semibold" style="color: #FFFFFF">蒙地卡羅模擬</h2>
            <span class="text-caption block mt-1" style="color: #666666">MONTE CARLO — 10,000 次路徑模擬</span>
          </div>
          <div class="p-5">
            <svg width="100%" height="400" viewBox="0 0 900 400" class="w-full">
              <!-- Grid -->
              <line v-for="i in 7" :key="'g-'+i" x1="70" :y1="30 + (i-1)*50" x2="850" :y2="30 + (i-1)*50" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
              <text v-for="l in ['+20%','+10%','0%','-10%','-20%','-30%','-40%']" :key="l" x="65" :y="30 + ({'+20%':0,'+10%':1,'0%':2,'-10%':3,'-20%':4,'-30%':5,'-40%':6}[l])*50 + 4" text-anchor="end" fill="#666666" font-size="10">{{ l }}</text>
              <!-- Percentile bands -->
              <polygon :points="mcP1" fill="#8B1A2B" fill-opacity="0.05" />
              <polygon :points="mcP5" fill="#8B1A2B" fill-opacity="0.08" />
              <!-- Median -->
              <polyline :points="mcPaths[10].map((v, i) => `${70 + (i/252)*780},${200 - v*400}`).join(' ')" fill="none" stroke="#FFFFFF" stroke-width="2" />
              <!-- Sample paths -->
              <polyline v-for="(path, idx) in displayedPaths" :key="idx"
                :points="path.map((v, i) => `${70 + (i/252)*780},${200 - v*400}`).join(' ')"
                fill="none" :stroke="['#333333','#444444','#555555','#666666'][idx % 4]" stroke-width="0.5" opacity="0.4" />
              <!-- Legend -->
              <g transform="translate(80, 18)">
                <line x1="0" y1="0" x2="20" y2="0" stroke="#FFFFFF" stroke-width="2" />
                <text x="25" y="4" fill="#FFFFFF" font-size="10">中位數路徑</text>
                <rect x="100" y="-6" width="16" height="10" fill="#8B1A2B" fill-opacity="0.15" />
                <text x="120" y="4" fill="#666666" font-size="10">99% 區間</text>
                <rect x="180" y="-6" width="16" height="10" fill="#8B1A2B" fill-opacity="0.1" />
                <text x="200" y="4" fill="#666666" font-size="10">95% 區間</text>
              </g>
            </svg>
            <div class="grid grid-cols-2 lg:grid-cols-5 gap-4 mt-5">
              <div v-for="stat in mcStats" :key="stat.label" class="border p-3 text-center" style="border-color: #333333">
                <span class="text-caption block mb-1" style="color: #666666">{{ stat.label }}</span>
                <span class="text-lg font-medium font-mono" :style="{ color: stat.color }">{{ stat.value }}</span>
              </div>
            </div>
          </div>
        </div>
      </ScrollReveal>
      <div class="h-10" />
    </div>
    <Footer />
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import NavigationHeader from '@/components/NavigationHeader.vue'
import Footer from '@/components/Footer.vue'
import ScrollReveal from '@/components/ScrollReveal.vue'
import ParticleCanvas from '@/components/ParticleCanvas.vue'
import {
  riskKPIData, varResults, varHistogram, esResults, varEsComparison,
  stressScenarios, backtestData, monteCarloPaths, monteCarloPercentiles
} from '@/data/riskData'
import type { StressScenario } from '@/data/riskData'

const selectedScenario = ref<string | null>(null)

function severityColor(s: StressScenario['severity']) {
  const map = { low: '#666666', medium: '#FF6B00', high: '#8B1A2B', extreme: '#8B1A2B' }
  return map[s]
}
function severityLabel(s: StressScenario['severity']) {
  const map = { low: '低', medium: '中', high: '高', extreme: '極高' }
  return map[s]
}

const displayedPaths = monteCarloPaths.slice(0, 8)

const mcP1 = computed(() => {
  const upper = monteCarloPercentiles.p99.map((v, i) => `${70 + (i/252)*780},${200 - v*400}`).join(' ')
  const lower = monteCarloPercentiles.p1.map((v, i) => `${70 + ((252-i)/252)*780},${200 - v*400}`).join(' ')
  return upper + ' ' + lower
})
const mcP5 = computed(() => {
  const upper = monteCarloPercentiles.p95.map((v, i) => `${70 + (i/252)*780},${200 - v*400}`).join(' ')
  const lower = monteCarloPercentiles.p5.map((v, i) => `${70 + ((252-i)/252)*780},${200 - v*400}`).join(' ')
  return upper + ' ' + lower
})

const mcStats = [
  { label: '一年勝率', value: '62.5%', color: '#FFFFFF' },
  { label: '期望報酬', value: '+8.2%', color: '#FFFFFF' },
  { label: '5% 最壞情境', value: '-28.5%', color: '#FF6B00' },
  { label: '1% 最壞情境', value: '-38.2%', color: '#8B1A2B' },
  { label: '路徑模擬次數', value: '10,000', color: '#666666' },
]
</script>
