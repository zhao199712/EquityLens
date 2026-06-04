<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NTag,
  NTabs,
  NTabPane,
  NProgress,
  NIcon,
  NSpace,
  NModal,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NPopconfirm,
} from 'naive-ui'
import {
  ArrowBackOutline,
  AddOutline,
  TrashOutline,
  ShieldCheckmarkOutline,
  FlashOutline,
  WarningOutline,
} from '@vicons/ionicons5'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { PieChart, BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import VChart from 'vue-echarts'

use([CanvasRenderer, PieChart, BarChart, GridComponent, TooltipComponent, LegendComponent])

const route = useRoute()
const router = useRouter()
const portfolioId = route.params.id as string

// Mock portfolio data
const portfolio = ref({
  id: portfolioId,
  name: '科技成長型投資組合',
  description: '聚焦科技與創新產業的高成長股票',
  strategy: 'growth',
  marketValue: 2450000,
  unrealizedPL: 185000,
  plPercent: 8.2,
  totalReturn: 12.5,
  volatility: 18.3,
  sharpeRatio: 1.42,
  maxDrawdown: -15.2,
  lastUpdated: '2026-06-03',
})

// Holdings
const holdings = ref([
  { symbol: 'AAPL', name: 'Apple Inc.', shares: 150, price: 195.50, value: 293250, weight: 12.0, pl: 25250, plPercent: 9.4 },
  { symbol: 'MSFT', name: 'Microsoft Corp.', shares: 100, price: 420.30, value: 420300, weight: 17.1, pl: 45300, plPercent: 12.1 },
  { symbol: 'NVDA', name: 'NVIDIA Corp.', shares: 80, price: 1250.00, value: 1000000, weight: 40.8, pl: 180000, plPercent: 22.0 },
  { symbol: 'GOOGL', name: 'Alphabet Inc.', shares: 200, price: 175.80, value: 351600, weight: 14.3, pl: -8400, plPercent: -2.3 },
  { symbol: 'TSLA', name: 'Tesla Inc.', shares: 120, price: 245.60, value: 294720, weight: 12.0, pl: -17280, plPercent: -5.5 },
  { symbol: 'AMZN', name: 'Amazon.com Inc.', shares: 50, price: 185.40, value: 92700, weight: 3.8, pl: -3900, plPercent: -4.0 },
])

// Allocation data for pie chart
const allocationData = computed(() =>
  holdings.value.map(h => ({
    name: h.symbol,
    value: h.weight,
  }))
)

const pieChartOption = computed(() => ({
  backgroundColor: 'transparent',
  tooltip: {
    trigger: 'item',
    backgroundColor: 'rgba(17, 24, 39, 0.9)',
    borderColor: 'rgba(255, 255, 255, 0.1)',
    textStyle: { color: '#f0f4f8' },
    formatter: '{b}: {c}%',
  },
  legend: {
    orient: 'vertical',
    right: '5%',
    top: 'center',
    textStyle: { color: '#94a3b8' },
  },
  series: [
    {
      type: 'pie',
      radius: ['45%', '75%'],
      center: ['35%', '50%'],
      avoidLabelOverlap: false,
      itemStyle: {
        borderRadius: 8,
        borderColor: '#0a0e1a',
        borderWidth: 2,
      },
      label: { show: false },
      emphasis: {
        label: {
          show: true,
          fontSize: 14,
          fontWeight: 'bold',
          color: '#f0f4f8',
        },
      },
      data: allocationData.value,
      color: ['#60a5fa', '#34d399', '#fbbf24', '#c084fc', '#f87171', '#fb923c'],
    },
  ],
}))

// Risk metrics bar chart
const riskChartOption = computed(() => ({
  backgroundColor: 'transparent',
  grid: {
    left: '3%',
    right: '8%',
    bottom: '3%',
    top: '5%',
    containLabel: true,
  },
  tooltip: {
    trigger: 'axis',
    backgroundColor: 'rgba(17, 24, 39, 0.9)',
    borderColor: 'rgba(255, 255, 255, 0.1)',
    textStyle: { color: '#f0f4f8' },
  },
  xAxis: {
    type: 'value',
    axisLine: { show: false },
    splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
    axisLabel: { color: '#64748b', formatter: '{value}%' },
  },
  yAxis: {
    type: 'category',
    data: ['Max Drawdown', 'Volatility', 'Total Return'],
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: '#94a3b8' },
  },
  series: [
    {
      type: 'bar',
      data: [
        { value: Math.abs(portfolio.value.maxDrawdown), itemStyle: { color: '#f87171' } },
        { value: portfolio.value.volatility, itemStyle: { color: '#fbbf24' } },
        { value: portfolio.value.totalReturn, itemStyle: { color: '#34d399' } },
      ],
      barWidth: '50%',
      itemStyle: { borderRadius: [0, 4, 4, 0] },
      label: {
        show: true,
        position: 'right',
        color: '#f0f4f8',
        formatter: (params: any) => {
          const val = params.dataIndex === 0 ? -params.value : params.value
          return `${val}%`
        },
      },
    },
  ],
}))

