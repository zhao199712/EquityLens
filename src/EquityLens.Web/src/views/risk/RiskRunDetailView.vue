<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NTag,
  NSteps,
  NStep,
  NIcon,
  NSpace,
} from 'naive-ui'
import {
  ArrowBackOutline,
  CheckmarkCircleOutline,
  PlayOutline,
} from '@vicons/ionicons5'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart, BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent, MarkLineComponent } from 'echarts/components'
import VChart from 'vue-echarts'

use([CanvasRenderer, LineChart, BarChart, GridComponent, TooltipComponent, LegendComponent, MarkLineComponent])

const route = useRoute()
const router = useRouter()
const riskRunId = route.params.id as string

// Mock risk run data
const riskRun = ref({
  id: riskRunId,
  portfolioName: '科技成長型投資組合',
  status: 'completed',
  createdAt: '2026-06-03 14:30:00',
  completedAt: '2026-06-03 14:32:15',
  duration: '2分15秒',
  model: 'Historical Simulation',
  confidenceLevel: 95,
  lookbackWindow: 252,
})

// VaR/ES Results
const riskMetrics = ref({
  var95: -2.34,
  var99: -3.87,
  es95: -3.12,
  es99: -4.56,
  volatility: 18.3,
  beta: 1.24,
})

// Loss distribution histogram
const lossDistributionData = Array.from({ length: 50 }, (_, i) => {
  const x = -8 + (i / 50) * 16
  const y = Math.exp(-((x + 2) ** 2) / 4) * 15 + Math.random() * 0.5
  return [x, y]
})

const lossDistributionOption = computed(() => ({
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
    formatter: (params: any) => {
      const p = params[0]
      return `損失: ${p.value[0].toFixed(2)}%<br/>頻率: ${p.value[1].toFixed(2)}`
    },
  },
  xAxis: {
    type: 'value',
    name: '損失 (%)',
    nameTextStyle: { color: '#64748b' },
    axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
    splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
    axisLabel: { color: '#64748b', formatter: '{value}%' },
  },
  yAxis: {
    type: 'value',
    name: '頻率',
    nameTextStyle: { color: '#64748b' },
    axisLine: { show: false },
    splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
    axisLabel: { color: '#64748b' },
  },
  series: [
    {
      type: 'bar',
      data: lossDistributionData,
      barWidth: '90%',
      itemStyle: {
        color: (params: any) => {
          const val = params.value[0]
          if (val <= -3.87) return '#f87171'
          if (val <= -2.34) return '#fbbf24'
          return 'rgba(96, 165, 250, 0.6)'
        },
        borderRadius: [2, 2, 0, 0],
      },
      markLine: {
        silent: true,
        symbol: 'none',
        lineStyle: { width: 2 },
        data: [
          {
            xAxis: -2.34,
            lineStyle: { color: '#fbbf24', type: 'dashed' },
            label: { formatter: 'VaR 95%', color: '#fbbf24', position: 'insideEndTop' },
          },
          {
            xAxis: -3.87,
            lineStyle: { color: '#f87171', type: 'dashed' },
            label: { formatter: 'VaR 99%', color: '#f87171', position: 'insideEndTop' },
          },
        ],
      },
    },
  ],
}))

// Historical VaR trend
const historicalVarData = Array.from({ length: 30 }, (_, i) => {
  const date = new Date()
  date.setDate(date.getDate() - (29 - i))
  return {
    date: date.toISOString().split('T')[0],
    var95: -(1.5 + Math.random() * 2),
    var99: -(2.5 + Math.random() * 3),
  }
})

