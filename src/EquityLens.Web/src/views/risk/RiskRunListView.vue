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
  NGrid,
  NGridItem,
} from 'naive-ui'
import {
  AddOutline,
  SearchOutline,
  ShieldCheckmarkOutline,
  TimeOutline,
  ArrowForwardOutline,
  TrashOutline,
} from '@vicons/ionicons5'

const router = useRouter()

const searchQuery = ref('')
const showCreateModal = ref(false)
const createForm = ref({
  name: '',
  portfolioId: null as string | null,
  model: 'historical',
})

const modelOptions = [
  { label: '歷史模擬法', value: 'historical' },
  { label: '參數法 (常態分配)', value: 'parametric' },
  { label: '蒙地卡羅模擬', value: 'monte-carlo' },
]

const portfolioOptions = [
  { label: '科技成長型投資組合', value: '1' },
  { label: '價值型藍籌股組合', value: '2' },
  { label: '全球平衡型組合', value: '3' },
]

const riskRuns = ref([
  {
    id: '1',
    name: '科技成長型投資組合 VaR 計算',
    portfolioName: '科技成長型投資組合',
    status: 'completed',
    model: 'historical',
    var95: '-2.34%',
    var99: '-3.87%',
    date: '2026-06-03',
  },
  {
    id: '2',
    name: '價值型藍籌股組合 VaR 計算',
    portfolioName: '價值型藍籌股組合',
    status: 'completed',
    model: 'historical',
    var95: '-1.52%',
    var99: '-2.71%',
    date: '2026-06-02',
  },
  {
    id: '3',
    name: '全球平衡型組合 VaR 計算',
    portfolioName: '全球平衡型組合',
    status: 'running',
    model: 'parametric',
    var95: '-',
    var99: '-',
    date: '2026-06-01',
  },
])

const filteredRuns = computed(() => {
  if (!searchQuery.value) return riskRuns.value
  const q = searchQuery.value.toLowerCase()
  return riskRuns.value.filter(r =>
    r.name.toLowerCase().includes(q) ||
    r.portfolioName.toLowerCase().includes(q)
  )
})

function getStatusType(status: string): 'success' | 'info' | 'warning' | 'error' {
  const map: Record<string, 'success' | 'info' | 'warning' | 'error'> = {
    completed: 'success',
    running: 'info',
    pending: 'warning',
    failed: 'error',
  }
  return map[status] || 'default'
}

function getStatusLabel(status: string): string {
  const map: Record<string, string> = {
    completed: '完成',
    running: '執行中',
    pending: '等待中',
    failed: '失敗',
  }
  return map[status] || status
}

function navigateToDetail(id: string) {
  router.push({ name: 'risk-run-detail', params: { id } })
}

function handleCreate() {
  const newRun = {
    id: String(riskRuns.value.length + 1),
    name: createForm.value.name,
    portfolioName: portfolioOptions.find(p => p.value === createForm.value.portfolioId)?.label || '未指定',
    status: 'pending',
    model: createForm.value.model,
    var95: '-',
    var99: '-',
    date: new Date().toISOString().split('T')[0],
  }
  riskRuns.value.unshift(newRun)
  showCreateModal.value = false
  createForm.value = { name: '', portfolioId: null, model: 'historical' }
}

function handleDelete(id: string) {
  riskRuns.value = riskRuns.value.filter(r => r.id !== id)
}
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">Risk Analysis</p>
        <h1>風險分析</h1>
      </div>
      <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
        <template #icon>
          <NIcon><AddOutline /></NIcon>
        </template>
        執行風險分析
      </NButton>
    </section>

    <div class="glass-panel" style="padding: 16px 20px; margin-top: 0;">
      <NInput
        v-model:value="searchQuery"
        placeholder="搜尋風險分析..."
        clearable
        style="max-width: 400px;"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
    </div>

    <div v-if="filteredRuns.length > 0" style="margin-top: 24px;">
      <NGrid :cols="3" :x-gap="16" :y-gap="16" responsive="screen">
        <NGridItem v-for="run in filteredRuns" :key="run.id">
          <div
            class="glass-card"
            style="padding: 24px; cursor: pointer; position: relative; overflow: hidden;"
            @click="navigateToDetail(run.id)"
          >
            <div style="position: absolute; top: 16px; right: 16px;">
              <NTag size="small" :type="getStatusType(run.status)" round>
                {{ getStatusLabel(run.status) }}
              </NTag>
            </div>

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
                  <ShieldCheckmarkOutline />
                </NIcon>
              </div>
              <div>
                <h3 style="margin: 0; font-size: 16px; font-weight: 600;">{{ run.name }}</h3>
                <p style="margin: 4px 0 0; color: var(--text-tertiary); font-size: 13px;">
                  {{ run.portfolioName }}
                </p>
              </div>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 20px;">
              <div>
                <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 4px;">VaR 95%</div>
                <div style="color: var(--text-primary); font-size: 18px; font-weight: 700;">{{ run.var95 }}</div>
              </div>
              <div>
                <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 4px;">模型</div>
                <div style="color: var(--text-primary); font-size: 18px; font-weight: 700;">{{ run.model }}</div>
              </div>
            </div>

            <div style="display: flex; align-items: center; justify-content: space-between; padding-top: 16px; border-top: 1px solid var(--border-subtle);">
              <span style="color: var(--text-tertiary); font-size: 12px;">
                <NIcon :size="14" style="vertical-align: middle; margin-right: 4px;"><TimeOutline /></NIcon>
                {{ run.date }}
              </span>
              <NSpace>
                <NPopconfirm @positive-click="handleDelete(run.id)">
                  <template #trigger>
                    <NButton text type="error" size="small" @click.stop>
                      <template #icon>
                        <NIcon><TrashOutline /></NIcon>
                      </template>
                    </NButton>
                  </template>
                  確定要刪除此風險分析嗎？
                </NPopconfirm>
                <NButton text type="primary" size="small" @click.stop="navigateToDetail(run.id)">
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

    <div v-else class="empty-state">
      <NEmpty description="尚無風險分析">
        <template #extra>
          <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
            執行第一個風險分析
          </NButton>
        </template>
      </NEmpty>
    </div>

    <NModal
      v-model:show="showCreateModal"
      title="執行風險分析"
      preset="card"
      style="width: 480px;"
      :bordered="false"
    >
      <NForm :model="createForm" label-placement="top">
        <NFormItem label="名稱" required>
          <NInput v-model:value="createForm.name" placeholder="輸入分析名稱" />
        </NFormItem>
        <NFormItem label="投資組合">
          <NSelect
            v-model:value="createForm.portfolioId"
            :options="portfolioOptions"
            placeholder="選擇投資組合"
          />
        </NFormItem>
        <NFormItem label="計算模型">
          <NSelect
            v-model:value="createForm.model"
            :options="modelOptions"
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