// Scenarios
const scenarios = ref([
  { id: '1', name: '市場下跌 10%', type: 'market', impact: -8.5, affectedHoldings: 4 },
  { id: '2', name: '科技板塊回調', type: 'sector', impact: -12.3, affectedHoldings: 3 },
  { id: '3', name: '單一股票衝擊 (NVDA)', type: 'single', impact: -8.2, affectedHoldings: 1 },
])

// Add holding modal
const showAddHoldingModal = ref(false)
const newHolding = ref({
  symbol: '',
  shares: 0,
  price: 0,
})

function formatCurrency(value: number): string {
  return new Intl.NumberFormat('zh-TW', {
    style: 'currency',
    currency: 'TWD',
    minimumFractionDigits: 0,
  }).format(value)
}

function getStrategyLabel(strategy: string): string {
  const map: Record<string, string> = {
    growth: '成長型',
    value: '價值型',
    balanced: '平衡型',
    income: '收益型',
  }
  return map[strategy] || strategy
}

function handleAddHolding() {
  const value = newHolding.value.shares * newHolding.value.price
  const totalValue = holdings.value.reduce((sum, h) => sum + h.value, 0) + value
  holdings.value.push({
    symbol: newHolding.value.symbol,
    name: newHolding.value.symbol,
    shares: newHolding.value.shares,
    price: newHolding.value.price,
    value,
    weight: Number(((value / totalValue) * 100).toFixed(1)),
    pl: 0,
    plPercent: 0,
  })
  showAddHoldingModal.value = false
  newHolding.value = { symbol: '', shares: 0, price: 0 }
}

function handleDeleteHolding(symbol: string) {
  holdings.value = holdings.value.filter(h => h.symbol !== symbol)
}

