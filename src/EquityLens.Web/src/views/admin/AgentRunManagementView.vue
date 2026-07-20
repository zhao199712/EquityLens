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
  NPopconfirm,
  NModal,
  useMessage,
  type DataTableColumns,
} from 'naive-ui'
import {
  RefreshOutline,
  ReloadOutline,
  CloseCircleOutline,
  EyeOutline,
} from '@vicons/ionicons5'
import { h } from 'vue'
import {
  listAgentRuns,
  retryAgentRun,
  cancelAgentRun,
  type AgentRunListItem,
} from '../../services/agentRuns'
import AgentRunAdminDetail from './components/AgentRunAdminDetail.vue'

const router = useRouter()
const route = useRoute()
const message = useMessage()

const runs = ref<AgentRunListItem[]>([])
const loading = ref(false)
const search = ref('')
const filterWorkflow = ref('')
const filterStatus = ref('')

const selectedRunId = computed(() => route.params.id as string | undefined)
const detailModalOpen = computed({
  get: () => !!selectedRunId.value,
  set: (value: boolean) => {
    if (!value) closeDetail()
  },
})

const filteredRuns = computed(() => {
  const q = search.value.trim().toLowerCase()
  return runs.value.filter((run) =>
    (!q || run.workflowType.toLowerCase().includes(q) || run.agentType.toLowerCase().includes(q) || run.id.toLowerCase().includes(q)) &&
    (!filterWorkflow.value || run.workflowType === filterWorkflow.value) &&
    (!filterStatus.value || run.status === filterStatus.value),
  )
})

const columns: DataTableColumns<AgentRunListItem> = [
  {
    title: 'Workflow',
    key: 'workflowType',
    width: 140,
    render(row) {
      return h(NTag, { size: 'small', type: 'default', bordered: true }, { default: () => row.workflowType })
    },
  },
  {
    title: 'Agent Type',
    key: 'agentType',
    width: 120,
    render(row) {
      return h(NTag, { size: 'small', type: 'default', bordered: true }, { default: () => row.agentType })
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
    title: 'ID',
    key: 'id',
    ellipsis: { tooltip: true },
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
    title: 'Started',
    key: 'startedAtUtc',
    width: 180,
    render(row) {
      return formatDate(row.startedAtUtc)
    },
  },
  {
    title: 'Completed',
    key: 'completedAtUtc',
    width: 180,
    render(row) {
      return formatDate(row.completedAtUtc)
    },
  },
  {
    title: 'Error',
    key: 'errorMessage',
    ellipsis: { tooltip: true },
  },
  {
    title: 'Actions',
    key: 'actions',
    width: 180,
    render(row) {
      return h(NSpace, { size: 'small' }, () => [
        h(NButton, { size: 'small', onClick: () => openDetail(row.id) }, { icon: () => h(NIcon, null, () => h(EyeOutline)) }),
        row.status === 'Failed'
          ? h(NButton, { size: 'small', type: 'warning', onClick: () => handleRetry(row.id) }, { icon: () => h(NIcon, null, () => h(ReloadOutline)) })
          : null,
        row.status === 'Pending' || row.status === 'Running'
          ? h(NPopconfirm, { onPositiveClick: () => handleCancel(row.id) }, {
              trigger: () => h(NButton, { size: 'small', type: 'error' }, { icon: () => h(NIcon, null, () => h(CloseCircleOutline)) }),
              default: () => '確定取消此 Agent Run？',
            })
          : null,
      ])
    },
  },
]

onMounted(loadRuns)

async function loadRuns() {
  loading.value = true
  try {
    runs.value = await listAgentRuns({ limit: 200 })
  } catch {
    message.error('無法載入 Agent Run 列表。')
  } finally {
    loading.value = false
  }
}

function openDetail(id: string) {
  router.push({ name: 'admin-agent-run-detail', params: { id } })
}

function closeDetail() {
  router.push({ name: 'admin-agent-runs' })
}

async function handleRetry(id: string) {
  try {
    await retryAgentRun(id)
    message.success('重試已送出。')
    await loadRuns()
  } catch {
    message.error('重試失敗。')
  }
}

async function handleCancel(id: string) {
  try {
    await cancelAgentRun(id)
    message.success('已取消。')
    await loadRuns()
  } catch {
    message.error('取消失敗。')
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

watch(() => route.params.id, () => {
  if (route.name === 'admin-agent-runs') {
    loadRuns()
  }
})
</script>

<template>
  <main class="prestige-section prestige-fade admin-view">
    <div class="prestige-section-head">
      <div>
        <p class="prestige-label view-eyebrow">Agent Runs</p>
        <h1 class="prestige-section-title">Agent Run 管理</h1>
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
          placeholder="搜尋 workflow / agent / ID..."
        />
        <select v-model="filterWorkflow" class="prestige-input admin-select">
          <option value="">全部 Workflow</option>
          <option value="CriticReview">CriticReview</option>
          <option value="DraftRevision">DraftRevision</option>
        </select>
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
        沒有符合條件的 Agent Run
      </div>
    </div>

    <NModal
      v-model:show="detailModalOpen"
      preset="card"
      title="Agent Run 詳情"
      style="width: 90vw; max-width: 1200px; max-height: 90vh; overflow: auto; --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c; --up: #7fa387; --down: #b05c5c; --panel-bg: rgba(201,168,106,.04); --serif: Georgia, 'Noto Serif TC', serif; --sans: 'Inter', 'Noto Sans TC', sans-serif"
      :mask-closable="false"
    >
      <AgentRunAdminDetail v-if="selectedRunId" :run-id="selectedRunId" />
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
  width: 300px;
  max-width: 100%;
}

.admin-select {
  width: 180px;
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

.admin-table-panel :deep(.n-data-table-thead) {
  background: transparent;
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
.admin-table-panel :deep(.n-tag) {
  background: transparent;
  color: var(--muted);
}

.admin-table-panel :deep(.n-tag .n-tag__border) {
  border-color: var(--gold-border-soft);
}

.admin-table-panel :deep(.n-tag--success-type) {
  color: var(--up);
}

.admin-table-panel :deep(.n-tag--success-type .n-tag__border) {
  border-color: rgba(127, 163, 135, 0.45);
}

.admin-table-panel :deep(.n-tag--error-type) {
  color: var(--down);
}

.admin-table-panel :deep(.n-tag--error-type .n-tag__border) {
  border-color: rgba(176, 92, 92, 0.45);
}

.admin-table-panel :deep(.n-tag--warning-type),
.admin-table-panel :deep(.n-tag--info-type) {
  color: #d4a24e;
}

.admin-table-panel :deep(.n-tag--warning-type .n-tag__border),
.admin-table-panel :deep(.n-tag--info-type .n-tag__border) {
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

.admin-table-panel :deep(.n-button--warning-type) {
  color: #d4a24e;
  border-color: rgba(212, 162, 78, 0.45);
}

.admin-table-panel :deep(.n-button--warning-type:hover) {
  background-color: rgba(212, 162, 78, 0.1);
  border-color: #d4a24e;
  color: #d4a24e;
}

.admin-table-panel :deep(.n-button--error-type) {
  color: var(--down);
  border-color: rgba(176, 92, 92, 0.45);
}

.admin-table-panel :deep(.n-button--error-type:hover) {
  background-color: rgba(176, 92, 92, 0.1);
  border-color: var(--down);
  color: var(--down);
}

/* ---- Spin ---- */
.admin-spin :deep(.n-spin-body) {
  color: var(--gold);
}
</style>
