<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NInput,
  NSelect,
  NTag,
  NSpin,
  NEmpty,
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
  SearchOutline,
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
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">IMPORT JOBS</p>
        <h1>資料匯入工作管理</h1>
      </div>
      <NSpace>
        <NButton type="primary" @click="loadJobs">
          <template #icon>
            <NIcon><RefreshOutline /></NIcon>
          </template>
          重新整理
        </NButton>
        <NButton type="primary" @click="() => { resetCreateForm(); createModalOpen = true }">
          <template #icon>
            <NIcon><AddOutline /></NIcon>
          </template>
          建立工作
        </NButton>
      </NSpace>
    </section>

    <div class="glass-panel" style="padding: 16px 20px;">
      <NSpace>
        <NInput
          v-model:value="search"
          placeholder="搜尋 job type / ID..."
          clearable
          style="width: 300px"
        >
          <template #prefix>
            <NIcon><SearchOutline /></NIcon>
          </template>
        </NInput>
        <NSelect
          v-model:value="filterJobType"
          :options="jobTypeOptions as SelectMixedOption[]"
          style="width: 200px"
        />
        <NSelect
          v-model:value="filterStatus"
          :options="[
            { label: '全部狀態', value: '' },
            { label: 'Queued', value: 'Queued' },
            { label: 'Running', value: 'Running' },
            { label: 'Completed', value: 'Completed' },
            { label: 'Failed', value: 'Failed' },
            { label: 'Cancelled', value: 'Cancelled' },
          ]"
          style="width: 160px"
        />
      </NSpace>
    </div>

    <div class="glass-panel" style="margin-top: 24px; padding: 0; overflow: hidden;">
      <NSpin :show="loading">
        <NDataTable
          :columns="columns"
          :data="filteredJobs"
          :bordered="false"
          :single-line="false"
          :row-key="(row) => row.id"
          :pagination="{ pageSize: 20 }"
        />
      </NSpin>
      <div v-if="!loading && filteredJobs.length === 0" style="padding: 60px 20px;">
        <NEmpty description="沒有符合條件的 Job" />
      </div>
    </div>

    <!-- Detail Modal -->
    <NModal
      v-model:show="detailModalOpen"
      preset="card"
      title="Job 詳情"
      style="width: 90vw; max-width: 900px; max-height: 90vh; overflow: auto"
      :mask-closable="false"
      @close="closeDetail"
    >
      <NSpin :show="detailLoading">
        <div v-if="selectedJob" style="line-height: 1.6">
          <div style="margin-bottom: 12px">
            <strong>ID:</strong> {{ selectedJob.id }}
          </div>
          <div style="margin-bottom: 12px">
            <strong>Type:</strong> {{ selectedJob.jobType }}
          </div>
          <div style="margin-bottom: 12px">
            <strong>Status:</strong>
            <NTag :type="statusType(selectedJob.status)" size="small" style="margin-left: 8px">{{ selectedJob.status }}</NTag>
          </div>
          <div style="margin-bottom: 12px">
            <strong>Progress:</strong> {{ selectedJob.progressPercent }}%
          </div>
          <div style="margin-bottom: 12px">
            <strong>Created:</strong> {{ formatDate(selectedJob.createdAtUtc) }}
          </div>
          <div style="margin-bottom: 12px">
            <strong>Started:</strong> {{ formatDate(selectedJob.startedAtUtc) }}
          </div>
          <div style="margin-bottom: 12px">
            <strong>Completed:</strong> {{ formatDate(selectedJob.completedAtUtc) }}
          </div>
          <div v-if="selectedJob.errorMessage" style="margin-bottom: 12px; color: #f87171">
            <strong>Error:</strong> {{ selectedJob.errorMessage }}
          </div>
          <div v-if="selectedJob.payloadJson" style="margin-bottom: 12px">
            <strong>Payload:</strong>
            <pre style="margin-top: 8px; padding: 12px; background: var(--bg-tertiary); border-radius: 8px; font-size: 12px; overflow: auto">{{ prettyJson(selectedJob.payloadJson) }}</pre>
          </div>
          <div v-if="selectedJob.resultJson">
            <strong>Result:</strong>
            <pre style="margin-top: 8px; padding: 12px; background: var(--bg-tertiary); border-radius: 8px; font-size: 12px; overflow: auto">{{ prettyJson(selectedJob.resultJson) }}</pre>
          </div>
        </div>
      </NSpin>
    </NModal>

    <!-- Create Modal -->
    <NModal
      v-model:show="createModalOpen"
      preset="card"
      title="建立資料匯入工作"
      style="width: 500px"
      :mask-closable="false"
    >
      <NForm label-placement="left" label-width="120">
        <NFormItem label="Job Type">
          <NSelect
            v-model:value="createForm.jobType"
            :options="createJobTypeOptions as SelectMixedOption[]"
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
          />
        </NFormItem>
      </NForm>

      <template #footer>
        <NSpace justify="end">
          <NButton @click="createModalOpen = false">取消</NButton>
          <NButton type="primary" @click="handleCreate">建立</NButton>
        </NSpace>
      </template>
    </NModal>
  </main>
</template>
