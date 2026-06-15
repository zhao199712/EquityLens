<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import StackedBarChart from '../../components/kimi/StackedBarChart.vue'
import RadarChart from '../../components/kimi/RadarChart.vue'
import DataTable from '../../components/kimi/DataTable.vue'
import Footer from '../../components/kimi/Footer.vue'
import {
  profitabilityData,
  cashFlowData,
  revenueProfitData,
} from '../../data/financialsKimiData'

const { t } = useI18n()
const router = useRouter()

const activeTab = ref('memo')
const tabs = computed(() => [
  { key: 'memo', label: t('reports.detail.aiMemoEn') },
  { key: 'metrics', label: t('reports.detail.financialMetricsEn') },
  { key: 'citations', label: t('reports.detail.citationsEn') },
  { key: 'critic', label: t('reports.detail.criticNotesEn') },
  { key: 'impact', label: t('reports.detail.portfolioImpactEn') },
])

const localFinancialKPIData = computed(() => [
  { label: t('data.financial.revenue'), value: 'NT$ 85.2B', sub: 'YoY +12.3%', positive: true },
  { label: t('data.financial.netIncome'), value: 'NT$ 18.7B', sub: 'YoY +8.5%', positive: true },
  { label: t('data.financial.grossMargin'), value: '42.8%', sub: '+1.2pp', positive: true },
  { label: t('data.financial.earningsPerShare'), value: 'NT$ 7.24', sub: 'YoY +9.1%', positive: true },
  { label: t('data.financial.returnOnEquity'), value: '18.5%', sub: '+0.8pp', positive: true },
])

const localRadarData = computed(() => [
  { label: t('data.radar.liquidity'), value: 85, max: 100 },
  { label: t('data.radar.solvency'), value: 78, max: 100 },
  { label: t('data.radar.profitability'), value: 92, max: 100 },
  { label: t('data.radar.growth'), value: 88, max: 100 },
  { label: t('data.radar.efficiency'), value: 82, max: 100 },
  { label: t('data.radar.cashQuality'), value: 90, max: 100 },
])

const localRatioTableData = computed(() => [
  { name: t('data.ratios.currentRatio'), current: '185%', prev: '172%', change: '+13pp', trend: 'up' as const },
  { name: t('data.ratios.quickRatio'), current: '142%', prev: '135%', change: '+7pp', trend: 'up' as const },
  { name: t('data.ratios.debtRatio'), current: '38.5%', prev: '41.2%', change: '-2.7pp', trend: 'down' as const },
  { name: t('data.ratios.interestCoverage'), current: '12.4x', prev: '10.8x', change: '+1.6x', trend: 'up' as const },
  { name: t('data.ratios.roa'), current: '14.2%', prev: '13.5%', change: '+0.7pp', trend: 'up' as const },
  { name: t('data.ratios.inventoryDays'), current: '45天', prev: '52天', change: '-7天', trend: 'up' as const },
  { name: t('data.ratios.receivableDays'), current: '38天', prev: '42天', change: '-4天', trend: 'up' as const },
])

const localBalanceSheetData = computed(() => ({
  assets: [
    { label: t('data.balanceSheet.cash'), value: 'NT$28.5B', ratio: 28.5, color: '#FFFFFF' },
    { label: t('data.balanceSheet.receivables'), value: 'NT$15.2B', ratio: 15.2, color: '#666666' },
    { label: t('data.balanceSheet.inventory'), value: 'NT$12.8B', ratio: 12.8, color: '#333333' },
    { label: t('data.balanceSheet.fixedAssets'), value: 'NT$22.4B', ratio: 22.4, color: '#999999' },
    { label: t('data.balanceSheet.other'), value: 'NT$6.3B', ratio: 6.3, color: '#555555' },
  ],
  liabilities: [
    { label: t('data.balanceSheet.shortTermDebt'), value: 'NT$8.2B', ratio: 8.2, color: '#666666' },
    { label: t('data.balanceSheet.longTermDebt'), value: 'NT$12.5B', ratio: 12.5, color: '#333333' },
    { label: t('data.balanceSheet.equity'), value: 'NT$15.0B', ratio: 15.0, color: '#FFFFFF' },
    { label: t('data.balanceSheet.retainedEarnings'), value: 'NT$28.0B', ratio: 28.0, color: '#888888' },
    { label: t('data.balanceSheet.otherEquity'), value: 'NT$5.2B', ratio: 5.2, color: '#444444' },
  ],
}))

