<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NTag,
  NTabs,
  NTabPane,
  NIcon,
  NSpace,
  NCollapse,
  NCollapseItem,
} from 'naive-ui'
import {
  ArrowBackOutline,
  SparklesOutline,
  CheckmarkCircleOutline,
  WarningOutline,
  LinkOutline,
  TrendingUpOutline,
  BarChartOutline,
  TimeOutline,
} from '@vicons/ionicons5'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { BarChart, LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import VChart from 'vue-echarts'

use([CanvasRenderer, BarChart, LineChart, GridComponent, TooltipComponent, LegendComponent])

const route = useRoute()
const router = useRouter()
const reportId = route.params.id as string

// Mock report data
const report = ref({
  id: reportId,
  name: 'TSMC 2025 Q1 財報分析',
  company: 'Taiwan Semiconductor Manufacturing Co.',
  ticker: 'TSMC',
  period: '2025 Q1',
  generatedAt: '2026-06-03 10:30:00',
  aiModel: 'GPT-4',
  confidence: '高',
  status: 'completed',
})

// AI Memo
const aiMemo = ref(`
## 財務表現摘要

TSMC 2025 年第一季營收達到 NT$6,592 億元，年增 16.5%，優於市場預期的 NT$6,400 億元。毛利率維持在 53.2% 的高水準，顯示公司在先進製程的定價能力依然強勁。

## 關鍵亮點

1. **先進製程營收佔比提升**：3nm 製程營收佔比從上季的 15% 提升至 22%，5nm 佔比維持在 32%。先進製程（7nm 及以下）合計佔總營收 67%。

2. **AI 相關需求強勁**：AI 加速器晶片營收年增超過 150%，佔總營收比重達到 18%，成為最主要的成長動能。

3. **資本支出維持高檔**：2025 年資本支出預估維持在 US$38-40 億元，主要用於 2nm 製程研發與產能擴充。

## 風險因素

- 地緣政治風險持續，美國對中國半導體出口管制可能影響部分客戶需求
- 智慧型手機市場復甦速度低於預期
- 先進製程研發成本持續攀升

## 投資建議

維持「買進」評等，目標價 NT$950。AI 需求持續強勁，先進製程領先地位穩固，長期成長動能明確。
`)

// Metrics
const metrics = ref({
  revenue: 659200,
  revenueGrowth: 16.5,
  grossMargin: 53.2,
  operatingMargin: 42.8,
  netMargin: 38.5,
  eps: 9.85,
  epsGrowth: 22.3,
  roe: 28.5,
  debtToEquity: 0.32,
})

// Financial data chart
const financialChartOption = computed(() => ({
  backgroundColor: 'transparent',
  grid: {
    left: '3%',
    right: '4%',
    bottom: '3%',
    top: '15%',
    containLabel: true,
  },
  tooltip: {
    trigger: 'axis',
    backgroundColor: 'rgba(17, 24, 39, 0.9)',
    borderColor: 'rgba(255, 255, 255, 0.1)',
    textStyle: { color: '#f0f4f8' },
  },
  legend: {
    data: ['營收', '毛利率', '營業利益率'],
    textStyle: { color: '#94a3b8' },
    top: 0,
  },
  xAxis: {
    type: 'category',
    data: ['2024 Q1', '2024 Q2', '2024 Q3', '2024 Q4', '2025 Q1'],
    axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
    axisLabel: { color: '#64748b' },
  },
  yAxis: [
    {
      type: 'value',
      name: '營收 (億)',
      nameTextStyle: { color: '#64748b' },
      axisLine: { show: false },
      splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
      axisLabel: { color: '#64748b' },
    },
    {
      type: 'value',
      name: '比率 (%)',
      nameTextStyle: { color: '#64748b' },
      axisLine: { show: false },
      splitLine: { show: false },
      axisLabel: { color: '#64748b', formatter: '{value}%' },
    },
  ],
  series: [
    {
      name: '營收',
      type: 'bar',
      data: [5650, 5820, 6100, 6380, 6592],
      itemStyle: { color: '#60a5fa', borderRadius: [4, 4, 0, 0] },
      barWidth: '40%',
    },
    {
      name: '毛利率',
      type: 'line',
      yAxisIndex: 1,
      data: [52.5, 52.8, 53.0, 53.5, 53.2],
      lineStyle: { color: '#34d399', width: 2 },
      itemStyle: { color: '#34d399' },
      symbol: 'circle',
      symbolSize: 6,
    },
    {
      name: '營業利益率',
      type: 'line',
      yAxisIndex: 1,
      data: [41.5, 41.8, 42.2, 43.0, 42.8],
      lineStyle: { color: '#fbbf24', width: 2 },
      itemStyle: { color: '#fbbf24' },
      symbol: 'circle',
      symbolSize: 6,
    },
  ],
}))

// Citations
const citations = ref([
  {
    id: '1',
    source: 'TSMC 2025 Q1 財報',
    section: '合併損益表',
    content: '第一季營收 NT$6,592 億元，毛利率 53.2%，營業利益率 42.8%',
    page: '3',
    confidence: '高',
  },
  {
    id: '2',
    source: 'TSMC 2025 Q1 法說會逐字稿',
    section: '管理階層討論',
    content: 'AI 相關營收年增超過 150%，佔總營收比重達到 18%',
    page: '12',
    confidence: '高',
  },
  {
    id: '3',
    source: 'TSMC 2025 Q1 財報',
    section: '資本支出展望',
    content: '2025 年資本支出預估維持在 US$38-40 億元',
    page: '8',
    confidence: '中',
  },
])

// Critic Notes
const criticNotes = ref([
  {
    id: '1',
    type: 'warning',
    title: '毛利率預測過於樂觀',
    content: 'AI 晶片雖然毛利率較高，但競爭加劇可能導致價格壓力。建議關注 Samsung 3nm 製程的競爭態勢。',
    severity: '中',
  },
  {
    id: '2',
    type: 'info',
    title: '地緣政治風險未充分量化',
    content: '報告提及地緣政治風險但未提供具體的財務影響評估。建議補充情境分析。',
    severity: '低',
  },
  {
    id: '3',
    type: 'success',
    title: '資本支出規劃合理',
    content: '2nm 製程的資本支出規劃與公司長期技術藍圖一致，產能擴充計畫穩健。',
    severity: '低',
  },
])

// Portfolio Impact
const portfolioImpact = ref({
  affectedPortfolios: ['科技成長型投資組合', '全球平衡型組合'],
  totalExposure: 520000,
  exposurePercent: 21.2,
  estimatedImpact: 3.5,
  recommendation: '觀望',
})

function formatCurrency(value: number): string {
  return new Intl.NumberFormat('zh-TW', {
    style: 'currency',
    currency: 'TWD',
    minimumFractionDigits: 0,
  }).format(value * 1000000)
}

function getConfidenceColor(confidence: string): 'success' | 'warning' | 'error' | 'default' {
  const map: Record<string, 'success' | 'warning' | 'error' | 'default'> = {
    '高': 'success',
    '中': 'warning',
    '低': 'error',
  }
  return map[confidence] || 'default'
}

function getSeverityColor(severity: string): 'error' | 'warning' | 'success' | 'default' {
  const map: Record<string, 'error' | 'warning' | 'success' | 'default'> = {
    '高': 'error',
    '中': 'warning',
    '低': 'success',
  }
  return map[severity] || 'default'
}

function goBack() {
  router.push({ name: 'dashboard' })
}
</script>

<template>
  <main class="page animate-fade-in">
    <!-- Back Button -->
    <div style="margin-bottom: 16px;">
      <NButton text type="primary" size="small" @click="goBack">
        <template #icon>
          <NIcon><ArrowBackOutline /></NIcon>
        </template>
        返回 Dashboard
      </NButton>
    </div>

    <!-- Header -->
    <section class="page-heading">
      <div>
        <p class="eyebrow">AI Financial Analysis Report</p>
        <h1>{{ report.name }}</h1>
        <p style="color: var(--text-secondary); margin-top: 8px;">
          {{ report.company }} ({{ report.ticker }}) · {{ report.period }}
        </p>
      </div>
      <NSpace align="center">
        <NTag :type="getConfidenceColor(report.confidence)" round>
          信心: {{ report.confidence }}
        </NTag>
        <NTag type="info" round>
          <template #icon>
            <NIcon><SparklesOutline /></NIcon>
          </template>
          {{ report.aiModel }}
        </NTag>
      </NSpace>
    </section>

    <!-- Key Metrics -->
    <NGrid :cols="4" :x-gap="16" :y-gap="16" responsive="screen">
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">營收 (億)</div>
          <div class="metric-value">{{ metrics.revenue }}</div>
          <div class="metric-change positive">
            <NIcon :size="14" style="vertical-align: middle; margin-right: 4px;"><TrendingUpOutline /></NIcon>
            +{{ metrics.revenueGrowth }}% YoY
          </div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">毛利率</div>
          <div class="metric-value" style="color: var(--success);">{{ metrics.grossMargin }}%</div>
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px;">高於產業平均</div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">每股盈餘</div>
          <div class="metric-value">NT${{ metrics.eps }}</div>
          <div class="metric-change positive">
            <NIcon :size="14" style="vertical-align: middle; margin-right: 4px;"><TrendingUpOutline /></NIcon>
            +{{ metrics.epsGrowth }}% YoY
          </div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">ROE</div>
          <div class="metric-value" style="color: var(--success);">{{ metrics.roe }}%</div>
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px;">股東權益報酬率</div>
        </div>
      </NGridItem>
    </NGrid>

    <!-- Tabs -->
    <div class="glass-panel" style="margin-top: 24px; padding: 0;">
      <NTabs type="line" class="glass-tabs" style="padding: 20px 24px 0;">
        <NTabPane name="memo" tab="AI 分析摘要">
          <div style="padding: 24px;">
            <div
              class="glass-card"
              style="padding: 28px; background: rgba(96, 165, 250, 0.05); border: 1px solid rgba(96, 165, 250, 0.15);"
            >
              <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 20px;">
                <NIcon :size="20" color="#60a5fa"><SparklesOutline /></NIcon>
                <span style="color: var(--accent-primary); font-weight: 600; font-size: 14px;">AI 生成分析</span>
                <span style="color: var(--text-tertiary); font-size: 12px;">· {{ report.generatedAt }}</span>
              </div>
              <div
                class="ai-memo-content"
                style="color: var(--text-secondary); line-height: 1.8; font-size: 14px;"
                v-html="aiMemo.replace(/\n/g, '<br>').replace(/## (.*)/g, '<h3 style=\'color: var(--text-primary); margin: 20px 0 12px; font-size: 16px;\'>$1</h3>').replace(/\d+\. \*\*(.*)\*\*/g, '<strong style=\'color: var(--text-primary);\'>$1</strong>')"
              ></div>
            </div>
          </div>
        </NTabPane>

        <NTabPane name="metrics" tab="財務指標">
          <div style="padding: 24px;">
            <VChart :option="financialChartOption" style="height: 360px;" autoresize />

            <div class="glass-divider"></div>

            <NGrid :cols="3" :x-gap="16" :y-gap="16" responsive="screen">
              <NGridItem>
                <div style="padding: 20px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                  <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">營業利益率</div>
                  <div style="color: var(--text-primary); font-size: 28px; font-weight: 700;">{{ metrics.operatingMargin }}%</div>
                </div>
              </NGridItem>
              <NGridItem>
                <div style="padding: 20px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                  <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">淨利率</div>
                  <div style="color: var(--text-primary); font-size: 28px; font-weight: 700;">{{ metrics.netMargin }}%</div>
                </div>
              </NGridItem>
              <NGridItem>
                <div style="padding: 20px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                  <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">負債權益比</div>
                  <div style="color: var(--text-primary); font-size: 28px; font-weight: 700;">{{ metrics.debtToEquity }}</div>
                </div>
              </NGridItem>
            </NGrid>
          </div>
        </NTabPane>

        <NTabPane name="citations" tab="資料來源">
          <div style="padding: 24px;">
            <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 20px;">
              <NIcon :size="18" color="#60a5fa"><LinkOutline /></NIcon>
              <span style="color: var(--text-secondary); font-size: 14px;">
                以下為 AI 分析所引用的資料來源，點擊可查看原始文件
              </span>
            </div>
            <NCollapse>
              <NCollapseItem
                v-for="citation in citations"
                :key="citation.id"
                :title="`${citation.source} · 第 ${citation.page} 頁`"
              >
                <template #header-extra>
                  <NTag size="small" :type="getConfidenceColor(citation.confidence)" round>
                    信心: {{ citation.confidence }}
                  </NTag>
                </template>
                <div style="padding: 16px; background: rgba(255,255,255,0.03); border-radius: 8px;">
                  <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">
                    章節: {{ citation.section }}
                  </div>
                  <div style="color: var(--text-primary); font-size: 14px; line-height: 1.6;">
                    「{{ citation.content }}」
                  </div>
                </div>
              </NCollapseItem>
            </NCollapse>
          </div>
        </NTabPane>

        <NTabPane name="critic" tab="批判檢視">
          <div style="padding: 24px;">
            <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 20px;">
              <NIcon :size="18" color="#fbbf24"><WarningOutline /></NIcon>
              <span style="color: var(--text-secondary); font-size: 14px;">
                AI 批判檢視系統自動生成的分析品質檢查報告
              </span>
            </div>
            <div style="display: grid; gap: 12px;">
              <div
                v-for="note in criticNotes"
                :key="note.id"
                class="glass-card"
                style="padding: 20px;"
                :style="{
                  borderLeft: `3px solid ${note.type === 'warning' ? 'var(--warning)' : note.type === 'success' ? 'var(--success)' : 'var(--info)'}`,
                }"
              >
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                  <div style="display: flex; align-items: center; gap: 8px;">
                    <NIcon
                      :size="18"
                      :color="note.type === 'warning' ? 'var(--warning)' : note.type === 'success' ? 'var(--success)' : 'var(--info)'"
                    >
                      <component :is="note.type === 'warning' ? WarningOutline : note.type === 'success' ? CheckmarkCircleOutline : BarChartOutline" />
                    </NIcon>
                    <span style="color: var(--text-primary); font-weight: 600;">{{ note.title }}</span>
                  </div>
                  <NTag size="small" :type="getSeverityColor(note.severity)" round>
                    嚴重度: {{ note.severity }}
                  </NTag>
                </div>
                <p style="color: var(--text-secondary); font-size: 13px; line-height: 1.6; margin: 0;">
                  {{ note.content }}
                </p>
              </div>
            </div>
          </div>
        </NTabPane>

        <NTabPane name="impact" tab="投資組合影響">
          <div style="padding: 24px;">
            <div class="glass-card" style="padding: 24px; margin-bottom: 20px;">
              <h3 style="margin-bottom: 20px;">影響摘要</h3>
              <NGrid :cols="2" :x-gap="16" :y-gap="16" responsive="screen">
                <NGridItem>
                  <div style="padding: 20px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                    <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">受影響投資組合</div>
                    <div style="display: flex; flex-wrap: wrap; gap: 8px;">
                      <NTag v-for="p in portfolioImpact.affectedPortfolios" :key="p" size="small" round>
                        {{ p }}
                      </NTag>
                    </div>
                  </div>
                </NGridItem>
                <NGridItem>
                  <div style="padding: 20px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                    <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">總曝險金額</div>
                    <div style="color: var(--text-primary); font-size: 24px; font-weight: 700;">
                      {{ formatCurrency(portfolioImpact.totalExposure / 1000000) }}
                    </div>
                    <div style="color: var(--text-secondary); font-size: 13px; margin-top: 4px;">
                      佔投資組合 {{ portfolioImpact.exposurePercent }}%
                    </div>
                  </div>
                </NGridItem>
                <NGridItem>
                  <div style="padding: 20px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                    <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">預估影響</div>
                    <div style="color: var(--success); font-size: 24px; font-weight: 700;">
                      +{{ portfolioImpact.estimatedImpact }}%
                    </div>
                    <div style="color: var(--text-secondary); font-size: 13px; margin-top: 4px;">
                      基於財報優於預期
                    </div>
                  </div>
                </NGridItem>
                <NGridItem>
                  <div style="padding: 20px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                    <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 8px;">建議操作</div>
                    <NTag type="warning" size="large" round>
                      <template #icon>
                        <NIcon><TimeOutline /></NIcon>
                      </template>
                      {{ portfolioImpact.recommendation }}
                    </NTag>
                  </div>
                </NGridItem>
              </NGrid>
            </div>
          </div>
        </NTabPane>
      </NTabs>
    </div>
  </main>
</template>

<style scoped>
.ai-memo-content h3 {
  color: var(--text-primary);
  margin: 20px 0 12px;
  font-size: 16px;
  font-weight: 600;
}

.ai-memo-content strong {
  color: var(--text-primary);
}

:deep(.n-tabs-tab) {
  font-weight: 500;
}

:deep(.n-tabs-tab--active) {
  font-weight: 600;
}

:deep(.n-collapse-item__header) {
  color: var(--text-primary) !important;
  font-weight: 500;
}

:deep(.n-collapse-item__content-wrapper) {
  border-color: var(--border-subtle) !important;
}
</style>
