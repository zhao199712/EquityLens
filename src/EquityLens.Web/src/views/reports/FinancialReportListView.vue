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
  DocumentTextOutline,
  TimeOutline,
  ArrowForwardOutline,
  TrashOutline,
} from '@vicons/ionicons5'

const router = useRouter()

const searchQuery = ref('')
const showCreateModal = ref(false)
const createForm = ref({
  name: '',
  company: '',
  type: 'ai-memo',
})

const typeOptions = [
  { label: 'AI Memo', value: 'ai-memo' },
  { label: 'Financial Report', value: 'financial-report' },
  { label: 'Risk Report', value: 'risk-report' },
]

const reports = ref([
  {
    id: '1',
    name: 'TSMC 2025 Q1 財報分析',
    company: 'Taiwan Semiconductor Manufacturing Co.',
    ticker: 'TSMC',
    type: 'ai-memo',
    confidence: '高',
    date: '2026-06-03',
  },
  {
    id: '2',
    name: 'Apple FY2025 半年報',
    company: 'Apple Inc.',
    ticker: 'AAPL',
    type: 'financial-report',
    confidence: '中',
    date: '2026-06-02',
  },
  {
    id: '3',
    name: 'NVIDIA 風險評估報告',
    company: 'NVIDIA Corp.',
    ticker: 'NVDA',
    type: 'risk-report',
    confidence: '高',
    date: '2026-06-01',
  },
])

const filteredReports = computed(() => {
  if (!searchQuery.value) return reports.value
  const q = searchQuery.value.toLowerCase()
  return reports.value.filter(r =>
    r.name.toLowerCase().includes(q) ||
    r.company.toLowerCase().includes(q) ||
    r.ticker.toLowerCase().includes(q)
  )
})

function getTypeColor(type: string): 'info' | 'success' | 'warning' | 'default' {
  const map: Record<string, 'info' | 'success' | 'warning' | 'default'> = {
    'ai-memo': 'success',
    'financial-report': 'info',
    'risk-report': 'warning',
  }
  return map[type] || 'default'
}

function getTypeLabel(type: string): string {
  const map: Record<string, string> = {
    'ai-memo': 'AI Memo',
    'financial-report': 'Financial Report',
    'risk-report': 'Risk Report',
  }
  return map[type] || type
}

function getConfidenceColor(confidence: string): 'success' | 'warning' | 'error' | 'default' {
  const map: Record<string, 'success' | 'warning' | 'error' | 'default'> = {
    '高': 'success',
    '中': 'warning',
    '低': 'error',
  }
  return map[confidence] || 'default'
}

function navigateToDetail(id: string) {
  router.push({ name: 'financial-report-detail', params: { id } })
}

function handleCreate() {
  const newReport = {
    id: String(reports.value.length + 1),
    name: createForm.value.name,
    company: createForm.value.company,
    ticker: createForm.value.company.slice(0, 4).toUpperCase(),
    type: createForm.value.type,
    confidence: '中',
    date: new Date().toISOString().split('T')[0],
  }
  reports.value.unshift(newReport)
  showCreateModal.value = false
  createForm.value = { name: '', company: '', type: 'ai-memo' }
}

function handleDelete(id: string) {
  reports.value = reports.value.filter(r => r.id !== id)
}
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">Financial Reports</p>
        <h1>財報分析</h1>
      </div>
      <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
        <template #icon>
          <NIcon><AddOutline /></NIcon>
        </template>
        新增報告
      </NButton>
    </section>

    <div class="glass-panel" style="padding: 16px 20px; margin-top: 0;">
      <NInput
        v-model:value="searchQuery"
        placeholder="搜尋報告..."
        clearable
        style="max-width: 400px;"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
    </div>

    <div v-if="filteredReports.length > 0" style="margin-top: 24px;">
      <NGrid :cols="3" :x-gap="16" :y-gap="16" responsive="screen">
        <NGridItem v-for="report in filteredReports" :key="report.id">
          <div
            class="glass-card"
            style="padding: 24px; cursor: pointer; position: relative; overflow: hidden;"
            @click="navigateToDetail(report.id)"
          >
            <div style="position: absolute; top: 16px; right: 16px;">
              <NTag size="small" :type="getTypeColor(report.type)" round>
                {{ getTypeLabel(report.type) }}
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
                  <DocumentTextOutline />
                </NIcon>
              </div>
              <div>
                <h3 style="margin: 0; font-size: 16px; font-weight: 600;">{{ report.name }}</h3>
                <p style="margin: 4px 0 0; color: var(--text-tertiary); font-size: 13px;">
                  {{ report.company }} ({{ report.ticker }})
                </p>
              </div>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 20px;">
              <div>
                <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 4px;">信心</div>
                <NTag size="small" :type="getConfidenceColor(report.confidence)" round>
                  {{ report.confidence }}
                </NTag>
              </div>
              <div>
                <div style="color: var(--text-tertiary); font-size: 12px; margin-bottom: 4px;">日期</div>
                <div style="color: var(--text-primary); font-size: 18px; font-weight: 700;">{{ report.date }}</div>
              </div>
            </div>

            <div style="display: flex; align-items: center; justify-content: space-between; padding-top: 16px; border-top: 1px solid var(--border-subtle);">
              <span style="color: var(--text-tertiary); font-size: 12px;">
                <NIcon :size="14" style="vertical-align: middle; margin-right: 4px;"><TimeOutline /></NIcon>
                {{ report.date }}
              </span>
              <NSpace>
                <NPopconfirm @positive-click="handleDelete(report.id)">
                  <template #trigger>
                    <NButton text type="error" size="small" @click.stop>
                      <template #icon>
                        <NIcon><TrashOutline /></NIcon>
                      </template>
                    </NButton>
                  </template>
                  確定要刪除此報告嗎？
                </NPopconfirm>
                <NButton text type="primary" size="small" @click.stop="navigateToDetail(report.id)">
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
      <NEmpty description="尚無財報分析">
        <template #extra>
          <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
            新增第一份報告
          </NButton>
        </template>
      </NEmpty>
    </div>

    <NModal
      v-model:show="showCreateModal"
      title="新增財報分析"
      preset="card"
      style="width: 480px;"
      :bordered="false"
    >
      <NForm :model="createForm" label-placement="top">
        <NFormItem label="名稱" required>
          <NInput v-model:value="createForm.name" placeholder="輸入報告名稱" />
        </NFormItem>
        <NFormItem label="公司">
          <NInput v-model:value="createForm.company" placeholder="輸入公司名稱" />
        </NFormItem>
        <NFormItem label="類型">
          <NSelect
            v-model:value="createForm.type"
            :options="typeOptions"
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
