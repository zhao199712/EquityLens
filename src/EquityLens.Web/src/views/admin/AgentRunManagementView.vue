<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
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
  useMessage,
  type DataTableColumns,
} from 'naive-ui'
import {
  RefreshOutline,
  SearchOutline,
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
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">AGENT RUNS</p>
        <h1>Agent Run 管理</h1>
      </div>
      <NSpace>
        <NButton type="primary" @click="loadRuns">
          <template #icon>
            <NIcon><RefreshOutline /></NIcon>
          </template>
          重新整理
        </NButton>
      </NSpace>
    </section>

    <div class="glass-panel" style="padding: 16px 20px;">
      <NSpace>
        <NInput
          v-model:value="search"
          placeholder="搜尋 workflow / agent / ID..."
          clearable
          style="width: 300px"
        >
          <template #prefix>
            <NIcon><SearchOutline /></NIcon>
          </template>
        </NInput>
        <NSelect
          v-model:value="filterWorkflow"
          :options="[
            { label: '全部 Workflow', value: '' },
            { label: 'CriticReview', value: 'CriticReview' },
            { label: 'DraftRevision', value: 'DraftRevision' },
          ]"
          style="width: 180px"
        />
        <NSelect
          v-model:value="filterStatus"
          :options="[
            { label: '全部狀態', value: '' },
            { label: 'Pending', value: 'Pending' },
            { label: 'Running', value: 'Running' },
            { label: 'Succeeded', value: 'Succeeded' },
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
          :data="filteredRuns"
          :bordered="false"
          :single-line="false"
          :row-key="(row) => row.id"
          :pagination="{ pageSize: 20 }"
        />
      </NSpin>
      <div v-if="!loading && filteredRuns.length === 0" style="padding: 60px 20px;">
        <NEmpty description="沒有符合條件的 Agent Run" />
      </div>
    </div>

    <NModal
      v-model:show="detailModalOpen"
      preset="card"
      title="Agent Run 詳情"
      style="width: 90vw; max-width: 1200px; max-height: 90vh; overflow: auto"
      :mask-closable="false"
    >
      <AgentRunAdminDetail v-if="selectedRunId" :run-id="selectedRunId" />
    </NModal>
  </main>
</template>