const historicalVarOption = computed(() => ({
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
  legend: {
    data: ['VaR 95%', 'VaR 99%'],
    textStyle: { color: '#94a3b8' },
    top: 0,
  },
  xAxis: {
    type: 'category',
    data: historicalVarData.map(d => d.date.slice(5)),
    axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
    axisLabel: { color: '#64748b' },
  },
  yAxis: {
    type: 'value',
    axisLine: { show: false },
    splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
    axisLabel: { color: '#64748b', formatter: '{value}%' },
  },
  series: [
    {
      name: 'VaR 95%',
      type: 'line',
      data: historicalVarData.map(d => d.var95),
      lineStyle: { color: '#fbbf24', width: 2 },
      itemStyle: { color: '#fbbf24' },
      symbol: 'circle',
      symbolSize: 4,
    },
    {
      name: 'VaR 99%',
      type: 'line',
      data: historicalVarData.map(d => d.var99),
      lineStyle: { color: '#f87171', width: 2 },
      itemStyle: { color: '#f87171' },
      symbol: 'circle',
      symbolSize: 4,
    },
  ],
}))

// Model assumptions
const assumptions = ref([
  { name: '計算模型', value: 'Historical Simulation' },
  { name: '置信水準', value: '95% / 99%' },
  { name: '回顧期間', value: '252 交易日' },
  { name: '資料頻率', value: '日報酬' },
  { name: '投資組合市值', value: 'NT$2,450,000' },
  { name: '計算時間', value: '2026-06-03 14:30:00' },
])

function goBack() {
  router.push({ name: 'portfolios' })
}