function goBack() {
  router.push({ name: 'portfolios' })
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
        返回投資組合列表
      </NButton>
    </div>

    <!-- Header -->
    <section class="page-heading">
      <div>
        <p class="eyebrow">Portfolio Detail</p>
        <h1>{{ portfolio.name }}</h1>
      </div>
      <NSpace>
        <NTag type="info" round>{{ getStrategyLabel(portfolio.strategy) }}</NTag>
        <NButton type="primary" class="btn-primary" size="small">
          <template #icon>
            <NIcon><FlashOutline /></NIcon>
          </template>
          執行風險分析
        </NButton>
      </NSpace>
    </section>

    <!-- Overview Cards -->
    <NGrid :cols="4" :x-gap="16" :y-gap="16" responsive="screen">
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">總市值</div>
          <div class="metric-value">{{ formatCurrency(portfolio.marketValue) }}</div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">未實現損益</div>
          <div class="metric-value" :style="{ color: portfolio.plPercent >= 0 ? 'var(--success)' : 'var(--danger)' }">
            {{ portfolio.plPercent > 0 ? '+' : '' }}{{ portfolio.plPercent }}%
          </div>
          <div class="metric-change" :class="portfolio.plPercent >= 0 ? 'positive' : 'negative'">
            {{ formatCurrency(portfolio.unrealizedPL) }}
          </div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">波動率</div>
          <div class="metric-value">{{ portfolio.volatility }}%</div>
          <div class="metric-change" style="color: var(--text-tertiary);">年化標準差</div>
        </div>
      </NGridItem>
      <NGridItem>
        <div class="metric-card">
          <div class="metric-label">夏普比率</div>
          <div class="metric-value" :style="{ color: portfolio.sharpeRatio >= 1 ? 'var(--success)' : 'var(--warning)' }">
            {{ portfolio.sharpeRatio }}
          </div>
          <div class="metric-change" :class="portfolio.sharpeRatio >= 1 ? 'positive' : 'negative'">
            {{ portfolio.sharpeRatio >= 1 ? '優秀' : '一般' }}
          </div>
        </div>
      </NGridItem>
    </NGrid>

    <!-- Tabs -->
    <div class="glass-panel" style="margin-top: 24px; padding: 0;">
      <NTabs type="line" class="glass-tabs" style="padding: 20px 24px 0;">
        <NTabPane name="overview" tab="總覽">
          <div style="padding: 24px;">
            <NGrid :cols="2" :x-gap="24" responsive="screen">
              <NGridItem>
                <h3 style="margin-bottom: 16px;">資產配置</h3>
                <VChart :option="pieChartOption" style="height: 300px;" autoresize />
              </NGridItem>
              <NGridItem>
                <h3 style="margin-bottom: 16px;">風險指標</h3>
                <VChart :option="riskChartOption" style="height: 300px;" autoresize />
              </NGridItem>
            </NGrid>
          </div>
        </NTabPane>

        <NTabPane name="holdings" tab="持股明細">
          <div style="padding: 0 24px 24px;">
            <div style="display: flex; justify-content: flex-end; margin-bottom: 16px;">
              <NButton type="primary" class="btn-primary" size="small" @click="showAddHoldingModal = true">
                <template #icon>
                  <NIcon><AddOutline /></NIcon>
                </template>
                新增持股
              </NButton>
            </div>
            <div class="glass-table">
              <table style="width: 100%; border-collapse: collapse;">
                <thead>
                  <tr style="border-bottom: 1px solid var(--border-subtle);">
                    <th style="text-align: left; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">代號</th>
                    <th style="text-align: left; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">名稱</th>
                    <th style="text-align: right; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">股數</th>
                    <th style="text-align: right; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">價格</th>
                    <th style="text-align: right; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">市值</th>
                    <th style="text-align: right; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">權重</th>
                    <th style="text-align: right; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">損益</th>
                    <th style="text-align: center; padding: 12px 16px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">操作</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="holding in holdings"
                    :key="holding.symbol"
                    style="border-bottom: 1px solid var(--border-subtle);"
                    class="table-row-hover"
                  >
                    <td style="padding: 14px 16px; color: var(--accent-primary); font-weight: 600;">{{ holding.symbol }}</td>
                    <td style="padding: 14px 16px; color: var(--text-primary);">{{ holding.name }}</td>
                    <td style="padding: 14px 16px; color: var(--text-secondary); text-align: right;">{{ holding.shares }}</td>
                    <td style="padding: 14px 16px; color: var(--text-secondary); text-align: right;">{{ holding.price }}</td>
                    <td style="padding: 14px 16px; color: var(--text-primary); text-align: right; font-weight: 500;">{{ formatCurrency(holding.value) }}</td>
                    <td style="padding: 14px 16px; text-align: right;">
                      <NProgress
                        type="line"
                        :percentage="holding.weight"
                        :show-indicator="false"
                        :height="6"
                        :color="holding.weight > 20 ? '#f87171' : '#60a5fa'"
                        style="width: 80px; display: inline-block;"
                      />
                      <span style="color: var(--text-secondary); font-size: 12px; margin-left: 8px;">{{ holding.weight }}%</span>
                    </td>
                    <td style="padding: 14px 16px; text-align: right;">
                      <span :style="{ color: holding.plPercent >= 0 ? 'var(--success)' : 'var(--danger)', fontWeight: 600 }">
                        {{ holding.plPercent > 0 ? '+' : '' }}{{ holding.plPercent }}%
                      </span>
                    </td>
                    <td style="padding: 14px 16px; text-align: center;">
                      <NPopconfirm @positive-click="handleDeleteHolding(holding.symbol)">
                        <template #trigger>
                          <NButton text type="error" size="small">
                            <template #icon>
                              <NIcon><TrashOutline /></NIcon>
                            </template>
                          </NButton>
                        </template>
                        確定要刪除此持股嗎？
                      </NPopconfirm>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </NTabPane>

        <NTabPane name="risk" tab="風險分析">
          <div style="padding: 24px;">
            <NGrid :cols="2" :x-gap="24" responsive="screen">
              <NGridItem>
                <div class="glass-card" style="padding: 24px;">
                  <h3 style="margin-bottom: 20px;">風險值 (VaR)</h3>
                  <div style="display: grid; gap: 16px;">
                    <div style="display: flex; justify-content: space-between; align-items: center; padding: 16px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                      <div>
                        <div style="color: var(--text-secondary); font-size: 13px; margin-bottom: 4px;">VaR 95%</div>
                        <div style="color: var(--danger); font-size: 24px; font-weight: 700;">-2.3%</div>
                      </div>
                      <NIcon :size="32" color="#f87171"><WarningOutline /></NIcon>
                    </div>
                    <div style="display: flex; justify-content: space-between; align-items: center; padding: 16px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                      <div>
                        <div style="color: var(--text-secondary); font-size: 13px; margin-bottom: 4px;">VaR 99%</div>
                        <div style="color: var(--danger); font-size: 24px; font-weight: 700;">-3.8%</div>
                      </div>
                      <NIcon :size="32" color="#f87171"><WarningOutline /></NIcon>
                    </div>
                    <div style="display: flex; justify-content: space-between; align-items: center; padding: 16px; background: rgba(255,255,255,0.03); border-radius: 12px;">
                      <div>
                        <div style="color: var(--text-secondary); font-size: 13px; margin-bottom: 4px;">預期短缺 (ES 95%)</div>
                        <div style="color: var(--warning); font-size: 24px; font-weight: 700;">-3.1%</div>
                      </div>
                      <NIcon :size="32" color="#fbbf24"><ShieldCheckmarkOutline /></NIcon>
                    </div>
                  </div>
                </div>
              </NGridItem>
              <NGridItem>
                <div class="glass-card" style="padding: 24px;">
                  <h3 style="margin-bottom: 20px;">情境分析</h3>
                  <div style="display: grid; gap: 12px;">
                    <div
                      v-for="scenario in scenarios"
                      :key="scenario.id"
                      style="padding: 16px; background: rgba(255,255,255,0.03); border-radius: 12px; border-left: 3px solid var(--danger);"
                    >
                      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                        <span style="color: var(--text-primary); font-weight: 600;">{{ scenario.name }}</span>
                        <NTag size="small" type="error" round>{{ scenario.impact }}%</NTag>
                      </div>
                      <div style="color: var(--text-secondary); font-size: 13px;">
                        影響 {{ scenario.affectedHoldings }} 檔持股
                      </div>
                    </div>
                  </div>
                </div>
              </NGridItem>
            </NGrid>
          </div>
        </NTabPane>

        <NTabPane name="scenarios" tab="情境模擬">
          <div style="padding: 24px;">
            <div class="empty-state" style="min-height: 200px;">
              <NIcon :size="48" color="var(--text-muted)"><FlashOutline /></NIcon>
              <p style="color: var(--text-secondary); margin-top: 12px;">建立情境模擬以預測市場變化對投資組合的影響</p>
              <NButton type="primary" class="btn-primary" style="margin-top: 16px;">
                <template #icon>
                  <NIcon><AddOutline /></NIcon>
                </template>
                建立情境
              </NButton>
            </div>
          </div>
        </NTabPane>
      </NTabs>
    </div>

    <!-- Add Holding Modal -->
    <NModal
      v-model:show="showAddHoldingModal"
      title="新增持股"
      preset="card"
      style="width: 420px;"
      :bordered="false"
    >
      <NForm :model="newHolding" label-placement="top">
        <NFormItem label="股票代號" required>
          <NInput v-model:value="newHolding.symbol" placeholder="例如: AAPL" />
        </NFormItem>
        <NFormItem label="股數" required>
          <NInputNumber v-model:value="newHolding.shares" placeholder="輸入股數" :min="1" />
        </NFormItem>
        <NFormItem label="成本價" required>
          <NInputNumber v-model:value="newHolding.price" placeholder="輸入成本價" :min="0" :precision="2" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showAddHoldingModal = false">取消</NButton>
          <NButton type="primary" class="btn-primary" @click="handleAddHolding">新增</NButton>
        </NSpace>
      </template>
    </NModal>
  </main>
</template>

<style scoped>
.table-row-hover:hover {
  background: rgba(255, 255, 255, 0.04);
}

:deep(.n-tabs-tab) {
  font-weight: 500;
}

:deep(.n-tabs-tab--active) {
  font-weight: 600;
}
</style>
