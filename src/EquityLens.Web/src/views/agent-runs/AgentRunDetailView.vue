<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import {
  cancelAgentRun,
  createDraftRevision,
  getAgentRun,
  retryAgentRun,
  type AgentRunDetail,
  type AgentRunNodeDto,
} from '../../services/agentRuns'

const route = useRoute()
const router = useRouter()
const run = ref<AgentRunDetail | null>(null)
const loading = ref(true)
const actionLoading = ref(false)
const error = ref('')
const activeTab = ref<'timeline' | 'nodes' | 'toolCalls' | 'feedback' | 'blackboard' | 'workflow'>('timeline')

onMounted(loadRun)

async function loadRun() {
  const id = route.params.id as string
  loading.value = true
  error.value = ''
  try {
    run.value = await getAgentRun(id)
  } catch {
    error.value = '無法載入 Agent Run 詳情。'
  } finally {
    loading.value = false
  }
}

async function handleRetry() {
  if (!run.value) return
  actionLoading.value = true
  error.value = ''
  try {
    await retryAgentRun(run.value.run.id)
    await loadRun()
  } catch {
    error.value = '重試失敗。'
  } finally {
    actionLoading.value = false
  }
}

async function handleCancel() {
  if (!run.value) return
  actionLoading.value = true
  error.value = ''
  try {
    await cancelAgentRun(run.value.run.id)
    await loadRun()
  } catch {
    error.value = '取消失敗。'
  } finally {
    actionLoading.value = false
  }
}

async function handleCreateDraftRevision() {
  if (!run.value) return
  actionLoading.value = true
  error.value = ''
  try {
    const created = await createDraftRevision(run.value.run.id)
    router.push({ name: 'agent-run-detail', params: { id: created.id } })
  } catch {
    error.value = '建立 DraftRevision 失敗。'
  } finally {
    actionLoading.value = false
  }
}

function formatDate(iso: string | null) {
  if (!iso) return '-'
  return new Date(iso).toLocaleString('zh-TW')
}

function statusColor(status: string) {
  switch (status) {
    case 'Succeeded': return '#34d399'
    case 'Failed': return '#f87171'
    case 'Running': return '#60a5fa'
    case 'Cancelled': return '#999999'
    case 'Pending': return '#fbbf24'
    case 'WaitingForFeedback': return '#c084fc'
    case 'Skipped': return '#666666'
    default: return '#666666'
  }
}

function eventTypeIcon(type: string) {
  if (type.includes('Failed') || type.includes('Error')) return '!'
  if (type.includes('Succeed') || type.includes('Completed')) return '✓'
  if (type.includes('Started') || type.includes('Created')) return '▶'
  if (type.includes('Skipped')) return '○'
  return '·'
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
  return new Map(run.value.nodes.map((node) => [node.id, node]))
})

const duration = computed(() => {
  if (!run.value?.run.startedAtUtc || !run.value?.run.completedAtUtc) return null
  const ms = new Date(run.value.run.completedAtUtc).getTime() - new Date(run.value.run.startedAtUtc).getTime()
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
})

