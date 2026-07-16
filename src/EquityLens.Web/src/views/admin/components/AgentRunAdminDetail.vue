<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import {
  NSpin,
  NEmpty,
  NTag,
  NButton,
  NSpace,
  NIcon,
  NTimeline,
  NTimelineItem,
  NCard,
  NCode,
  useMessage,
} from 'naive-ui'
import {
  CheckmarkCircleOutline,
  CloseCircleOutline,
  TimeOutline,
  AlertCircleOutline,
  ReloadOutline,
  CloseOutline,
} from '@vicons/ionicons5'
import { getAgentRun, retryAgentRun, cancelAgentRun, type AgentRunDetail, type AgentRunNodeDto } from '../../../services/agentRuns'

const props = defineProps<{ runId: string }>()
const message = useMessage()

const run = ref<AgentRunDetail | null>(null)
const loading = ref(true)
const error = ref('')
let timer: ReturnType<typeof setInterval> | null = null
const TERMINAL_STATUSES = new Set(['Succeeded', 'Failed', 'Cancelled'])

const isTerminal = computed(() => {
  if (!run.value) return false
  return TERMINAL_STATUSES.has(run.value.run.status)
})

onMounted(() => {
  loadRun()
  startPolling()
})

onUnmounted(() => {
  stopPolling()
})

async function loadRun() {
  loading.value = true
  error.value = ''
  try {
    run.value = await getAgentRun(props.runId)
  } catch {
    error.value = '無法載入 Agent Run 詳情。'
  } finally {
    loading.value = false
  }
}

function startPolling() {
  stopPolling()
  timer = setInterval(async () => {
    if (isTerminal.value) {
      stopPolling()
      return
    }
    try {
      run.value = await getAgentRun(props.runId)
    } catch {
      // ignore polling errors
    }
  }, 2000)
}

function stopPolling() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
}

async function handleRetry() {
  try {
    await retryAgentRun(props.runId)
    message.success('重試已送出。')
    startPolling()
  } catch {
    message.error('重試失敗。')
  }
}

