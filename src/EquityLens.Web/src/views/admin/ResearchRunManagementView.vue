<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NTag,
  NSpin,
  NDataTable,
  NSpace,
  NIcon,
  NModal,
  useMessage,
  type DataTableColumns,
} from 'naive-ui'
import {
  RefreshOutline,
  EyeOutline,
} from '@vicons/ionicons5'
import { h } from 'vue'
import {
  listResearchRuns,
  getResearchRun,
  type ResearchRunSummary,
  type ResearchRunDetail,
} from '../../services/research'
import {
  createCriticReview,
  type AgentRunCreatedResponse,
} from '../../services/agentRuns'

const router = useRouter()
const route = useRoute()
const message = useMessage()

const runs = ref<ResearchRunSummary[]>([])
const loading = ref(false)
const search = ref('')
const filterStatus = ref('')
const selectedRunDetail = ref<ResearchRunDetail | null>(null)
const detailLoading = ref(false)
const detailModalOpen = ref(false)

const filteredRuns = computed(() => {
  const q = search.value.trim().toLowerCase()
  return runs.value.filter((run) =>
    (!q || run.ticker.toLowerCase().includes(q) || run.question.toLowerCase().includes(q) || run.id.toLowerCase().includes(q)) &&
    (!filterStatus.value || run.status === filterStatus.value),
  )
})

const columns: DataTableColumns<ResearchRunSummary> = [
  {
    title: 'Ticker',
    key: 'ticker',
    width: 120,
    render(row) {
      return h(NTag, { size: 'small', type: 'default', bordered: true }, { default: () => row.ticker })
    },
  },
  {
    title: 'Status',
    key: 'status',
    width: 120,
    render(row) {
      return h(NTag, { size: 'small', type: statusType(row.status), bordered: true }, { default: () => row.status })
    },
  },
  {
    title: 'Question',
    key: 'question',
    ellipsis: { tooltip: true },
  },
  {
    title: 'Citations',
    key: 'citationCount',
    width: 100,
  },
  {
    title: 'Latency',
    key: 'latencyMs',
    width: 120,
    render(row) {
      return `${row.latencyMs}ms`
    },
  },
  {
    title: 'Created',
    key: 'createdAtUtc',
    width: 180,
    render(row) {
      return formatDate(row.createdAtUtc)
    },
  },
  {
    title: 'Actions',
    key: 'actions',
    width: 220,
    render(row) {
      return h(NSpace, { size: 'small' }, () => [
        h(NButton, { size: 'small', onClick: () => openDetail(row.id) }, { icon: () => h(NIcon, null, () => h(EyeOutline)) }),
        h(NButton, { size: 'small', type: 'primary', onClick: () => handleCreateCriticReview(row.id) }, 'Critic Review'),
      ])
    },
  },
]

onMounted(loadRuns)

async function loadRuns() {
  loading.value = true
  try {
    runs.value = await listResearchRuns({ limit: 200 })
  } catch {
    message.error('無法載入 Research Run 列表。')
  } finally {
    loading.value = false
  }
}

async function openDetail(id: string) {
  detailLoading.value = true
  detailModalOpen.value = true
  try {
    selectedRunDetail.value = await getResearchRun(id)
  } catch {
    message.error('無法載入 Research Run 詳情。')
    detailModalOpen.value = false
  } finally {
    detailLoading.value = false
  }
}

function closeDetail() {
  detailModalOpen.value = false
  selectedRunDetail.value = null
  router.push({ name: 'admin-research-runs' })
}

async function handleCreateCriticReview(id: string) {
  try {
    const created: AgentRunCreatedResponse = await createCriticReview(id)
    message.success('Critic Review 已建立。')
    router.push({ name: 'admin-agent-run-detail', params: { id: created.id } })
  } catch {
    message.error('建立 Critic Review 失敗。')
  }
}

function formatDate(iso: string | null) {
  if (!iso) return '-'
  return new Date(iso).toLocaleString('zh-TW')
}

