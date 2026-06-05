<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NGrid,
  NGridItem,
  NTag,
  NTimeline,
  NTimelineItem,
  NList,
  NListItem,
  NThing,
  NSpace,
} from 'naive-ui'
import {
  WalletOutline,
  ShieldCheckmarkOutline,
  DocumentTextOutline,
  SparklesOutline,
  TrendingUpOutline,
  TrendingDownOutline,
  ArrowForwardOutline,
  TimeOutline,
} from '@vicons/ionicons5'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import VChart from 'vue-echarts'

use([CanvasRenderer, LineChart, GridComponent, TooltipComponent, LegendComponent])

const router = useRouter()

// Mock data for demo
const portfolioCount = ref(3)
const riskRunCount = ref(12)
const reportCount = ref(8)
const aiMemoCount = ref(5)

const metrics = [
  {
    label: '投資組合',
    value: portfolioCount.value,
    change: '+2',
    positive: true,
    icon: WalletOutline,
    color: '#60a5fa',
  },
  {
    label: '風險分析',
    value: riskRunCount.value,
    change: '+5',
    positive: true,
    icon: ShieldCheckmarkOutline,
    color: '#34d399',
  },
  {
    label: '財務報告',
    value: reportCount.value,
    change: '+3',
    positive: true,
    icon: DocumentTextOutline,
    color: '#fbbf24',
  },
  {
    label: 'AI 分析',
    value: aiMemoCount.value,
    change: '+1',
    positive: true,
    icon: SparklesOutline,
    color: '#c084fc',
  },
]

// Portfolio value chart
const portfolioChartOption = computed(() => ({
  backgroundColor: 'transparent',
  grid: {
    left: '3%',
    right: '4%',
    bottom: '3%',
    top: '10%',
    containLabel: true,
  },
  tooltip: {
    trigger: 'axis',
    backgroundColor: 'rgba(17, 24, 39, 0.9)',
    borderColor: 'rgba(255, 255, 255, 0.1)',
    textStyle: { color: '#f0f4f8' },
  },
  xAxis: {
    type: 'category',
    data: ['1月', '2月', '3月', '4月', '5月', '6月'],
    axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
    axisLabel: { color: '#64748b' },
  },
  yAxis: {
    type: 'value',
    axisLine: { show: false },
    splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
    axisLabel: { color: '#64748b' },
  },
  series: [
    {
      name: '投資組合價值',
      type: 'line',
      smooth: true,
      data: [1200000, 1250000, 1230000, 1320000, 1380000, 1450000],
      lineStyle: { color: '#60a5fa', width: 3 },
      areaStyle: {
        color: {
          type: 'linear',
          x: 0, y: 0, x2: 0, y2: 1,
          colorStops: [
            { offset: 0, color: 'rgba(96, 165, 250, 0.3)' },
            { offset: 1, color: 'rgba(96, 165, 250, 0)' },
          ],
        },
      },
      itemStyle: { color: '#60a5fa' },
      symbol: 'none',
    },
  ],
}))

// Recent activities
const recentActivities: { title: string; description: string; time: string; type: string; status: 'error' | 'default' | 'success' | 'info' | 'warning' }[] = [
  {
    title: '完成投資組合風險分析',
    description: '科技成長型投資組合 - VaR 95%: -2.3%',
    time: '2 小時前',
    type: 'risk',
    status: 'success',
  },
  {
    title: '新增 AI 財務分析報告',
    description: 'TSMC 2025 Q1 財報分析',
    time: '5 小時前',
    type: 'report',
    status: 'info',
  },
  {
    title: '建立新投資組合',
    description: '價值型藍籌股組合',
    time: '1 天前',
    type: 'portfolio',
    status: 'default',
  },
  {
    title: '風險模型參數更新',
    description: '更新置信水準至 99%',
    time: '2 天前',
    type: 'settings',
    status: 'warning',
  },
]

// Recent risk runs
const recentRiskRuns = [
  { name: '科技成長型投資組合', date: '2026-06-03', var95: '-2.3%', var99: '-3.8%', status: 'completed' },
  { name: '價值型藍籌股組合', date: '2026-06-02', var95: '-1.5%', var99: '-2.7%', status: 'completed' },
  { name: '全球平衡型組合', date: '2026-06-01', var95: '-1.8%', var99: '-3.1%', status: 'completed' },
]

// Recent reports
const recentReports = [
  { name: 'TSMC 2025 Q1 財報分析', date: '2026-06-03', type: 'AI Memo', confidence: '高' },
  { name: 'Apple FY2025 半年報', date: '2026-06-02', type: 'Financial Report', confidence: '中' },
  { name: 'NVIDIA 風險評估報告', date: '2026-06-01', type: 'Risk Report', confidence: '高' },
]

function navigateToPortfolios() {
  router.push({ name: 'portfolios' })
}
</script>