async function handleCancel() {
  try {
    await cancelAgentRun(props.runId)
    message.success('已取消。')
    await loadRun()
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

function statusIcon(status: string) {
  switch (status) {
    case 'Succeeded': return CheckmarkCircleOutline
    case 'Failed': return CloseCircleOutline
    case 'Running': return TimeOutline
    case 'Pending': return TimeOutline
    default: return AlertCircleOutline
  }
}

function prettyJson(obj: unknown): string {
  if (obj === null || obj === undefined) return 'null'
  try {
    return JSON.stringify(obj, null, 2)
  } catch {
    return String(obj)
  }
}

const sortedEvents = computed(() => {
  if (!run.value) return []
  return [...run.value.events].sort((a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime())
})

const nodeMap = computed(() => {
  if (!run.value) return new Map<string, AgentRunNodeDto>()
  return new Map<string, AgentRunNodeDto>(run.value.nodes.map((node: AgentRunNodeDto) => [node.id, node]))
})
</script>

<template>
  <NSpin :show="loading">
    <div v-if="error" style="padding: 20px; color: #f87171">{{ error }}</div>
    <div v-else-if="run">
      <NCard title="Run Info" style="margin-bottom: 16px">
        <NSpace vertical size="small">
          <div>
            <strong>Workflow:</strong> {{ run.run.workflowType }}
          </div>
          <div>
            <strong>Agent Type:</strong> {{ run.run.agentType }}
          </div>
          <div>
            <strong>Status:</strong>
            <NTag :type="statusType(run.run.status)" size="small" style="margin-left: 8px">
              {{ run.run.status }}
            </NTag>
          </div>
          <div>
            <strong>ID:</strong> {{ run.run.id }}
          </div>
          <div>
            <strong>Created:</strong> {{ formatDate(run.run.createdAtUtc) }}
          </div>
          <div>
            <strong>Started:</strong> {{ formatDate(run.run.startedAtUtc) }}
          </div>
          <div>
            <strong>Completed:</strong> {{ formatDate(run.run.completedAtUtc) }}
          </div>
          <div v-if="run.run.errorMessage" style="color: #f87171">
            <strong>Error:</strong> {{ run.run.errorMessage }}
          </div>
          <NSpace v-if="run.run.status === 'Failed' || run.run.status === 'Pending' || run.run.status === 'Running'" style="margin-top: 12px">
            <NButton v-if="run.run.status === 'Failed'" type="warning" @click="handleRetry">
              <template #icon>
                <NIcon><ReloadOutline /></NIcon>
              </template>
              重試
            </NButton>
            <NButton v-if="run.run.status === 'Pending' || run.run.status === 'Running'" type="error" @click="handleCancel">
              <template #icon>
                <NIcon><CloseOutline /></NIcon>
              </template>
              取消
            </NButton>
          </NSpace>
        </NSpace>
      </NCard>

      <NCard title="Nodes" style="margin-bottom: 16px">
        <div v-if="run.nodes.length === 0" style="color: var(--text-tertiary)">暫無節點。</div>
        <NTimeline v-else
          >
          <NTimelineItem
            v-for="node in run.nodes"
            :key="node.id"
            :type="statusType(node.status)"
            :icon="statusIcon(node.status)"
            :title="node.nodeKey"
            :content="node.nodeType"
            :time="formatDate(node.completedAtUtc)"
          >
            <div v-if="node.errorMessage" style="color: #f87171; font-size: 12px">{{ node.errorMessage }}</div>
            <div v-if="node.durationMs !== null" style="color: var(--text-tertiary); font-size: 12px">耗時: {{ node.durationMs }}ms</div>
          </NTimelineItem>
        </NTimeline>
      </NCard>

      <NCard title="Events" style="margin-bottom: 16px">
        <div v-if="sortedEvents.length === 0" style="color: var(--text-tertiary)">暫無事件。</div>
        <NTimeline v-else
          >
          <NTimelineItem
            v-for="evt in sortedEvents"
            :key="evt.id"
            :type="evt.eventType.includes('Failed') ? 'error' : evt.eventType.includes('Succeed') ? 'success' : 'info'"
            :title="evt.eventType"
            :time="formatDate(evt.createdAtUtc)"
          >
            <div v-if="evt.message" style="font-size: 13px; color: var(--text-secondary)">{{ evt.message }}</div>
            <div v-if="evt.agentRunNodeId" style="font-size: 12px; color: var(--text-tertiary)">
              node: {{ nodeMap.get(evt.agentRunNodeId)?.nodeKey ?? evt.agentRunNodeId.slice(0, 8) }}
            </div>
          </NTimelineItem>
        </NTimeline>
      </NCard>

      <NCard title="Tool Calls" style="margin-bottom: 16px">
        <div v-if="run.toolCalls.length === 0" style="color: var(--text-tertiary)">暫無工具呼叫。</div>
        <div v-for="tc in run.toolCalls" :key="tc.id" style="margin-bottom: 12px; padding: 12px; background: var(--bg-tertiary); border-radius: 8px">
          <div style="display: flex; align-items: center; justify-content: space-between">
            <strong>{{ tc.toolName }}</strong>
            <NTag :type="statusType(tc.status)" size="small">{{ tc.status }}</NTag>
          </div>
          <div v-if="tc.resultPreview" style="font-size: 12px; color: var(--text-tertiary); margin-top: 4px">{{ tc.resultPreview }}</div>
          <div v-if="tc.errorMessage" style="font-size: 12px; color: #f87171; margin-top: 4px">{{ tc.errorMessage }}</div>
          <NCode :code="prettyJson(tc.argumentsJson)" language="json" style="margin-top: 8px" />
        </div>
      </NCard>

      <NCard title="Output" style="margin-bottom: 16px">
        <NCode v-if="run.outputJson" :code="prettyJson(run.outputJson)" language="json" />
        <div v-else style="color: var(--text-tertiary)">尚無輸出。</div>
      </NCard>
    </div>
    <NEmpty v-else description="無資料" />
  </NSpin>
</template>
