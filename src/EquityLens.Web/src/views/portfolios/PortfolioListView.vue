<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NInput,
  NTag,
  NEmpty,
  NModal,
  NForm,
  NFormItem,
  NSelect,
  NSpace,
  NIcon,
  NPopconfirm,
} from 'naive-ui'
import {
  AddOutline,
  SearchOutline,
  FolderOpenOutline,
  TrashOutline,
  TrendingUpOutline,
  TrendingDownOutline,
  ArrowForwardOutline,
} from '@vicons/ionicons5'

const router = useRouter()

// Search
const searchQuery = ref('')

// Create modal
const showCreateModal = ref(false)
const createForm = ref({
  name: '',
  description: '',
  strategy: null as string | null,
})

const strategyOptions = [
  { label: '成長型', value: 'growth' },
  { label: '價值型', value: 'value' },
  { label: '平衡型', value: 'balanced' },
  { label: '收益型', value: 'income' },
]

// Mock portfolios
const portfolios = ref([
  {
    id: '1',
    name: '科技成長型投資組合',
    description: '聚焦科技與創新產業的高成長股票',
    strategy: 'growth',
    holdings: 12,
    marketValue: 2450000,
    unrealizedPL: 185000,
    plPercent: 8.2,
    lastUpdated: '2026-06-03',
  },
  {
    id: '2',
    name: '價值型藍籌股組合',
    description: '低估值、高股息的優質藍籌股',
    strategy: 'value',
    holdings: 8,
    marketValue: 1800000,
    unrealizedPL: 72000,
    plPercent: 4.2,
    lastUpdated: '2026-06-02',
  },
  {
    id: '3',
    name: '全球平衡型組合',
    description: '跨地域、跨產業的分散化投資',
    strategy: 'balanced',
    holdings: 15,
    marketValue: 3200000,
    unrealizedPL: -45000,
    plPercent: -1.4,
    lastUpdated: '2026-06-01',
  },
])

const filteredPortfolios = computed(() => {
  if (!searchQuery.value) return portfolios.value
  const q = searchQuery.value.toLowerCase()
  return portfolios.value.filter(p =>
    p.name.toLowerCase().includes(q) ||
    p.description.toLowerCase().includes(q)
  )
})