const localDupontData = computed(() => ({
  roe: '18.5%',
  netMargin: '21.9%',
  assetTurnover: '0.68',
  equityMultiplier: '1.64',
  table: [
    { metric: t('data.dupont.netMargin'), current: '21.9%', industry: '18.5%', diff: '+3.4pp', positive: true },
    { metric: t('data.dupont.assetTurnover'), current: '0.68', industry: '0.72', diff: '-0.04', positive: false },
    { metric: t('data.dupont.equityMultiplier'), current: '1.64', industry: '1.58', diff: '+0.06', positive: true },
    { metric: t('data.dupont.roe'), current: '18.5%', industry: '16.2%', diff: '+2.3pp', positive: true },
  ],
}))

const aiMemoContent = `
## TSMC 2025 Q1 財務分析摘要

### Financial Highlights
台積電 2025 年第一季營收達 NT$25.8B，季減 1.2% 但年增 14.7%。毛利率提升至 42.8%，營業利益率達 18.2%，均優於市場預期。

### Key Developments
- AI/HPC 需求持續強勁，佔先進製程營收 65%
- 3nm 產能利用率提升至 85%
- 海外擴廠進度符合預期（Arizona、熊本）

### Risk Factors
- 地緣政治風險持續存在
- 資本支出維持高檔，短期自由現金流承壓
- 匯率波動對毛利率影響約 0.5-1.0pp

### Investment Recommendation
**Positive** — AI 長期結構性需求支撐成長動能，技術領先地位穩固。
`

const citations = [
  { source: '台積電 2025 Q1 財報', section: '綜合損益表', content: '營收 NT$25.8B，毛利率 42.8%，營業利益率 18.2%', page: '12', confidence: '高' },
  { source: '台積電 2025 Q1 財報', section: '營運回顧', content: 'AI/HPC 佔先進製程營收 65%，3nm 產能利用率 85%', page: '8', confidence: '高' },
  { source: '台積電法說會紀錄', section: '管理層展望', content: '海外擴廠進度符合預期，資本支出維持 US$30-32B', page: '15', confidence: '中' },
]

const criticNotes = [
  { severity: 'warning', title: '數據來源提醒', note: '部分比率使用估計值，非正式財報數字' },
  { severity: 'info', title: '比較基準', note: 'ROE 與同業比較基準為半導體代工產業平均值' },
  { severity: 'success', title: '分析品質', note: '所有關鍵數據均已交叉驗證，可信度高' },
]

