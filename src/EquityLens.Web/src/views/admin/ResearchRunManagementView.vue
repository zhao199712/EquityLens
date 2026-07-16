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
  NModal,
  useMessage,
  type DataTableColumns,
} from 'naive-ui'
import {
  RefreshOutline,
  SearchOutline,
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
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">RESEARCH RUNS</p>
        <h1>Research Run 管理</h1>
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
          placeholder="搜尋 ticker / question / ID..."
          clearable
          style="width: 320px"
        >
          <template #prefix>
            <NIcon><SearchOutline /></NIcon>
          </template>
        </NInput>
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
        <NEmpty description="沒有符合條件的 Research Run" />
      </div>
    </div>

    <NModal
      v-model:show="detailModalOpen"
      preset="card"
      title="Research Run 詳情"
      style="width: 90vw; max-width: 900px; max-height: 90vh; overflow: auto"
      :mask-closable="false"
      @close="closeDetail"
    >
      <NSpin :show="detailLoading">
        <div v-if="selectedRunDetail" style="line-height: 1.6">
          <div style="margin-bottom: 16px">
            <strong>Ticker:</strong> {{ selectedRunDetail.run.ticker }}
          </div>
          <div style="margin-bottom: 16px">
            <strong>Question:</strong> {{ selectedRunDetail.run.question }}
          </div>
          <div style="margin-bottom: 16px">
            <strong>Status:</strong>
            <NTag :type="statusType(selectedRunDetail.run.status)" size="small" style="margin-left: 8px">
              {{ selectedRunDetail.run.status }}
            </NTag>
          </div>
          <div style="margin-bottom: 16px">
            <strong>Citations:</strong> {{ selectedRunDetail.run.citationCount }}
          </div>
          <div style="margin-bottom: 16px">
            <strong>Latency:</strong> {{ selectedRunDetail.run.latencyMs }}ms
          </div>
          <div style="margin-bottom: 16px">
            <strong>Answer:</strong>
            <div style="white-space: pre-wrap; margin-top: 8px; padding: 12px; background: var(--bg-tertiary); border-radius: 8px">
              {{ selectedRunDetail.answer }}
            </div>
          </div>
        </div>
      </NSpin>
    </NModal>
  </main>
</template>