function formatCurrency(value: number): string {
  return new Intl.NumberFormat('zh-TW', {
    style: 'currency',
    currency: 'TWD',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
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

function getStrategyColor(strategy: string): 'info' | 'success' | 'warning' | 'default' {
  const map: Record<string, 'info' | 'success' | 'warning' | 'default'> = {
    growth: 'info',
    value: 'success',
    balanced: 'warning',
    income: 'default',
  }
  return map[strategy] || 'default'
}

function navigateToDetail(id: string) {
  router.push({ name: 'portfolio-detail', params: { id } })
}

function handleCreate() {
  // TODO: API call
  const newPortfolio = {
    id: String(portfolios.value.length + 1),
    name: createForm.value.name,
    description: createForm.value.description,
    strategy: createForm.value.strategy || 'balanced',
    holdings: 0,
    marketValue: 0,
    unrealizedPL: 0,
    plPercent: 0,
    lastUpdated: new Date().toISOString().split('T')[0],
  }
  portfolios.value.unshift(newPortfolio)
  showCreateModal.value = false
  createForm.value = { name: '', description: '', strategy: null }
}

function handleDelete(id: string) {
  portfolios.value = portfolios.value.filter(p => p.id !== id)
}
</script>

<template>
  <main class="page animate-fade-in">
    <!-- Header -->
    <section class="page-heading">
      <div>
        <p class="eyebrow">Portfolio Management</p>
        <h1>投資組合</h1>
      </div>
      <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
        <template #icon>
          <NIcon><AddOutline /></NIcon>
        </template>
        建立投資組合
      </NButton>
    </section>

    <!-- Search -->
    <div class="glass-panel" style="padding: 16px 20px; margin-top: 0;">
      <NInput
        v-model:value="searchQuery"
        placeholder="搜尋投資組合..."
        clearable
        style="max-width: 400px;"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
    </div>

    <!-- Portfolio Grid -->
    <div v-if="filteredPortfolios.length > 0" style="margin-top: 24px;">
      <NGrid :cols="3" :x-gap="16" :y-gap="16" responsive="screen">
        <NGridItem v-for="portfolio in filteredPortfolios" :key="portfolio.id">
          <div
            class="glass-card"
            style="padding: 24px; cursor: pointer; position: relative; overflow: hidden;"
            @click="navigateToDetail(portfolio.id)"
          >
            <!-- Strategy Tag -->
            <div style="position: absolute; top: 16px; right: 16px;">
              <NTag size="small" :type="getStrategyColor(portfolio.strategy)" round>
                {{ getStrategyLabel(portfolio.strategy) }}
              </NTag>
            </div>

            <!-- Icon & Name -->
            <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 16px;">
              <div
                style="
                  width: 48px;
                  height: 48px;
                  border-radius: 12px;
                  display: grid;
                  place-items: center;
                  background: linear-gradient(135deg, rgba(96, 165, 250, 0.2), rgba(129, 140, 248, 0.2));
                  border: 1px solid rgba(96, 165, 250, 0.2);
                "
              >
                <NIcon :size="24" color="#60a5fa">
                  <FolderOpenOutline />
                </NIcon>
              </div>
              <div>
                <h3 style="margin: 0; font-size: 16px; font-weight: 600;">{{ portfolio.name }}</h3>
                <p style="margin: 4px 0 0; color: var(--text-tertiary); font-size: 13px;">
                  {{ portfolio.holdings }} 檔持股
                </p>
              </div>
            </div>

            <!-- Description -->
            <p style="color: var(--text-secondary); font-size: 13px; margin-bottom: 20px; line-height: 1.5;">
              {{ portfolio.description }}
            </p>

            <!-- Metrics -->
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 20px;">
              <div>
                <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 4px;">市值</div>
                <div style="color: var(--text-primary); font-size: 18px; font-weight: 700;">
                  {{ formatCurrency(portfolio.marketValue) }}
                </div>
              </div>
              <div>
                <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 4px;">未實現損益</div>
                <div
                  style="font-size: 18px; font-weight: 700;"
                  :style="{ color: portfolio.plPercent >= 0 ? 'var(--success)' : 'var(--danger)' }"
                >
                  <NIcon :size="16" style="vertical-align: middle; margin-right: 4px;">
                    <component :is="portfolio.plPercent >= 0 ? TrendingUpOutline : TrendingDownOutline" />
                  </NIcon>
                  {{ portfolio.plPercent > 0 ? '+' : '' }}{{ portfolio.plPercent }}%
                </div>
              </div>
            </div>

            <!-- Footer -->
            <div style="display: flex; align-items: center; justify-content: space-between; padding-top: 16px; border-top: 1px solid var(--border-subtle);">
              <span style="color: var(--text-tertiary); font-size: 12px;">
                更新於 {{ portfolio.lastUpdated }}
              </span>
              <NSpace>
                <NPopconfirm @positive-click="handleDelete(portfolio.id)">
                  <template #trigger>
                    <NButton text type="error" size="small" @click.stop>
                      <template #icon>
                        <NIcon><TrashOutline /></NIcon>
                      </template>
                    </NButton>
                  </template>
                  確定要刪除此投資組合嗎？
                </NPopconfirm>
                <NButton text type="primary" size="small" @click.stop="navigateToDetail(portfolio.id)">
                  查看
                  <template #icon>
                    <NIcon><ArrowForwardOutline /></NIcon>
                  </template>
                </NButton>
              </NSpace>
            </div>
          </div>
        </NGridItem>
      </NGrid>
    </div>

    <!-- Empty State -->
    <div v-else class="empty-state">
      <NEmpty description="尚無投資組合">
        <template #extra>
          <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
            建立第一個投資組合
          </NButton>
        </template>
      </NEmpty>
    </div>

    <!-- Create Modal -->
    <NModal
      v-model:show="showCreateModal"
      title="建立投資組合"
      preset="card"
      style="width: 480px;"
      :bordered="false"
    >
      <NForm :model="createForm" label-placement="top">
        <NFormItem label="名稱" required>
          <NInput v-model:value="createForm.name" placeholder="輸入投資組合名稱" />
        </NFormItem>
        <NFormItem label="描述">
          <NInput
            v-model:value="createForm.description"
            type="textarea"
            placeholder="描述此投資組合的策略與目標"
            :rows="3"
          />
        </NFormItem>
        <NFormItem label="策略類型">
          <NSelect
            v-model:value="createForm.strategy"
            :options="strategyOptions"
            placeholder="選擇策略類型"
          />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showCreateModal = false">取消</NButton>
          <NButton type="primary" class="btn-primary" @click="handleCreate">建立</NButton>
        </NSpace>
      </template>
    </NModal>
  </main>
</template>

<style scoped>
:deep(.n-input) {
  background: rgba(255, 255, 255, 0.06) !important;
}

:deep(.n-input__input-el) {
  color: var(--text-primary) !important;
}

:deep(.n-input__placeholder) {
  color: var(--text-tertiary) !important;
}
</style>
