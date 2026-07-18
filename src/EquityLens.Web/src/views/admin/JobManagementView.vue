<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NInput,
  NSelect,
  NTag,
  NSpin,
  NDataTable,
  NSpace,
  NIcon,
  NPopconfirm,
  NModal,
  NForm,
  NFormItem,
  NDatePicker,
  useMessage,
  type DataTableColumns,
} from 'naive-ui'
import {
  RefreshOutline,
  AddOutline,
  CloseCircleOutline,
  EyeOutline,
} from '@vicons/ionicons5'
import {
  listAdminJobs,
  getAdminJob,
  createAdminJob,
  cancelAdminJob,
  type AdminJobRun,
  type CreateAdminJobRequest,
} from '../../services/adminJobs'
import type { SelectMixedOption } from 'naive-ui/es/select/src/interface'

interface JobTypeOption {
  label: string
  value: string
}

const router = useRouter()
const route = useRoute()
const message = useMessage()

const jobs = ref<AdminJobRun[]>([])
const loading = ref(false)
const search = ref('')
const filterJobType = ref('')
const filterStatus = ref('')
const selectedJobId = computed(() => route.params.id as string | undefined)
const selectedJob = ref<AdminJobRun | null>(null)
const detailLoading = ref(false)
const detailModalOpen = ref(false)
const createModalOpen = ref(false)

const createForm = ref<CreateAdminJobRequest>({
  jobType: 'ImportMarketPrices',
  from: undefined,
  to: undefined,
  ticker: '',
  exchange: 'TWSE',
})

const jobTypeOptions: JobTypeOption[] = [
  { label: '全部類型', value: '' },
  { label: '法說會匯入', value: 'ImportConferences' },
  { label: '法說會切 Chunk', value: 'ChunkConferences' },
  { label: '產生 Embedding', value: 'EmbedChunks' },
  { label: 'FinMind 財報匯入', value: 'ImportFinMindFinancials' },
  { label: 'FinMind 股息匯入', value: 'ImportFinMindDividends' },
  { label: 'MOPS 財報匯入', value: 'ImportMopsFinancials' },
  { label: 'TWSE 年報匯入', value: 'ImportTwseReportFiles' },
  { label: '股價匯入', value: 'ImportMarketPrices' },
  { label: '同步 0050 成分股價格', value: 'SyncTw0050Prices' },
]

const createJobTypeOptions = jobTypeOptions.filter((j) => j.value !== '')

const filteredJobs = computed(() => {
  const q = search.value.trim().toLowerCase()
  return jobs.value.filter((job) =>
    (!q || job.jobType.toLowerCase().includes(q) || job.id.toLowerCase().includes(q)) &&
    (!filterJobType.value || job.jobType === filterJobType.value) &&
    (!filterStatus.value || job.status === filterStatus.value),
  )
})