<template>
  <main class="page animate-fade-in">
    <!-- Header -->
    <section class="page-heading">
      <div>
        <p class="eyebrow">Investment Intelligence Workspace</p>
        <h1>Dashboard</h1>
      </div>
      <NButton type="primary" class="btn-primary" @click="navigateToPortfolios">
        <template #icon>
          <NIcon><ArrowForwardOutline /></NIcon>
        </template>
        查看投資組合
      </NButton>
    </section>

    <!-- Metrics Grid -->
    <NGrid :cols="4" :x-gap="16" :y-gap="16" responsive="screen">
      <NGridItem v-for="metric in metrics" :key="metric.label">
        <div class="metric-card">
          <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 16px;">
            <div
              :style="{
                width: '40px',
                height: '40px',
                borderRadius: '10px',
                display: 'grid',
                placeItems: 'center',
                background: `rgba(${metric.color === '#60a5fa' ? '96, 165, 250' : metric.color === '#34d399' ? '52, 211, 153' : metric.color === '#fbbf24' ? '251, 191, 36' : '192, 132, 252'}, 0.15)`,
              }"
            >
              <NIcon :size="20" :color="metric.color">
                <component :is="metric.icon" />
              </NIcon>
            </div>
            <span class="metric-label">{{ metric.label }}</span>
          </div>
          <div class="metric-value">{{ metric.value }}</div>
          <div class="metric-change" :class="metric.positive ? 'positive' : 'negative'">
            <NIcon :size="14" style="vertical-align: middle; margin-right: 4px;">
              <component :is="metric.positive ? TrendingUpOutline : TrendingDownOutline" />
            </NIcon>
            {{ metric.change }} 本月
          </div>
        </div>
      </NGridItem>
    </NGrid>

    <!-- Charts & Activity -->
    <NGrid :cols="2" :x-gap="16" :y-gap="16" responsive="screen" style="margin-top: 24px;">
      <NGridItem>
        <div class="glass-panel">
          <h2 style="margin-bottom: 20px;">投資組合價值趨勢</h2>
          <VChart :option="portfolioChartOption" style="height: 300px;" autoresize />
        </div>
      </NGridItem>

      <NGridItem>
        <div class="glass-panel">
          <h2 style="margin-bottom: 20px;">最近活動</h2>
          <NTimeline>
            <NTimelineItem
              v-for="activity in recentActivities"
              :key="activity.title"
              :type="activity.status"
            >
              <template #icon>
                <NIcon :size="16">
                  <TimeOutline />
                </NIcon>
              </template>
              <div>
                <div style="font-weight: 600; color: var(--text-primary); margin-bottom: 4px;">
                  {{ activity.title }}
                </div>
                <div style="color: var(--text-secondary); font-size: 13px; margin-bottom: 4px;">
                  {{ activity.description }}
                </div>
                <div style="color: var(--text-tertiary); font-size: 12px;">
                  {{ activity.time }}
                </div>
              </div>
            </NTimelineItem>
          </NTimeline>
        </div>
      </NGridItem>
    </NGrid>

    <!-- Recent Risk Runs -->
    <div class="glass-panel" style="margin-top: 24px;">
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 20px;">
        <h2 style="margin: 0;">最近風險分析</h2>
        <NButton text type="primary" size="small">
          查看全部
          <template #icon>
            <NIcon><ArrowForwardOutline /></NIcon>
          </template>
        </NButton>
      </div>
      <div class="glass-table">
        <table style="width: 100%; border-collapse: collapse;">
          <thead>
            <tr style="border-bottom: 1px solid var(--border-subtle);">
              <th style="text-align: left; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">投資組合</th>
              <th style="text-align: left; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">日期</th>
              <th style="text-align: left; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">VaR 95%</th>
              <th style="text-align: left; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">VaR 99%</th>
              <th style="text-align: left; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">狀態</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="run in recentRiskRuns"
              :key="run.name"
              style="border-bottom: 1px solid var(--border-subtle); transition: background 0.2s;"
              class="table-row-hover"
            >
              <td style="padding: 14px 16px; color: var(--text-primary); font-weight: 500;">{{ run.name }}</td>
              <td style="padding: 14px 16px; color: var(--text-secondary);">{{ run.date }}</td>
              <td style="padding: 14px 16px; color: var(--danger); font-weight: 600;">{{ run.var95 }}</td>
              <td style="padding: 14px 16px; color: var(--danger); font-weight: 600;">{{ run.var99 }}</td>
              <td style="padding: 14px 16px;">
                <NTag size="small" type="success" round>
                  <template #icon>
                    <NIcon><ShieldCheckmarkOutline /></NIcon>
                  </template>
                  完成
                </NTag>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Recent Reports -->
    <div class="glass-panel" style="margin-top: 24px;">
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 20px;">
        <h2 style="margin: 0;">最近 AI 財報分析</h2>
        <NButton text type="primary" size="small">
          查看全部
          <template #icon>
            <NIcon><ArrowForwardOutline /></NIcon>
          </template>
        </NButton>
      </div>
      <NList hoverable clickable>
        <NListItem v-for="report in recentReports" :key="report.name">
          <NThing>
            <template #header>
              <span style="color: var(--text-primary); font-weight: 600;">{{ report.name }}</span>
            </template>
            <template #header-extra>
              <NSpace>
                <NTag size="small" type="info" round>{{ report.type }}</NTag>
                <NTag size="small" type="warning" round>信心: {{ report.confidence }}</NTag>
              </NSpace>
            </template>
            <template #description>
              <span style="color: var(--text-secondary); font-size: 13px;">{{ report.date }}</span>
            </template>
          </NThing>
        </NListItem>
      </NList>
    </div>
  </main>
</template>

<style scoped>
.table-row-hover:hover {
  background: rgba(255, 255, 255, 0.04);
}

:deep(.n-timeline-item-content__title) {
  color: var(--text-primary);
  font-weight: 600;
}

:deep(.n-timeline-item-content__content) {
  color: var(--text-secondary);
  font-size: 13px;
}

:deep(.n-list-item) {
  border-bottom: 1px solid var(--border-subtle);
}

:deep(.n-list-item:hover) {
  background: rgba(255, 255, 255, 0.04);
}

:deep(.n-thing-header) {
  margin-bottom: 4px;
}
</style>