const portfolioImpact = {
  portfolios: ['科技成長型投資組合', '全球平衡型組合'],
  totalExposure: 'NT$ 3,250,000',
  exposurePercent: '25.8%',
  estimatedImpact: '+5.2%',
  recommendation: 'Positive',
}
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px">
    <!-- Back + Header -->
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn kimi-btn-dark" style="margin-bottom: 24px" @click="router.push({ name: 'financial-reports' })">
        ← BACK TO REPORTS
      </button>

      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">TSMC 2025 Q1 財報分析</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">FINANCIAL REPORT — 台積電 (2330)</span>
        </div>
        <div style="display: flex; gap: 8px">
          <span class="kimi-tag" style="border-color: #8B1A2B; color: #8B1A2B">{{ t('reports.detail.aiMemoEn') }}</span>
          <span class="kimi-tag" style="border-color: #fbbf24; color: #fbbf24">{{ t('reports.list.confidence') }}: 高</span>
        </div>
      </div>

      <!-- KPI Cards -->
      <ScrollReveal>
        <div class="kimi-section-dark">
          <div class="kimi-kpi-grid-5">
            <div v-for="(kpi, i) in localFinancialKPIData" :key="i"
              class="kimi-kpi-cell kimi-kpi-cell-dark"
            >
              <span class="kimi-caption" style="color: #666666; margin-bottom: 8px">{{ kpi.label }}</span>
              <span style="font-size: 28px; font-weight: 600; color: #FFFFFF">{{ kpi.value }}</span>
              <span
                style="font-size: 12px; margin-top: 4px"
                :style="{ color: kpi.positive ? '#8B1A2B' : '#666666' }"
              >
                {{ kpi.sub }}
              </span>
              <div class="accent-bar" style="background-color: #8B1A2B" />
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Tabs -->
      <div style="margin-top: 40px; border-bottom: 1px solid #333333; display: flex; gap: 0">
        <button
          v-for="tab in tabs"
          :key="tab.key"
          :style="{
            padding: '12px 20px',
            fontSize: '12px',
            fontWeight: 500,
            letterSpacing: '0.1em',
            background: 'none',
            border: 'none',
            borderBottom: activeTab === tab.key ? '2px solid #8B1A2B' : '2px solid transparent',
            color: activeTab === tab.key ? '#FFFFFF' : '#666666',
            cursor: 'pointer',
            fontFamily: 'Inter, sans-serif',
            transition: 'all 0.2s ease',
          }"
          @click="activeTab = tab.key"
        >
          {{ tab.label }}
        </button>
      </div>

      <!-- Tab: AI Memo -->
      <div v-if="activeTab === 'memo'" style="margin-top: 24px">
        <ScrollReveal>
          <div class="kimi-section-dark" style="padding: 24px">
            <div style="white-space: pre-wrap; font-size: 14px; line-height: 1.8; color: #E0E0E0">
              {{ aiMemoContent }}
            </div>
          </div>
        </ScrollReveal>
      </div>

      <!-- Tab: Financial Metrics -->
      <div v-if="activeTab === 'metrics'" style="margin-top: 24px">
        <!-- Revenue & Profit Trend -->
        <ScrollReveal>
          <div class="kimi-section-dark" style="margin-bottom: 24px">
            <div style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">{{ t('reports.detail.revenueProfit') }}</h2>
              <span class="kimi-caption" style="color: #666666; margin-top: 4px; display: block">{{ t('reports.detail.revenueProfitEn') }}</span>
            </div>
            <div style="padding: 20px">
              <svg width="100%" height="420" viewBox="0 0 900 420">
                <!-- Grid -->
                <line v-for="i in 6" :key="'g-' + i" x1="80" :y1="40 + ((i - 1) / 5) * 320" x2="860" :y2="40 + ((i - 1) / 5) * 320" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                <!-- Y labels -->
                <text v-for="(l, i) in ['100B', '80B', '60B', '40B', '20B', '0']" :key="'y-' + i" x="75" :y="40 + (i / 5) * 320 + 4" text-anchor="end" fill="#666666" font-size="11">{{ l }}</text>

                <!-- Bars -->
                <template v-for="(d, i) in revenueProfitData.quarters" :key="'q-' + i">
                  <rect v-if="revenueProfitData.r23[i] !== null"
                    :x="80 + (i / 12) * 780 - 18"
                    :y="40 + 320 - ((revenueProfitData.r23[i] ?? 0) / 100) * 320"
                    width="16"
                    :height="((revenueProfitData.r23[i] ?? 0) / 100) * 320"
                    fill="#333333"
                  />
                  <rect v-if="revenueProfitData.r24[i] !== null"
                    :x="80 + (i / 12) * 780 - 2"
                    :y="40 + 320 - ((revenueProfitData.r24[i] ?? 0) / 100) * 320"
                    width="16"
                    :height="((revenueProfitData.r24[i] ?? 0) / 100) * 320"
                    fill="#666666"
                  />
                  <rect v-if="revenueProfitData.r25[i] !== null"
                    :x="80 + (i / 12) * 780 + 2"
                    :y="40 + 320 - ((revenueProfitData.r25[i] ?? 0) / 100) * 320"
                    width="16"
                    :height="((revenueProfitData.r25[i] ?? 0) / 100) * 320"
                    fill="#FFFFFF"
                  />
                  <text :x="80 + (i / 12) * 780 + 8" y="385" text-anchor="middle" fill="#666666" font-size="10">{{ d }}</text>
                </template>

                <!-- Net income line -->
                <polyline
                  :points="revenueProfitData.quarters.map((_, i) => {
                    const x = 80 + (i / 12) * 780 + 8
                    const y = 40 + 320 - (revenueProfitData.netIncome[i] / 100) * 320
                    return `${x},${y}`
                  }).join(' ')"
                  fill="none"
                  stroke="#8B1A2B"
                  stroke-width="2"
                />
                <circle
                  v-for="(_, i) in revenueProfitData.quarters"
                  :key="'ni-' + i"
                  :cx="80 + (i / 12) * 780 + 8"
                  :cy="40 + 320 - (revenueProfitData.netIncome[i] / 100) * 320"
                  r="4"
                  fill="#8B1A2B"
                />

                <!-- Legend -->
                <g transform="translate(350, 410)">
                  <rect x="0" y="-8" width="12" height="10" fill="#333333" />
                  <text x="18" y="0" fill="#666666" font-size="10">2023營收</text>
                  <rect x="80" y="-8" width="12" height="10" fill="#666666" />
                  <text x="98" y="0" fill="#666666" font-size="10">2024營收</text>
                  <rect x="160" y="-8" width="12" height="10" fill="#FFFFFF" />
                  <text x="178" y="0" fill="#666666" font-size="10">2025營收</text>
                  <circle cx="265" cy="-3" r="4" fill="#8B1A2B" />
                  <text x="275" y="0" fill="#666666" font-size="10">淨利</text>
                </g>
              </svg>
            </div>
          </div>
        </ScrollReveal>

        <!-- Profitability -->
        <div class="kimi-grid-3" style="margin-bottom: 24px">
          <ScrollReveal v-for="(item, i) in [
            { title: '毛利率趨勢', data: profitabilityData.grossMargin, color: '#FFFFFF', latest: '42.8%' },
            { title: '營業利益率', data: profitabilityData.operatingMargin, color: '#8B1A2B', latest: '18.2%' },
            { title: t('reports.detail.netMargin'), data: profitabilityData.netMargin, color: '#666666', latest: '21.9%' },
          ]" :key="i" :delay="i * 0.1">
            <div class="kimi-panel-dark">
              <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600; color: #FFFFFF">{{ item.title }}</h3>
              <svg width="100%" height="160" viewBox="0 0 300 160">
                <polyline
                  :points="item.data.map((v, j) => {
                    const x = 20 + (j / 11) * 260
                    const y = 140 - ((v - 35) / 10) * 120
                    return `${x},${y}`
                  }).join(' ')"
                  fill="none"
                  :stroke="item.color"
                  stroke-width="2"
                />
                <circle
                  v-for="(v, j) in item.data"
                  :key="j"
                  :cx="20 + (j / 11) * 260"
                  :cy="140 - ((v - 35) / 10) * 120"
                  r="3"
                  :fill="item.color"
                />
                <text x="280" :y="140 - ((item.data[item.data.length - 1] - 35) / 10) * 120 + 4" text-anchor="end" fill="#FFFFFF" font-size="14" font-weight="600">
                  {{ item.latest }}
                </text>
              </svg>
            </div>
          </ScrollReveal>
        </div>

        <!-- Cash Flow -->
        <ScrollReveal>
          <div class="kimi-section-dark" style="margin-bottom: 24px">
            <div style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">{{ t('reports.detail.cashFlow') }}</h2>
              <span class="kimi-caption" style="color: #666666; margin-top: 4px; display: block">{{ t('reports.detail.cashFlowEn') }}</span>
            </div>
            <div style="padding: 20px">
              <StackedBarChart
                :data="cashFlowData.labels.map((label, i) => ({
                  label,
                  values: [cashFlowData.operating[i], cashFlowData.investing[i], cashFlowData.financing[i]],
                }))"
                :colors="['#FFFFFF', '#666666', '#333333']"
                :legend-labels="[t('reports.detail.operating'), t('reports.detail.investing'), t('reports.detail.financing'), t('reports.detail.freeCashFlow')]"
                :y-axis-labels="['-10B', '0', '10B', '20B', '30B']"
                :line-data="cashFlowData.freeCashFlow"
              />
            </div>
          </div>
        </ScrollReveal>

        <!-- Radar + Ratios -->
        <div class="kimi-grid-2" style="border: 1px solid #333333">
          <ScrollReveal style="padding: 20px; display: flex; flex-direction: column; align-items: center; border-right: 1px solid #333333">
            <h2 style="margin: 0 0 24px; font-size: 20px; font-weight: 600; color: #FFFFFF">{{ t('reports.detail.financialRadar') }}</h2>
            <RadarChart :dimensions="localRadarData" :width="300" :height="300" />
          </ScrollReveal>
          <ScrollReveal :delay="0.1" style="padding: 20px">
            <h2 style="margin: 0 0 16px; font-size: 20px; font-weight: 600; color: #FFFFFF">{{ t('reports.detail.keyRatios') }}</h2>
            <DataTable
              :headers="[t('reports.detail.keyRatios'), t('reports.detail.current'), '上期', t('reports.detail.diff'), '趨勢']"
              :rows="localRatioTableData.map((r) => [r.name, r.current, r.prev, r.change, r.trend === 'up' ? '↑' : '↓'])"
              :dark="true"
            />
          </ScrollReveal>
        </div>

        <!-- Balance Sheet -->
        <ScrollReveal style="margin-top: 24px">
          <div class="kimi-section-dark">
            <div style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">{{ t('reports.detail.balanceSheet') }}</h2>
              <span class="kimi-caption" style="color: #666666; margin-top: 4px; display: block">{{ t('reports.detail.balanceSheetEn') }}</span>
            </div>
            <div style="display: grid; grid-template-columns: 1fr 1fr">
              <div style="padding: 20px; border-right: 1px solid #333333">
                <h3 style="margin: 0 0 16px; font-size: 14px; font-weight: 500; color: #FFFFFF">{{ t('reports.detail.assets') }}</h3>
                <div style="display: flex; height: 40px; width: 100%; margin-bottom: 16px">
                  <div style="height: 100%; width: 55%; background: #FFFFFF" />
                  <div style="height: 100%; width: 30%; background: #666666" />
                  <div style="height: 100%; width: 15%; background: #333333" />
                </div>
                <div v-for="(a, i) in localBalanceSheetData.assets" :key="i" style="display: flex; align-items: center; gap: 8px; margin-bottom: 8px">
                  <div style="width: 12px; height: 12px" :style="{ background: a.color }" />
                  <span style="flex: 1; font-size: 13px; color: #999999">{{ a.label }}</span>
                  <span style="font-size: 13px; color: #FFFFFF">{{ a.value }}</span>
                </div>
              </div>
              <div style="padding: 20px">
                <h3 style="margin: 0 0 16px; font-size: 14px; font-weight: 500; color: #FFFFFF">{{ t('reports.detail.liabilities') }}</h3>
                <div style="display: flex; height: 40px; width: 100%; margin-bottom: 16px">
                  <div style="height: 100%; width: 35%; background: #666666" />
                  <div style="height: 100%; width: 25%; background: #333333" />
                  <div style="height: 100%; width: 40%; background: #FFFFFF" />
                </div>
                <div v-for="(l, i) in localBalanceSheetData.liabilities" :key="i" style="display: flex; align-items: center; gap: 8px; margin-bottom: 8px">
                  <div style="width: 12px; height: 12px" :style="{ background: l.color }" />
                  <span style="flex: 1; font-size: 13px; color: #999999">{{ l.label }}</span>
                  <span style="font-size: 13px; color: #FFFFFF">{{ l.value }}</span>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- DuPont -->
        <ScrollReveal style="margin-top: 24px">
          <div class="kimi-section-dark">
            <div style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="margin: 0; font-size: 20px; font-weight: 600; color: #FFFFFF">{{ t('reports.detail.dupontAnalysis') }}</h2>
              <span class="kimi-caption" style="color: #666666; margin-top: 4px; display: block">{{ t('reports.detail.dupontAnalysisEn') }}</span>
            </div>
            <div style="padding: 24px">
              <!-- Decomposition -->
              <div style="display: flex; align-items: center; justify-content: center; gap: 24px; flex-wrap: wrap; margin-bottom: 32px">
                <div style="border: 1px solid #8B1A2B; padding: 20px; text-align: center; min-width: 140px">
                  <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 4px">{{ t('reports.detail.roe') }}</span>
                  <span style="font-size: 28px; font-weight: 600; color: #FFFFFF">{{ localDupontData.roe }}</span>
                </div>
                <span style="font-size: 24px; color: #666666">=</span>
                <div style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap; justify-content: center">
                  <template v-for="(f, i) in [
                    { label: t('reports.detail.netMargin'), value: localDupontData.netMargin },
                    { label: t('reports.detail.assetTurnover'), value: localDupontData.assetTurnover },
                    { label: t('reports.detail.equityMultiplier'), value: localDupontData.equityMultiplier },
                  ]" :key="i">
                    <div style="border: 1px solid #333333; padding: 16px; text-align: center; min-width: 100px">
                      <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 4px">{{ f.label }}</span>
                      <span style="font-size: 18px; font-weight: 500; color: #FFFFFF">{{ f.value }}</span>
                    </div>
                    <span v-if="i < 2" style="font-size: 18px; color: #666666">×</span>
                  </template>
                </div>
              </div>

              <!-- Comparison Table -->
              <DataTable
                :headers="[t('reports.detail.metric'), t('reports.detail.current'), t('reports.detail.industry'), t('reports.detail.diff')]"
                :rows="localDupontData.table.map((r) => [r.metric, r.current, r.industry, r.diff])"
                :dark="true"
              />
            </div>
          </div>
        </ScrollReveal>
      </div>

      <!-- Tab: Citations -->
      <div v-if="activeTab === 'citations'" style="margin-top: 24px">
        <ScrollReveal>
          <div class="kimi-section-dark" style="padding: 20px">
            <div v-for="(c, i) in citations" :key="i" style="border-bottom: 1px solid #333333; padding: 20px 0">
              <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px">
                <span style="font-weight: 600; color: #FFFFFF">{{ c.source }}</span>
                <span class="kimi-tag" style="font-size: 10px">Page {{ c.page }}</span>
              </div>
              <div style="font-size: 13px; color: #666666; margin-bottom: 8px">
                Section: {{ c.section }} | Confidence: {{ c.confidence }}
              </div>
              <div style="font-size: 14px; color: #E0E0E0; padding: 12px; border-left: 2px solid #8B1A2B; background: rgba(139, 26, 43, 0.05)">
                "{{ c.content }}"
              </div>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <!-- Tab: Critic Notes -->
      <div v-if="activeTab === 'critic'" style="margin-top: 24px">
        <ScrollReveal>
          <div class="kimi-section-dark" style="padding: 20px">
            <div v-for="(note, i) in criticNotes" :key="i"
              style="padding: 16px 20px; margin-bottom: 12px; border-left: 3px solid; background: rgba(255,255,255,0.02)"
              :style="{
                borderColor: note.severity === 'warning' ? '#fbbf24' : note.severity === 'info' ? '#60a5fa' : '#34d399'
              }"
            >
              <div style="font-weight: 600; color: #FFFFFF; margin-bottom: 4px">{{ note.title }}</div>
              <div style="font-size: 13px; color: #999999">{{ note.note }}</div>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <!-- Tab: Portfolio Impact -->
      <div v-if="activeTab === 'impact'" style="margin-top: 24px">
        <ScrollReveal>
          <div class="kimi-section-dark" style="padding: 24px">
            <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 24px">
              <div>
                <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 8px">{{ t('reports.detail.impactSummary') }}</span>
                <div v-for="p in portfolioImpact.portfolios" :key="p" style="padding: 8px 0; border-bottom: 1px solid #333333; color: #FFFFFF; font-size: 14px">
                  {{ p }}
                </div>
              </div>
              <div>
                <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 8px">總曝險</span>
                <span style="font-size: 24px; font-weight: 600; color: #FFFFFF">{{ portfolioImpact.totalExposure }}</span>
              </div>
              <div>
                <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 8px">曝險佔比</span>
                <span style="font-size: 24px; font-weight: 600; color: #FFFFFF">{{ portfolioImpact.exposurePercent }}</span>
              </div>
              <div>
                <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 8px">預估影響</span>
                <span style="font-size: 24px; font-weight: 600; color: #8B1A2B">{{ portfolioImpact.estimatedImpact }}</span>
              </div>
            </div>
            <div style="margin-top: 24px; padding: 16px; border: 1px solid #8B1A2B; background: rgba(139, 26, 43, 0.05)">
              <span class="kimi-caption" style="color: #666666; display: block; margin-bottom: 4px">{{ t('reports.detail.criticRecommendation') }}</span>
              <span style="font-size: 18px; font-weight: 600; color: #8B1A2B">{{ portfolioImpact.recommendation }}</span>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div style="height: 80px" />
    </div>

    <Footer :dark="true" label="FINANCIALS" />
  </div>
</template>
