<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NSpin,
  NTag,
  NSpace,
  NIcon,
  NTimeline,
  NTimelineItem,
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
import { getAgentRun, retryAgentRun, cancelAgentRun, createEvidenceRemediation, createEvidenceReanalysis, decideAgentApproval, type AgentRunDetail, type AgentRunNodeDto } from '../../../services/agentRuns'

const props = defineProps<{ runId: string }>()
const message = useMessage()
const router = useRouter()

const run = ref<AgentRunDetail | null>(null)
const loading = ref(true)
const error = ref('')
const creatingRemediation = ref(false)
const creatingReanalysis = ref(false)
const approvalComment = ref('')
let timer: ReturnType<typeof setInterval> | null = null
const TERMINAL_STATUSES = new Set(['Succeeded', 'Failed', 'Cancelled'])

const isTerminal = computed(() => {
  if (!run.value) return false
  return TERMINAL_STATUSES.has(run.value.run.status)
})

const canRemediateEvidence = computed(() => run.value?.run.workflowType === 'CriticReview'
  && run.value.run.status === 'Succeeded'
  && run.value.outputJson?.requiresMoreEvidence === true)

const canReanalyzeEvidence = computed(() => run.value?.run.workflowType === 'EvidenceRemediation'
  && run.value.run.status === 'Succeeded'
  && run.value.outputJson?.requiresReanalysis === true)
const pendingApproval = computed(() => run.value?.approvals.find(item => item.status === 'Pending') ?? null)

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

async function handleApproval(decision: 'approve' | 'reject') {
  if (!pendingApproval.value) return
  if (decision === 'reject' && !approvalComment.value.trim()) {
    message.error('拒絕時必須填寫原因。')
    return
  }
  try {
    await decideAgentApproval(props.runId, pendingApproval.value.id, decision, approvalComment.value)
    approvalComment.value = ''
    message.success(decision === 'approve' ? '已批准並排程恢復。' : '已拒絕並取消 Run。')
  } catch {
    message.error('Approval 狀態可能已變更，已重新載入。')
  }
  await loadRun()
}

async function handleEvidenceRemediation() {
  if (!run.value || creatingRemediation.value) return
  creatingRemediation.value = true
  try {
    const created = await createEvidenceRemediation(run.value.run.id)
    message.success('證據補強流程已建立。')
    await router.push({ name: 'admin-agent-run-detail', params: { id: created.id } })
  } catch {
    message.error('無法建立證據補強流程，請確認 Critic Review 已完成且需要更多證據。')
  } finally {
    creatingRemediation.value = false
  }
}