function statusType(status: string): 'success' | 'error' | 'warning' | 'info' | 'default' {
  switch (status) {
    case 'Succeeded': return 'success'
    case 'Failed': return 'error'
    case 'Running': return 'info'
    case 'Cancelled': return 'default'
    case 'Pending': return 'warning'
    default: return 'default'
  }
}

watch(() => route.params.id, (id) => {
  if (route.name === 'admin-research-runs' && id) {
    openDetail(id as string)
  }
})
</script>

<template>
  <main class="prestige-section prestige-fade admin-view">
    <div class="prestige-section-head">
      <div>
        <p class="prestige-label view-eyebrow">Research Runs</p>
        <h1 class="prestige-section-title">Research Run 管理</h1>
      </div>
      <NSpace>
        <button type="button" class="prestige-btn" @click="loadRuns">
          <NIcon size="16"><RefreshOutline /></NIcon>
          重新整理
        </button>
      </NSpace>
    </div>

    <div class="prestige-panel admin-filter-panel">
      <div class="admin-filter-row">
        <input
          v-model="search"
          class="prestige-input admin-search"
          placeholder="搜尋 ticker / question / ID..."
        />
        <select v-model="filterStatus" class="prestige-input admin-select">
          <option value="">全部狀態</option>
          <option value="Pending">Pending</option>
          <option value="Running">Running</option>
          <option value="Succeeded">Succeeded</option>
          <option value="Failed">Failed</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </div>
    </div>

    <div class="prestige-panel admin-table-panel">
      <NSpin :show="loading" class="admin-spin">
        <NDataTable
          :columns="columns"
          :data="filteredRuns"
          :bordered="false"
          :single-line="false"
          :row-key="(row) => row.id"
          :pagination="{ pageSize: 20 }"
        />
      </NSpin>
      <div v-if="!loading && filteredRuns.length === 0" class="prestige-empty admin-empty">
        沒有符合條件的 Research Run
      </div>
    </div>

    <NModal
      v-model:show="detailModalOpen"
      preset="card"
      title="Research Run 詳情"
      style="width: 90vw; max-width: 900px; max-height: 90vh; overflow: auto; --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c; --up: #7fa387; --down: #b05c5c; --panel-bg: rgba(201,168,106,.04); --serif: Georgia, 'Noto Serif TC', serif; --sans: 'Inter', 'Noto Sans TC', sans-serif"
      :mask-closable="false"
      @close="closeDetail"
    >
      <NSpin :show="detailLoading" class="admin-spin">
        <div v-if="selectedRunDetail" class="admin-modal-body">
          <div class="admin-field">
            <span class="admin-field-label">Ticker</span>
            <span class="admin-field-value prestige-mono">{{ selectedRunDetail.run.ticker }}</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Question</span>
            <span class="admin-field-value">{{ selectedRunDetail.run.question }}</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Status</span>
            <div>
              <NTag :type="statusType(selectedRunDetail.run.status)" size="small">
                {{ selectedRunDetail.run.status }}
              </NTag>
            </div>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Citations</span>
            <span class="admin-field-value prestige-mono">{{ selectedRunDetail.run.citationCount }}</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Latency</span>
            <span class="admin-field-value prestige-mono">{{ selectedRunDetail.run.latencyMs }}ms</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Answer</span>
            <div class="admin-answer">{{ selectedRunDetail.answer }}</div>
          </div>
        </div>
      </NSpin>
    </NModal>
  </main>
</template>

<style scoped>
.admin-view {
  padding-top: 8px;
}

.view-eyebrow {
  margin: 0 0 10px;
}

/* ---- Filter bar ---- */
.admin-filter-panel {
  padding: 16px 20px;
}

.admin-filter-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.admin-search {
  width: 320px;
  max-width: 100%;
}

.admin-select {
  width: 160px;
  max-width: 100%;
}

/* ---- Table panel ---- */
.admin-table-panel {
  margin-top: 24px;
  overflow: hidden;
}

.admin-empty {
  margin: 20px;
}

.admin-table-panel :deep(.n-data-table) {
  background: transparent;
  color: var(--ivory);
  font-size: 13px;
}