const canCreateDraftRevision = computed(() =>
  run.value?.run.workflowType === 'CriticReview' && run.value.run.status === 'Succeeded',
)
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px; padding-bottom: 80px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn-dark" style="margin-bottom: 20px" @click="router.push({ name: 'agent-runs' })">← 返回列表</button>

      <div v-if="loading" style="color: #666666; font-size: 14px">載入中...</div>
      <div v-else-if="error" style="color: #f87171; font-size: 14px">{{ error }}</div>

      <template v-else-if="run">
        <ScrollReveal>
          <div style="margin-bottom: 32px">
            <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 8px; flex-wrap: wrap">
              <span class="kimi-tag-dark" style="font-family: monospace">{{ run.run.workflowType }}</span>
              <span class="kimi-tag-dark">{{ run.run.agentType }}</span>
              <span class="kimi-tag-dark" :style="{ borderColor: statusColor(run.run.status), color: statusColor(run.run.status) }">{{ run.run.status }}</span>
              <span v-if="duration" style="font-size: 12px; color: #666666">{{ duration }}</span>
            </div>
            <div style="font-family: monospace; font-size: 12px; color: #999999">{{ run.run.id }}</div>

            <div style="display: flex; gap: 8px; margin-top: 12px">
              <button v-if="run.run.status === 'Failed'" class="kimi-btn-dark" style="border-color: #34d399; color: #34d399" :disabled="actionLoading" @click="handleRetry">重試</button>
              <button v-if="run.run.status === 'Running' || run.run.status === 'Pending'" class="kimi-btn-dark" style="border-color: #f87171; color: #f87171" :disabled="actionLoading" @click="handleCancel">取消</button>
              <button v-if="canCreateDraftRevision" class="kimi-btn-dark" style="border-color: #60a5fa; color: #60a5fa" :disabled="actionLoading" @click="handleCreateDraftRevision">產生修訂稿</button>
              <button class="kimi-btn-dark" :disabled="actionLoading" @click="loadRun">重新整理</button>
            </div>

            <div style="display: flex; gap: 24px; margin-top: 16px; font-size: 12px; color: #666666; flex-wrap: wrap">
              <div>建立：{{ formatDate(run.run.createdAtUtc) }}</div>
              <div>開始：{{ formatDate(run.run.startedAtUtc) }}</div>
              <div>完成：{{ formatDate(run.run.completedAtUtc) }}</div>
            </div>

            <div v-if="run.run.errorMessage" style="color: #f87171; font-size: 13px; margin-top: 12px; padding: 12px; background: rgba(248,113,113,0.1); border-radius: 8px">{{ run.run.errorMessage }}</div>
          </div>
        </ScrollReveal>

        <div style="display: flex; gap: 0; margin-bottom: 24px; border-bottom: 1px solid #333333; overflow-x: auto">
          <button
            v-for="tab in (['timeline', 'nodes', 'toolCalls', 'feedback', 'blackboard', 'workflow'] as const)"
            :key="tab"
            :style="{ padding: '10px 20px', background: 'transparent', border: 'none', borderBottom: activeTab === tab ? '2px solid #FFFFFF' : '2px solid transparent', color: activeTab === tab ? '#FFFFFF' : '#666666', cursor: 'pointer', fontSize: '14px', fontWeight: activeTab === tab ? '600' : '400' }"
            @click="activeTab = tab"
          >
            {{ tab === 'timeline' ? '執行時間線' : tab === 'nodes' ? '節點狀態' : tab === 'toolCalls' ? '工具呼叫' : tab === 'feedback' ? 'Feedback' : tab === 'blackboard' ? 'Blackboard' : 'Workflow' }}
          </button>
        </div>

        <div v-if="activeTab === 'timeline'" style="display: flex; flex-direction: column; gap: 0">
          <div v-for="evt in sortedEvents" :key="evt.id" style="display: flex; gap: 12px; padding: 8px 0; border-left: 2px solid #333333; margin-left: 8px; padding-left: 16px; position: relative">
            <div :style="{ position: 'absolute', left: '-9px', top: '12px', width: '16px', height: '16px', borderRadius: '50%', background: evt.eventType.includes('Failed') ? '#f87171' : evt.eventType.includes('Succeed') ? '#34d399' : evt.eventType.includes('Started') ? '#60a5fa' : '#333333', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '10px', color: '#FFFFFF', fontWeight: '700' }">{{ eventTypeIcon(evt.eventType) }}</div>
            <div style="flex: 1; min-width: 0">
              <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 2px; flex-wrap: wrap">
                <span style="font-family: monospace; font-size: 12px; color: #FFFFFF; font-weight: 600">{{ evt.eventType }}</span>
                <span v-if="evt.agentRunNodeId" style="font-family: monospace; font-size: 11px; color: #666666">node: {{ nodeMap.get(evt.agentRunNodeId)?.nodeKey ?? evt.agentRunNodeId.slice(0, 8) }}</span>
              </div>
              <div v-if="evt.message" style="font-size: 13px; color: #999999">{{ evt.message }}</div>
              <pre v-if="evt.payloadJson" style="margin-top: 4px; font-family: monospace; font-size: 11px; color: #666666; white-space: pre-wrap; word-break: break-all">{{ prettyJson(evt.payloadJson) }}</pre>
            </div>
            <div style="font-size: 11px; color: #666666; white-space: nowrap">{{ formatDate(evt.createdAtUtc) }}</div>
          </div>
          <div v-if="sortedEvents.length === 0" style="color: #666666; padding: 20px">暫無事件記錄。</div>
        </div>

        <div v-if="activeTab === 'nodes'" style="display: flex; flex-direction: column; gap: 12px">
          <div v-for="node in run.nodes" :key="node.id" class="kimi-panel-dark" style="padding: 16px">
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; gap: 12px">
              <div style="display: flex; align-items: center; gap: 8px; flex-wrap: wrap">
                <span style="font-family: monospace; font-size: 14px; font-weight: 600; color: #FFFFFF">{{ node.nodeKey }}</span>
                <span class="kimi-tag-dark" style="font-size: 11px">{{ node.nodeType }}</span>
              </div>
              <span class="kimi-tag-dark" :style="{ borderColor: statusColor(node.status), color: statusColor(node.status) }">{{ node.status }}</span>
            </div>
            <div v-if="node.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 8px">{{ node.errorMessage }}</div>
            <div style="display: flex; gap: 16px; font-size: 12px; color: #666666; flex-wrap: wrap">
              <span>開始：{{ formatDate(node.startedAtUtc) }}</span>
              <span>完成：{{ formatDate(node.completedAtUtc) }}</span>
              <span v-if="node.durationMs !== null">耗時：{{ node.durationMs }}ms</span>
            </div>
            <pre v-if="node.outputJson" style="margin-top: 12px; padding: 12px; background: #0b0b0b; border-radius: 8px; color: #999999; overflow-x: auto">{{ prettyJson(node.outputJson) }}</pre>
          </div>
        </div>

        <div v-if="activeTab === 'toolCalls'" style="display: flex; flex-direction: column; gap: 12px">
          <div v-for="tc in run.toolCalls" :key="tc.id" class="kimi-panel-dark" style="padding: 16px">
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; gap: 12px">
              <span style="font-family: monospace; font-size: 14px; color: #FFFFFF">{{ tc.toolName }}</span>
              <span class="kimi-tag-dark" :style="{ borderColor: statusColor(tc.status), color: statusColor(tc.status) }">{{ tc.status }}</span>
            </div>
            <div v-if="tc.resultPreview" style="font-size: 13px; color: #999999; margin-bottom: 8px">{{ tc.resultPreview }}</div>
            <div v-if="tc.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 8px">{{ tc.errorMessage }}</div>
            <pre style="padding: 12px; background: #0b0b0b; border-radius: 8px; color: #999999; overflow-x: auto">{{ prettyJson(tc.argumentsJson) }}</pre>
          </div>
          <div v-if="run.toolCalls.length === 0" style="color: #666666; padding: 20px">暫無工具呼叫。</div>
        </div>

        <div v-if="activeTab === 'feedback'" style="display: flex; flex-direction: column; gap: 12px">
          <div v-for="item in run.feedback" :key="item.id" class="kimi-panel-dark" style="padding: 16px">
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; gap: 12px">
              <div style="display: flex; align-items: center; gap: 8px; flex-wrap: wrap">
                <span style="font-family: monospace; font-size: 14px; color: #FFFFFF">{{ item.feedbackType }}</span>
                <span v-if="item.agentRunNodeId" style="font-family: monospace; font-size: 11px; color: #666666">node: {{ nodeMap.get(item.agentRunNodeId)?.nodeKey ?? item.agentRunNodeId.slice(0, 8) }}</span>
              </div>
              <span class="kimi-tag-dark" :style="{ borderColor: statusColor(item.status), color: statusColor(item.status) }">{{ item.status }}</span>
            </div>
            <div style="font-size: 13px; color: #999999; margin-bottom: 8px">{{ item.prompt }}</div>
            <div style="display: flex; gap: 16px; font-size: 12px; color: #666666; flex-wrap: wrap">
              <span>建立：{{ formatDate(item.createdAtUtc) }}</span>
              <span>回覆：{{ formatDate(item.respondedAtUtc) }}</span>
            </div>
            <pre v-if="item.responseJson" style="margin-top: 12px; padding: 12px; background: #0b0b0b; border-radius: 8px; color: #999999; overflow-x: auto">{{ prettyJson(item.responseJson) }}</pre>
          </div>
          <div v-if="run.feedback.length === 0" style="color: #666666; padding: 20px">暫無 feedback。</div>
        </div>

        <div v-if="activeTab === 'blackboard'">
          <pre class="kimi-panel-dark" style="padding: 16px; color: #999999; overflow-x: auto; white-space: pre-wrap">{{ prettyJson(run.blackboardJson) }}</pre>
          <h3 style="color: #FFFFFF; margin-top: 24px">Output</h3>
          <pre class="kimi-panel-dark" style="padding: 16px; color: #999999; overflow-x: auto; white-space: pre-wrap">{{ prettyJson(run.outputJson) }}</pre>
        </div>

        <div v-if="activeTab === 'workflow'">
          <pre class="kimi-panel-dark" style="padding: 16px; color: #999999; overflow-x: auto; white-space: pre-wrap">{{ prettyJson(run.workflowDefinitionJson) }}</pre>
        </div>
      </template>
    </div>
  </div>
</template>