const columns: DataTableColumns<AdminJobRun> = [
  {
    title: 'Job Type',
    key: 'jobType',
    width: 200,
    render(row) {
      return h(NTag, { size: 'small', type: 'default', bordered: true }, { default: () => jobTypeLabel(row.jobType) })
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
    title: 'Progress',
    key: 'progressPercent',
    width: 120,
    render(row) {
      return `${row.progressPercent}%`
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
    width: 160,
    render(row) {
      return h(NSpace, { size: 'small' }, () => [
        h(NButton, { size: 'small', onClick: () => openDetail(row.id) }, { icon: () => h(NIcon, null, () => h(EyeOutline)) }),
        row.status === 'Queued' || row.status === 'Running'
          ? h(NPopconfirm, { onPositiveClick: () => handleCancel(row.id) }, {
              trigger: () => h(NButton, { size: 'small', type: 'error' }, { icon: () => h(NIcon, null, () => h(CloseCircleOutline)) }),
              default: () => '確定取消此工作？',
            })
          : null,
      ])
    },
  },
]

onMounted(() => {
  loadJobs()
  if (selectedJobId.value) {
    openDetail(selectedJobId.value)
  }
})

async function loadJobs() {
  loading.value = true
  try {
    jobs.value = await listAdminJobs({ limit: 200 })
  } catch {
    message.error('無法載入 Job 列表。')
  } finally {
    loading.value = false
  }
}

async function openDetail(id: string) {
  detailLoading.value = true
  detailModalOpen.value = true
  try {
    selectedJob.value = await getAdminJob(id)
  } catch {
    message.error('無法載入 Job 詳情。')
    detailModalOpen.value = false
  } finally {
    detailLoading.value = false
  }
}

function closeDetail() {
  detailModalOpen.value = false
  selectedJob.value = null
  router.push({ name: 'admin-jobs' })
}

async function handleCreate() {
  try {
    const created = await createAdminJob(createForm.value)
    message.success('Job 已建立。')
    createModalOpen.value = false
    await loadJobs()
    openDetail(created.id)
  } catch {
    message.error('建立 Job 失敗。')
  }
}

async function handleCancel(id: string) {
  try {
    await cancelAdminJob(id)
    message.success('已取消。')
    await loadJobs()
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
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Running': return 'info'
    case 'Cancelled': return 'default'
    case 'Queued': return 'warning'
    default: return 'default'
  }
}

function jobTypeLabel(jobType: string) {
  return jobTypeOptions.find((option) => option.value === jobType)?.label ?? jobType
}

function prettyJson(value: string | Record<string, unknown> | null | undefined): string {
  if (!value) return 'null'
  try {
    const parsed = typeof value === 'string' ? JSON.parse(value) : value
    return JSON.stringify(parsed, null, 2)
  } catch {
    return String(value)
  }
}

function resetCreateForm() {
  createForm.value = {
    jobType: 'ImportMarketPrices',
    from: undefined,
    to: undefined,
    ticker: '',
    exchange: 'TWSE',
  }
}

function needsDateRange(jobType: string) {
  return [
    'ImportFinMindFinancials',
    'ImportFinMindDividends',
    'ImportMopsFinancials',
    'ImportMarketPrices',
  ].includes(jobType)
}

function needsTicker(jobType: string) {
  return jobType === 'ImportMarketPrices'
}

watch(() => route.params.id, (id) => {
  if (route.name === 'admin-job-detail' && id) {
    openDetail(id as string)
  } else if (route.name === 'admin-jobs') {
    detailModalOpen.value = false
    selectedJob.value = null
  }
})
</script>

<template>
  <main class="prestige-section prestige-fade admin-view">
    <div class="prestige-section-head">
      <div>
        <p class="prestige-label view-eyebrow">Import Jobs</p>
        <h1 class="prestige-section-title">資料匯入工作管理</h1>
      </div>
      <NSpace>
        <button type="button" class="prestige-btn" @click="loadJobs">
          <NIcon size="16"><RefreshOutline /></NIcon>
          重新整理
        </button>
        <button type="button" class="prestige-btn prestige-btn-solid" @click="() => { resetCreateForm(); createModalOpen = true }">
          <NIcon size="16"><AddOutline /></NIcon>
          建立工作
        </button>
      </NSpace>
    </div>

    <div class="prestige-panel admin-filter-panel">
      <div class="admin-filter-row">
        <input
          v-model="search"
          class="prestige-input admin-search"
          placeholder="搜尋 job type / ID..."
        />
        <select v-model="filterJobType" class="prestige-input admin-select">
          <option v-for="option in jobTypeOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
        <select v-model="filterStatus" class="prestige-input admin-select">
          <option value="">全部狀態</option>
          <option value="Queued">Queued</option>
          <option value="Running">Running</option>
          <option value="Completed">Completed</option>
          <option value="Failed">Failed</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </div>
    </div>

    <div class="prestige-panel admin-table-panel">
      <NSpin :show="loading" class="admin-spin">
        <NDataTable
          :columns="columns"
          :data="filteredJobs"
          :bordered="false"
          :single-line="false"
          :row-key="(row) => row.id"
          :pagination="{ pageSize: 20 }"
        />
      </NSpin>
      <div v-if="!loading && filteredJobs.length === 0" class="prestige-empty admin-empty">
        沒有符合條件的 Job
      </div>
    </div>

    <!-- Detail Modal -->
    <NModal
      v-model:show="detailModalOpen"
      preset="card"
      title="Job 詳情"
      style="width: 90vw; max-width: 900px; max-height: 90vh; overflow: auto; --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c; --up: #7fa387; --down: #b05c5c; --panel-bg: rgba(201,168,106,.04); --serif: Georgia, 'Noto Serif TC', serif; --sans: 'Inter', 'Noto Sans TC', sans-serif"
      :mask-closable="false"
      @close="closeDetail"
    >
      <NSpin :show="detailLoading" class="admin-spin">
        <div v-if="selectedJob" class="admin-modal-body">
          <div class="admin-field">
            <span class="admin-field-label">ID</span>
            <span class="admin-field-value prestige-mono">{{ selectedJob.id }}</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Type</span>
            <span class="admin-field-value">{{ selectedJob.jobType }}</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Status</span>
            <div>
              <NTag :type="statusType(selectedJob.status)" size="small">{{ selectedJob.status }}</NTag>
            </div>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Progress</span>
            <span class="admin-field-value prestige-mono">{{ selectedJob.progressPercent }}%</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Created</span>
            <span class="admin-field-value prestige-mono">{{ formatDate(selectedJob.createdAtUtc) }}</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Started</span>
            <span class="admin-field-value prestige-mono">{{ formatDate(selectedJob.startedAtUtc) }}</span>
          </div>
          <div class="admin-field">
            <span class="admin-field-label">Completed</span>
            <span class="admin-field-value prestige-mono">{{ formatDate(selectedJob.completedAtUtc) }}</span>
          </div>
          <div v-if="selectedJob.errorMessage" class="admin-field">
            <span class="admin-field-label">Error</span>
            <span class="admin-field-value admin-down">{{ selectedJob.errorMessage }}</span>
          </div>
          <div v-if="selectedJob.payloadJson" class="admin-field">
            <span class="admin-field-label">Payload</span>
            <pre class="admin-json">{{ prettyJson(selectedJob.payloadJson) }}</pre>
          </div>
          <div v-if="selectedJob.resultJson" class="admin-field">
            <span class="admin-field-label">Result</span>
            <pre class="admin-json">{{ prettyJson(selectedJob.resultJson) }}</pre>
          </div>
        </div>
      </NSpin>
    </NModal>

    <!-- Create Modal -->
    <NModal
      v-model:show="createModalOpen"
      preset="card"
      title="建立資料匯入工作"
      style="width: 500px; --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c; --up: #7fa387; --down: #b05c5c; --panel-bg: rgba(201,168,106,.04); --serif: Georgia, 'Noto Serif TC', serif; --sans: 'Inter', 'Noto Sans TC', sans-serif"
      :mask-closable="false"
    >
      <div class="admin-modal-form">
        <NForm label-placement="left" label-width="120">
          <NFormItem label="Job Type">
            <NSelect
              v-model:value="createForm.jobType"
              :options="createJobTypeOptions as SelectMixedOption[]"
              :to="false"
            />
          </NFormItem>

          <NFormItem v-if="needsDateRange(createForm.jobType)" label="Date Range">
            <NSpace>
              <NDatePicker
                v-model:formatted-value="createForm.from"
                value-format="yyyy-MM-dd"
                placeholder="From"
              />
              <NDatePicker
                v-model:formatted-value="createForm.to"
                value-format="yyyy-MM-dd"
                placeholder="To"
              />
            </NSpace>
          </NFormItem>

          <NFormItem v-if="needsTicker(createForm.jobType)" label="Ticker">
            <NInput v-model:value="createForm.ticker" placeholder="例如 2330" />
          </NFormItem>

          <NFormItem v-if="needsTicker(createForm.jobType)" label="Exchange">
            <NSelect
              v-model:value="createForm.exchange"
              :options="[
                { label: 'TWSE', value: 'TWSE' },
                { label: 'TPEX', value: 'TPEX' },
                { label: 'NASDAQ', value: 'NASDAQ' },
                { label: 'NYSE', value: 'NYSE' },
              ]"
              style="width: 200px"
              :to="false"
            />
          </NFormItem>
        </NForm>
      </div>

      <template #footer>
        <NSpace justify="end">
          <button type="button" class="prestige-btn" @click="createModalOpen = false">取消</button>
          <button type="button" class="prestige-btn prestige-btn-solid" @click="handleCreate">建立</button>
        </NSpace>
      </template>
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
  width: 200px;
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

.admin-down {
  color: var(--down);
}

.admin-json {
  margin: 4px 0 0;
  padding: 12px 14px;
  background: rgba(11, 18, 32, 0.6);
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  color: var(--ivory);
  font-family: 'SFMono-Regular', Consolas, 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.6;
  overflow: auto;
}

/* ---- Modal form naive overrides ---- */
.admin-modal-form :deep(.n-form-item-label__text) {
  color: var(--muted);
  font-size: 12px;
  letter-spacing: 0.06em;
}

.admin-modal-form :deep(.n-input) {
  background-color: rgba(11, 18, 32, 0.6);
}

.admin-modal-form :deep(.n-input .n-input__input-el) {
  color: var(--ivory);
}

.admin-modal-form :deep(.n-input .n-input__input-el::placeholder) {
  color: var(--muted);
}

.admin-modal-form :deep(.n-input .n-input__border),
.admin-modal-form :deep(.n-input .n-input__state-border) {
  border-color: var(--gold-border-soft);
}

.admin-modal-form :deep(.n-input:hover .n-input__state-border) {
  border-color: var(--gold-border);
}

.admin-modal-form :deep(.n-input--focus .n-input__state-border) {
  border-color: var(--gold);
  box-shadow: 0 0 0 2px rgba(201, 168, 106, 0.18);
}

.admin-modal-form :deep(.n-base-selection) {
  background-color: rgba(11, 18, 32, 0.6);
  color: var(--ivory);
}

.admin-modal-form :deep(.n-base-selection .n-base-selection-label) {
  background-color: transparent;
  color: var(--ivory);
}

.admin-modal-form :deep(.n-base-selection .n-base-selection-placeholder) {
  color: var(--muted);
}

.admin-modal-form :deep(.n-base-selection .n-base-selection__border),
.admin-modal-form :deep(.n-base-selection .n-base-selection__state-border) {
  border-color: var(--gold-border-soft);
}

.admin-modal-form :deep(.n-base-selection:hover .n-base-selection__state-border),
.admin-modal-form :deep(.n-base-selection:focus-within .n-base-selection__state-border) {
  border-color: var(--gold);
}

.admin-modal-form :deep(.n-base-selection .n-base-arrow) {
  color: var(--muted);
}

.admin-modal-form :deep(.n-select-menu) {
  background-color: #101a2e;
  border: 1px solid var(--gold-border);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
}

.admin-modal-form :deep(.n-base-select-option) {
  color: var(--ivory);
  background: transparent;
}

.admin-modal-form :deep(.n-base-select-option:hover) {
  background: rgba(201, 168, 106, 0.08);
}

.admin-modal-form :deep(.n-base-select-option--selected) {
  color: var(--gold);
}

.admin-modal-form :deep(.n-base-select-option .n-base-select-option__check) {
  color: var(--gold);
}
</style>