async function handleEvidenceReanalysis() {
  if (!run.value || creatingReanalysis.value) return
  creatingReanalysis.value = true
  try {
    const created = await createEvidenceReanalysis(run.value.run.id)
    message.success('證據驅動重新分析流程已建立。')
    await router.push({ name: 'admin-agent-run-detail', params: { id: created.id } })
  } catch {
    message.error('無法建立重新分析流程，請確認補證據流程已完成且建議重新分析。')
  } finally {
    creatingReanalysis.value = false
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
    case 'WaitingForApproval': return 'warning'
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
  <NSpin :show="loading" class="agent-run-detail">
    <div v-if="error" class="prestige-error">{{ error }}</div>
    <div v-else-if="run">
      <section class="prestige-panel prestige-panel-pad detail-section">
        <span class="prestige-label detail-label">Run Info</span>
        <div class="detail-field">
          <span class="detail-field-label">Workflow</span>
          <span class="detail-field-value">{{ run.run.workflowType }}</span>
        </div>
        <div class="detail-field">
          <span class="detail-field-label">Agent Type</span>
          <span class="detail-field-value">{{ run.run.agentType }}</span>
        </div>
        <div class="detail-field">
          <span class="detail-field-label">Status</span>
          <div>
            <NTag :type="statusType(run.run.status)" size="small">{{ run.run.status }}</NTag>
          </div>
        </div>
        <div class="detail-field">
          <span class="detail-field-label">ID</span>
          <span class="detail-field-value prestige-mono">{{ run.run.id }}</span>
        </div>
        <div class="detail-field">
          <span class="detail-field-label">Created</span>
          <span class="detail-field-value prestige-mono">{{ formatDate(run.run.createdAtUtc) }}</span>
        </div>
        <div class="detail-field">
          <span class="detail-field-label">Started</span>
          <span class="detail-field-value prestige-mono">{{ formatDate(run.run.startedAtUtc) }}</span>
        </div>
        <div class="detail-field">
          <span class="detail-field-label">Completed</span>
          <span class="detail-field-value prestige-mono">{{ formatDate(run.run.completedAtUtc) }}</span>
        </div>
        <div v-if="run.run.errorMessage" class="detail-field">
          <span class="detail-field-label">Error</span>
          <span class="detail-field-value detail-down">{{ run.run.errorMessage }}</span>
        </div>
        <NSpace v-if="run.run.status === 'Failed' || run.run.status === 'Pending' || run.run.status === 'Running' || run.run.status === 'WaitingForApproval'" class="detail-actions">
          <button v-if="run.run.status === 'Failed'" type="button" class="prestige-btn" @click="handleRetry">
            <NIcon size="16"><ReloadOutline /></NIcon>
            重試
          </button>
          <button v-if="run.run.status === 'Pending' || run.run.status === 'Running' || run.run.status === 'WaitingForApproval'" type="button" class="prestige-btn detail-btn-danger" @click="handleCancel">
            <NIcon size="16"><CloseOutline /></NIcon>
            取消
          </button>
        </NSpace>
        <button v-if="canRemediateEvidence" type="button" class="prestige-btn detail-remediation-btn" :disabled="creatingRemediation" @click="handleEvidenceRemediation">
          {{ creatingRemediation ? '建立中…' : '補充證據並修訂' }}
        </button>
        <button v-if="canReanalyzeEvidence" type="button" class="prestige-btn detail-remediation-btn" :disabled="creatingReanalysis" @click="handleEvidenceReanalysis">
          {{ creatingReanalysis ? '建立中…' : '重新分析' }}
        </button>
      </section>

      <section v-if="pendingApproval" class="prestige-panel prestige-panel-pad detail-section approval-admin" data-testid="admin-approval-panel">
        <span class="prestige-label detail-label">Human Approval Required</span>
        <div class="detail-field"><span class="detail-field-label">Node</span><span class="detail-field-value">{{ pendingApproval.nodeKey }} · {{ pendingApproval.nodeType }}</span></div>
        <div class="detail-field"><span class="detail-field-label">Side Effect</span><span class="detail-field-value">{{ pendingApproval.sideEffectLevel }}</span></div>
        <div class="detail-field"><span class="detail-field-label">Reason</span><span class="detail-field-value">{{ pendingApproval.reason }}</span></div>
        <textarea v-model="approvalComment" class="prestige-input approval-admin-comment" rows="3" placeholder="批准可選填；拒絕必填" />
        <NSpace>
          <button type="button" class="prestige-btn prestige-btn-solid" @click="handleApproval('approve')">批准並繼續</button>
          <button type="button" class="prestige-btn detail-btn-danger" :disabled="!approvalComment.trim()" @click="handleApproval('reject')">拒絕並取消</button>
        </NSpace>
      </section>

      <section class="prestige-panel prestige-panel-pad detail-section">
        <span class="prestige-label detail-label">Nodes</span>
        <div v-if="run.nodes.length === 0" class="detail-muted">暫無節點。</div>
        <NTimeline v-else>
          <NTimelineItem
            v-for="node in run.nodes"
            :key="node.id"
            :type="statusType(node.status)"
            :icon="statusIcon(node.status)"
            :title="node.nodeKey"
            :content="`${node.nodeType}${node.iteration > 0 ? ` · 第 ${node.iteration} 輪` : ''}`"
            :time="formatDate(node.completedAtUtc)"
            :class="`tl-${statusType(node.status)}`"
          >
            <div v-if="node.errorMessage" class="detail-node-error">{{ node.errorMessage }}</div>
            <div v-if="node.durationMs !== null" class="detail-node-meta">耗時: {{ node.durationMs }}ms</div>
          </NTimelineItem>
        </NTimeline>
      </section>

      <section class="prestige-panel prestige-panel-pad detail-section">
        <span class="prestige-label detail-label">Events</span>
        <div v-if="sortedEvents.length === 0" class="detail-muted">暫無事件。</div>
        <NTimeline v-else>
          <NTimelineItem
            v-for="evt in sortedEvents"
            :key="evt.id"
            :type="evt.eventType.includes('Failed') ? 'error' : evt.eventType.includes('Succeed') ? 'success' : 'info'"
            :title="evt.eventType"
            :time="formatDate(evt.createdAtUtc)"
            :class="evt.eventType.includes('Failed') ? 'tl-error' : evt.eventType.includes('Succeed') ? 'tl-success' : 'tl-info'"
          >
            <div v-if="evt.message" class="detail-event-message">{{ evt.message }}</div>
            <div v-if="evt.agentRunNodeId" class="detail-node-meta">
              node: {{ nodeMap.get(evt.agentRunNodeId)?.nodeKey ?? evt.agentRunNodeId.slice(0, 8) }}
            </div>
          </NTimelineItem>
        </NTimeline>
      </section>

      <section class="prestige-panel prestige-panel-pad detail-section">
        <span class="prestige-label detail-label">Tool Calls</span>
        <div v-if="run.toolCalls.length === 0" class="detail-muted">暫無工具呼叫。</div>
        <div v-for="tc in run.toolCalls" :key="tc.id" class="tool-call-item">
          <div class="tool-call-head">
            <span class="tool-call-name">{{ tc.toolName }}</span>
            <NTag :type="statusType(tc.status)" size="small">{{ tc.status }}</NTag>
          </div>
          <div v-if="tc.resultPreview" class="detail-node-meta tool-call-preview">{{ tc.resultPreview }}</div>
          <div v-if="tc.errorMessage" class="detail-node-error tool-call-preview">{{ tc.errorMessage }}</div>
          <pre class="detail-json tool-call-json">{{ prettyJson(tc.argumentsJson) }}</pre>
        </div>
      </section>

      <section class="prestige-panel prestige-panel-pad detail-section">
        <span class="prestige-label detail-label">Output</span>
        <pre v-if="run.outputJson" class="detail-json">{{ prettyJson(run.outputJson) }}</pre>
        <div v-else class="detail-muted">尚無輸出。</div>
      </section>
    </div>
    <div v-else class="prestige-empty">無資料</div>
  </NSpin>
</template>

<style scoped>
.approval-admin { border-color: rgba(212, 162, 78, .55); }
.approval-admin-comment { width: 100%; margin: 12px 0; resize: vertical; }
.agent-run-detail {
  --gold: #c9a86a;
  --gold-strong: #ddc18a;
  --gold-border: rgba(201, 168, 106, 0.25);
  --gold-border-soft: rgba(201, 168, 106, 0.14);
  --ivory: #f5efe0;
  --muted: #9a917c;
  --up: #7fa387;
  --down: #b05c5c;
  --panel-bg: rgba(201, 168, 106, 0.04);
  --serif: Georgia, 'Noto Serif TC', serif;
  --sans: 'Inter', 'Noto Sans TC', sans-serif;
  color: var(--ivory);
}

.agent-run-detail :deep(.n-spin-body) {
  color: var(--gold);
}

/* ---- Sections ---- */
.detail-section {
  margin-bottom: 16px;
}

.detail-label {
  display: block;
  margin-bottom: 16px;
}

/* ---- Run info fields ---- */
.detail-field {
  margin-bottom: 12px;
}

.detail-field-label {
  display: block;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--gold);
  margin-bottom: 4px;
}

.detail-field-value {
  color: var(--ivory);
  font-size: 14px;
  line-height: 1.6;
}

.detail-down {
  color: var(--down);
}

.detail-actions {
  margin-top: 12px;
}

.detail-remediation-btn { margin-top: 12px; }

.detail-btn-danger {
  color: var(--down);
  border-color: rgba(176, 92, 92, 0.45);
}

.detail-btn-danger:hover {
  background: rgba(176, 92, 92, 0.1);
  border-color: var(--down);
}

/* ---- Text helpers ---- */
.detail-muted {
  color: var(--muted);
  font-size: 13px;
}

.detail-node-error {
  color: var(--down);
  font-size: 12px;
}

.detail-node-meta {
  color: var(--muted);
  font-size: 12px;
}

.detail-event-message {
  color: var(--ivory);
  font-size: 13px;
}

/* ---- Timeline ---- */
.agent-run-detail :deep(.n-timeline-item-content__title) {
  color: var(--ivory);
}

.agent-run-detail :deep(.n-timeline-item-content__content) {
  color: var(--muted);
}

.agent-run-detail :deep(.n-timeline-item-content__meta) {
  color: var(--muted);
}

.agent-run-detail :deep(.n-timeline-item-timeline__line) {
  background-color: var(--gold-border-soft);
}

.tl-success :deep(.n-timeline-item__icon) {
  color: var(--up);
}

.tl-error :deep(.n-timeline-item__icon) {
  color: var(--down);
}

.tl-warning :deep(.n-timeline-item__icon),
.tl-info :deep(.n-timeline-item__icon) {
  color: #d4a24e;
}

.tl-default :deep(.n-timeline-item__icon) {
  color: var(--muted);
}

/* ---- Tags ---- */
.agent-run-detail :deep(.n-tag) {
  background: transparent;
  color: var(--muted);
}

.agent-run-detail :deep(.n-tag .n-tag__border) {
  border-color: var(--gold-border-soft);
}

.agent-run-detail :deep(.n-tag--success-type) {
  color: var(--up);
}

.agent-run-detail :deep(.n-tag--success-type .n-tag__border) {
  border-color: rgba(127, 163, 135, 0.45);
}

.agent-run-detail :deep(.n-tag--error-type) {
  color: var(--down);
}

.agent-run-detail :deep(.n-tag--error-type .n-tag__border) {
  border-color: rgba(176, 92, 92, 0.45);
}

.agent-run-detail :deep(.n-tag--warning-type),
.agent-run-detail :deep(.n-tag--info-type) {
  color: #d4a24e;
}

.agent-run-detail :deep(.n-tag--warning-type .n-tag__border),
.agent-run-detail :deep(.n-tag--info-type .n-tag__border) {
  border-color: rgba(212, 162, 78, 0.45);
}

/* ---- Tool calls ---- */
.tool-call-item {
  margin-bottom: 12px;
  padding: 12px 14px;
  background: rgba(11, 18, 32, 0.6);
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
}

.tool-call-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.tool-call-name {
  color: var(--ivory);
  font-size: 14px;
  font-weight: 600;
}

.tool-call-preview {
  margin-top: 4px;
}

.tool-call-json {
  margin-top: 8px;
}

/* ---- JSON blocks ---- */
.detail-json {
  margin: 0;
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
</style>