.admin-table-panel :deep(.n-data-table-th) {
  background: transparent;
  color: var(--gold);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  border-bottom: 1px solid var(--gold-border);
}

.admin-table-panel :deep(.n-data-table-td) {
  background: transparent;
  color: var(--ivory);
  border-bottom: 1px solid var(--gold-border-soft);
}

.admin-table-panel :deep(.n-data-table-tr:hover .n-data-table-td) {
  background: rgba(201, 168, 106, 0.05);
}

.admin-table-panel :deep(.n-data-table-empty) {
  color: var(--muted);
}

.admin-table-panel :deep(.n-pagination-item) {
  color: var(--muted);
  background: transparent;
  border-color: var(--gold-border-soft);
}

.admin-table-panel :deep(.n-pagination .n-pagination-item:hover),
.admin-table-panel :deep(.n-pagination .n-pagination-item--active) {
  color: var(--gold);
  border-color: var(--gold);
  background: rgba(201, 168, 106, 0.08);
}

/* ---- Tags ---- */
.admin-table-panel :deep(.n-tag),
.admin-modal-body :deep(.n-tag) {
  background: transparent;
  color: var(--muted);
}

.admin-table-panel :deep(.n-tag .n-tag__border),
.admin-modal-body :deep(.n-tag .n-tag__border) {
  border-color: var(--gold-border-soft);
}

.admin-table-panel :deep(.n-tag--success-type),
.admin-modal-body :deep(.n-tag--success-type) {
  color: var(--up);
}

.admin-table-panel :deep(.n-tag--success-type .n-tag__border),
.admin-modal-body :deep(.n-tag--success-type .n-tag__border) {
  border-color: rgba(127, 163, 135, 0.45);
}

.admin-table-panel :deep(.n-tag--error-type),
.admin-modal-body :deep(.n-tag--error-type) {
  color: var(--down);
}

.admin-table-panel :deep(.n-tag--error-type .n-tag__border),
.admin-modal-body :deep(.n-tag--error-type .n-tag__border) {
  border-color: rgba(176, 92, 92, 0.45);
}

.admin-table-panel :deep(.n-tag--warning-type),
.admin-table-panel :deep(.n-tag--info-type),
.admin-modal-body :deep(.n-tag--warning-type),
.admin-modal-body :deep(.n-tag--info-type) {
  color: #d4a24e;
}

.admin-table-panel :deep(.n-tag--warning-type .n-tag__border),
.admin-table-panel :deep(.n-tag--info-type .n-tag__border),
.admin-modal-body :deep(.n-tag--warning-type .n-tag__border),
.admin-modal-body :deep(.n-tag--info-type .n-tag__border) {
  border-color: rgba(212, 162, 78, 0.45);
}

/* ---- Table action buttons ---- */
.admin-table-panel :deep(.n-button) {
  background-color: transparent;
  color: var(--gold);
  border-color: var(--gold-border);
}

.admin-table-panel :deep(.n-button:hover) {
  background-color: rgba(201, 168, 106, 0.08);
  border-color: var(--gold);
  color: var(--gold-strong);
}

.admin-table-panel :deep(.n-button--primary-type) {
  background-color: var(--gold);
  border-color: var(--gold);
  color: #0b1220;
}

.admin-table-panel :deep(.n-button--primary-type:hover) {
  background-color: var(--gold-strong);
  border-color: var(--gold-strong);
  color: #0b1220;
}

/* ---- Spin ---- */
.admin-spin :deep(.n-spin-body) {
  color: var(--gold);
}

/* ---- Modal detail fields ---- */
.admin-field {
  margin-bottom: 16px;
}

.admin-field-label {
  display: block;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--gold);
  margin-bottom: 6px;
}

.admin-field-value {
  color: var(--ivory);
  font-size: 14px;
  line-height: 1.6;
}

.admin-answer {
  margin-top: 4px;
  padding: 12px 14px;
  background: rgba(11, 18, 32, 0.6);
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  color: var(--ivory);
  font-size: 13px;
  line-height: 1.8;
  white-space: pre-wrap;
}
</style>