function formatPercent(value: number): string {
  return `${value > 0 ? '+' : ''}${value.toFixed(2)}%`
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
        返回投資組合
      </NButton>
    </div>

    <!-- Header -->
    <section class="page-heading">
      <div>
        <p class="eyebrow">Risk Analysis Run</p>
        <h1>風險分析 #{{ riskRun.id }}</h1>
        <p style="color: var(--text-secondary); margin-top: 8px;">{{ riskRun.portfolioName }}</p>
      </div>
      <NSpace align="center">
        <NTag type="success" round>
          <template #icon>
            <NIcon><CheckmarkCircleOutline /></NIcon>
          </template>
          已完成
        </NTag>
        <NButton type="primary" class="btn-primary" size="small">
          <template #icon>
            <NIcon><PlayOutline /></NIcon>
          </template>
          重新執行
        </NButton>
      </NSpace>
    </section>

    <!-- Run Info -->
    <div class="glass-panel" style="padding: 20px 24px;">
      <NSteps :current="3" size="small" style="max-width: 600px;">
        <NStep title="準備資料" description="14:30:00" />
        <NStep title="計算 VaR" description="14:31:20" />
        <NStep title="產生報告" description="14:32:15" />
      </NSteps>
      <div style="display: flex; gap: 24px; margin-top: 16px; padding-top: 16px; border-top: 1px solid var(--border-subtle);">
        <div>
          <span style="color: var(--text-tertiary); font-size: 12px;">執行時間</span>
          <div style="color: var(--text-primary); font-weight: 600;">{{ riskRun.duration }}</div>
        </div>
        <div>
          <span style="color: var(--text-tertiary); font-size: 12px;">計算模型</span>
          <div style="color: var(--text-primary); font-weight: 600;">{{ riskRun.model }}</div>
        </div>
        <div>
          <span style="color: var(--text-tertiary); font-size: 12px;">置信水準</span>
          <div style="color: var(--text-primary); font-weight: 600;">{{ riskRun.confidenceLevel }}%</div>
        </div>
        <div>
          <span style="color: var(--text-tertiary); font-size: 12px;">回顧期間</span>
          <div style="color: var(--text-primary); font-weight: 600;">{{ riskRun.lookbackWindow }} 日</div>
        </div>
      </div>
    </div>

    <!-- Risk Metrics -->
    <NGrid :cols="4" :x-gap="16" :y-gap="16" responsive="screen" style="margin-top: 24px;">
      <NGridItem>
        <div class="metric-card" style="border-left: 3px solid var(--warning);">
          <div class="metric-label">VaR 95%</div>
          <div class="metric-value" style="color: var(--warning);">{{ formatPercent(riskMetrics.var95) }}</div>
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px;">單日最大損失</div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card" style="border-left: 3px solid var(--danger);">
          <div class="metric-label">VaR 99%</div>
          <div class="metric-value" style="color: var(--danger);">{{ formatPercent(riskMetrics.var99) }}</div>
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px;">極端風險</div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card" style="border-left: 3px solid var(--warning);">
          <div class="metric-label">ES 95%</div>
          <div class="metric-value" style="color: var(--warning);">{{ formatPercent(riskMetrics.es95) }}</div>
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px;">預期短缺</div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card" style="border-left: 3px solid var(--info);">
          <div class="metric-label">波動率</div>
          <div class="metric-value" style="color: var(--info);">{{ riskMetrics.volatility }}%</div>
          <div style="color: var(--text-tertiary); font-size: 12px; margin-top: 4px;">年化標準差</div>
        </div>
      </NGridItem>
    </NGrid>

    <!-- Charts -->
    <NGrid :cols="2" :x-gap="16" :y-gap="16" responsive="screen" style="margin-top: 24px;">
      <NGridItem>
        <div class="glass-panel">
          <h2 style="margin-bottom: 20px;">損失分佈</h2>
          <VChart :option="lossDistributionOption" style="height: 320px;" autoresize />
          <div style="display: flex; gap: 16px; margin-top: 16px; justify-content: center;">
            <div style="display: flex; align-items: center; gap: 6px;">
              <div style="width: 12px; height: 12px; border-radius: 2px; background: rgba(96, 165, 250, 0.6);"></div>
              <span style="color: var(--text-secondary); font-size: 12px;">正常區間</span>
            </div>
            <div style="display: flex; align-items: center; gap: 6px;">
              <div style="width: 12px; height: 12px; border-radius: 2px; background: #fbbf24;"></div>
              <span style="color: var(--text-secondary); font-size: 12px;">VaR 95%</span>
            </div>
            <div style="display: flex; align-items: center; gap: 6px;">
              <div style="width: 12px; height: 12px; border-radius: 2px; background: #f87171;"></div>
              <span style="color: var(--text-secondary); font-size: 12px;">VaR 99%</span>
            </div>
          </div>
        </div>
      </NGridItem>

      <NGridItem>
        <div class="glass-panel">
          <h2 style="margin-bottom: 20px;">歷史 VaR 趨勢</h2>
          <VChart :option="historicalVarOption" style="height: 320px;" autoresize />
        </div>
      </NGridItem>
    </NGrid>

    <!-- Model Assumptions -->
    <div class="glass-panel" style="margin-top: 24px;">
      <h2 style="margin-bottom: 20px;">模型假設與參數</h2>
      <NGrid :cols="3" :x-gap="16" :y-gap="12" responsive="screen">
        <NGridItem v-for="assumption in assumptions" :key="assumption.name">
          <div style="padding: 16px; background: rgba(255,255,255,0.03); border-radius: 12px;">
            <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 4px;">{{ assumption.name }}</div>
            <div style="color: var(--text-primary); font-weight: 600;">{{ assumption.value }}</div>
          </div>
        </NGridItem>
      </NGrid>
    </div>
  </main>
</template>

<style scoped>
:deep(.n-steps .n-step-indicator) {
  background: var(--bg-tertiary) !important;
  border-color: var(--border-medium) !important;
}

:deep(.n-steps .n-step-splitor) {
  background: var(--border-medium) !important;
}

:deep(.n-steps .n-step-content__title) {
  color: var(--text-primary) !important;
}

:deep(.n-steps .n-step-content__description) {
  color: var(--text-secondary) !important;
}

:deep(.n-step--finish .n-step-indicator) {
  background: var(--success) !important;
  border-color: var(--success) !important;
}

:deep(.n-step--process .n-step-indicator) {
  background: var(--accent-primary) !important;
  border-color: var(--accent-primary) !important;
}
</style>
