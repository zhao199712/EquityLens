<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import {
  cancelAgentRun,
  createDraftRevision,
  retryAgentRun,
  type AgentRunNodeDto,
} from '../../services/agentRuns'
import { useAgentRunPolling } from '../../composables/useAgentRunPolling'

interface CriticFinding {
  severity: string
  category: string
  message: string
  relatedCitationIndexes: number[]
  recommendation: string
}

interface CriticReviewOutput {
  summary?: string
  overallSeverity?: string
  findings?: CriticFinding[]
  requiresRevision?: boolean
  requiresMoreEvidence?: boolean
  routeBackTo?: string | null
  recommendedNextAction?: string
  suggestedAnswerRevision?: string | null
}

interface DraftRevisionOutput {
  sourceAnswer?: string | null
  revisedAnswer?: string
  revisionSummary?: string
  revisionRequired?: boolean
  appliedRecommendation?: string | null
}

const route = useRoute()
const router = useRouter()
const actionLoading = ref(false)
const error = ref('')
const activeTab = ref<'timeline' | 'nodes' | 'toolCalls' | 'feedback' | 'blackboard' | 'workflow'>('timeline')
const debugExpanded = ref(false)

const runId = computed(() => route.params.id as string)
const { agentRun: run, isLoading, isPolling, isTerminalStatus, error: pollError, startPolling, refresh } = useAgentRunPolling(runId.value)

onMounted(startPolling)

async function handleRetry() {
  if (!run.value) return
  actionLoading.value = true
  error.value = ''
  try {
    await retryAgentRun(run.value.run.id)
    refresh()
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
    refresh()
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

const canCreateDraftRevision = computed(() => {
  if (!run.value || run.value.run.workflowType !== 'CriticReview' || run.value.run.status !== 'Succeeded') return false
  const output = run.value.outputJson as CriticReviewOutput | null
  if (!output) return true
  return output.recommendedNextAction === 'ReviseAnswer' || output.recommendedNextAction === 'CollectMoreEvidenceThenReviseAnswer'
})

const criticOutput = computed((): CriticReviewOutput | null => {
  if (!run.value || run.value.run.workflowType !== 'CriticReview') return null
  return (run.value.outputJson as CriticReviewOutput) ?? null
})

const draftOutput = computed((): DraftRevisionOutput | null => {
  if (!run.value || run.value.run.workflowType !== 'DraftRevision') return null
  return (run.value.outputJson as DraftRevisionOutput) ?? null
})

const nextActionLabel = computed(() => {
  const action = criticOutput.value?.recommendedNextAction
  if (!action) return null
  switch (action) {
    case 'AcceptAnswer': return { text: '答案可接受', color: '#34d399', bg: 'rgba(52,211,153,0.1)' }
    case 'ReviseAnswer': return { text: '建議修正答案', color: '#fbbf24', bg: 'rgba(251,191,36,0.1)' }
    case 'CollectMoreEvidenceThenReviseAnswer': return { text: '需要更多證據後修正', color: '#f87171', bg: 'rgba(248,113,113,0.1)' }
    default: return { text: action, color: '#666666', bg: 'transparent' }
  }
})
</script>

<template>
  <div class="kimi-page-vscode">
    <div class="kimi-content">
      <div style="margin-top: 60px; margin-bottom: 12px">
        <button class="kimi-btn" @click="router.push({ name: 'agent-runs' })">← 返回列表</button>
      </div>

      <div v-if="isLoading" style="padding: 40px 0; color: var(--kimi-muted); font-size: 14px; text-align: center">載入中...</div>
      <div v-else-if="error || pollError" style="padding: 20px; color: #f87171; font-size: 14px">{{ error || pollError }}</div>

      <template v-else-if="run">
        <!-- Header -->
        <ScrollReveal>
          <div class="kimi-section">
            <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
              <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 8px; flex-wrap: wrap">
                <span class="kimi-tag" style="font-family: var(--kimi-font-mono)">{{ run.run.workflowType }}</span>
                <span class="kimi-tag">{{ run.run.agentType }}</span>
                <span class="kimi-tag" :style="{ borderColor: statusColor(run.run.status), color: statusColor(run.run.status) }">{{ run.run.status }}</span>
                <span v-if="duration" style="font-size: 12px; color: var(--kimi-muted)">{{ duration }}</span>
              </div>
              <div style="font-family: var(--kimi-font-mono); font-size: 12px; color: var(--kimi-muted); margin-bottom: 12px">{{ run.run.id }}</div>

              <div style="display: flex; gap: 8px; flex-wrap: wrap">
                <button v-if="run.run.status === 'Failed'" class="kimi-btn kimi-btn-solid" :disabled="actionLoading" @click="handleRetry">重試</button>
                <button v-if="run.run.status === 'Running' || run.run.status === 'Pending'" class="kimi-btn" style="border-color: #f87171; color: #f87171" :disabled="actionLoading" @click="handleCancel">取消</button>
                <button v-if="canCreateDraftRevision" class="kimi-btn kimi-btn-solid" :disabled="actionLoading" @click="handleCreateDraftRevision">產生修訂稿</button>
                <button class="kimi-btn" :disabled="actionLoading" @click="refresh">重新整理</button>
              </div>

              <div style="display: flex; gap: 20px; margin-top: 12px; font-size: 12px; color: var(--kimi-muted); flex-wrap: wrap">
                <span>建立：{{ formatDate(run.run.createdAtUtc) }}</span>
                <span>開始：{{ formatDate(run.run.startedAtUtc) }}</span>
                <span>完成：{{ formatDate(run.run.completedAtUtc) }}</span>
              </div>

              <div v-if="!isTerminalStatus || run.run.errorMessage" style="display: flex; align-items: center; gap: 8px; margin-top: 12px; font-size: 13px; flex-wrap: wrap">
                <span v-if="!isTerminalStatus" style="display: inline-flex; align-items: center; gap: 6px; color: #60a5fa">
                  <span style="display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #60a5fa; animation: pulse 1.5s infinite" />
                  {{ run.run.status === 'Pending' ? '已加入背景執行佇列，正在等待執行' : '背景執行中，頁面會自動更新' }}
                </span>
                <span v-if="run.run.errorMessage" style="color: #f87171">{{ run.run.errorMessage }}</span>
              </div>

              <div v-if="isPolling" style="display: flex; align-items: center; gap: 6px; margin-top: 8px; font-size: 12px; color: var(--kimi-muted)">
                <span style="display: inline-block; width: 6px; height: 6px; border-radius: 50%; background: #60a5fa; animation: pulse 1.5s infinite" />
                自動重新整理中
              </div>
            </div>

            <!-- Waiting for output -->
            <div v-if="!isTerminalStatus || run.outputJson === null" style="padding: 40px 20px; border-bottom: 1px solid var(--kimi-border-light); text-align: center; color: var(--kimi-muted)">
              <div style="font-size: 14px; margin-bottom: 8px">等待背景工作完成</div>
              <div style="font-size: 12px">結果產出後會自動顯示在這裡</div>
            </div>

            <!-- CriticReview Result -->
            <div v-if="criticOutput" style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
              <h3 style="margin: 0 0 16px 0; font-size: 16px; font-weight: 600">Critic Review 結果</h3>

              <div v-if="nextActionLabel" style="display: inline-block; padding: 6px 14px; margin-bottom: 16px; font-size: 13px; font-weight: 600" :style="{ color: nextActionLabel.color, background: nextActionLabel.bg, border: `1px solid ${nextActionLabel.color}` }">
                {{ nextActionLabel.text }}
              </div>

              <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 16px; margin-bottom: 16px">
                <div>
                  <div class="kimi-caption" style="margin-bottom: 4px">Overall Severity</div>
                  <div style="font-size: 14px; font-weight: 600">{{ criticOutput.overallSeverity ?? '-' }}</div>
                </div>
                <div>
                  <div class="kimi-caption" style="margin-bottom: 4px">Requires Revision</div>
                  <div :style="{ color: criticOutput.requiresRevision ? '#f87171' : '#34d399', fontSize: '14px', fontWeight: '600' }">{{ criticOutput.requiresRevision ? '是' : '否' }}</div>
                </div>
                <div>
                  <div class="kimi-caption" style="margin-bottom: 4px">Requires More Evidence</div>
                  <div :style="{ color: criticOutput.requiresMoreEvidence ? '#fbbf24' : '#34d399', fontSize: '14px', fontWeight: '600' }">{{ criticOutput.requiresMoreEvidence ? '是' : '否' }}</div>
                </div>
                <div v-if="criticOutput.routeBackTo">
                  <div class="kimi-caption" style="margin-bottom: 4px">Route Back To</div>
                  <div style="font-size: 14px">{{ criticOutput.routeBackTo }}</div>
                </div>
              </div>

              <div style="margin-bottom: 16px">
                <div class="kimi-caption" style="margin-bottom: 4px">Summary</div>
                <div style="font-size: 14px; line-height: 1.6">{{ criticOutput.summary ?? '-' }}</div>
              </div>

              <div v-if="criticOutput.findings && criticOutput.findings.length > 0">
                <div class="kimi-caption" style="margin-bottom: 8px">Findings ({{ criticOutput.findings.length }})</div>
                <div style="display: flex; flex-direction: column; gap: 8px">
                  <div v-for="(finding, fi) in criticOutput.findings" :key="fi" style="padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light)">
                    <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 4px">
                      <span class="kimi-tag" :style="{ borderColor: finding.severity === 'Critical' || finding.severity === 'High' ? '#f87171' : finding.severity === 'Medium' ? '#fbbf24' : '#34d399', color: finding.severity === 'Critical' || finding.severity === 'High' ? '#f87171' : finding.severity === 'Medium' ? '#fbbf24' : '#34d399', fontSize: '11px' }">{{ finding.severity }}</span>
                      <span class="kimi-tag" style="font-size: 11px">{{ finding.category }}</span>
                    </div>
                    <div style="font-size: 13px; margin-bottom: 4px">{{ finding.message }}</div>
                    <div style="font-size: 12px; color: var(--kimi-muted)">建議：{{ finding.recommendation }}</div>
                  </div>
                </div>
              </div>

              <div v-if="criticOutput.suggestedAnswerRevision" style="margin-top: 16px">
                <div class="kimi-caption" style="margin-bottom: 4px">Suggested Revision</div>
                <div style="font-size: 13px; color: #b45309; line-height: 1.6; padding: 12px; background: rgba(251,191,36,0.08); border: 1px solid rgba(251,191,36,0.2)">{{ criticOutput.suggestedAnswerRevision }}</div>
              </div>
            </div>

            <!-- DraftRevision Result -->
            <div v-if="draftOutput" style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
              <h3 style="margin: 0 0 16px 0; font-size: 16px; font-weight: 600">修訂結果</h3>

              <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 16px; margin-bottom: 16px">
                <div>
                  <div class="kimi-caption" style="margin-bottom: 4px">Revision Required</div>
                  <div :style="{ color: draftOutput.revisionRequired ? '#fbbf24' : '#34d399', fontSize: '14px', fontWeight: '600' }">{{ draftOutput.revisionRequired ? '是' : '否' }}</div>
                </div>
                <div v-if="draftOutput.appliedRecommendation">
                  <div class="kimi-caption" style="margin-bottom: 4px">Applied Recommendation</div>
                  <div style="font-size: 14px">{{ draftOutput.appliedRecommendation }}</div>
                </div>
              </div>

              <div v-if="draftOutput.revisionSummary" style="margin-bottom: 16px">
                <div class="kimi-caption" style="margin-bottom: 4px">修正說明</div>
                <div style="font-size: 14px; line-height: 1.6">{{ draftOutput.revisionSummary }}</div>
              </div>

              <div v-if="draftOutput.sourceAnswer" style="margin-bottom: 16px">
                <div class="kimi-caption" style="margin-bottom: 4px">原始答案</div>
                <div style="font-size: 13px; color: var(--kimi-muted); line-height: 1.6; padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); white-space: pre-wrap">{{ draftOutput.sourceAnswer }}</div>
              </div>

              <div v-if="draftOutput.revisedAnswer">
                <div class="kimi-caption" style="margin-bottom: 4px">修正版答案</div>
                <div style="font-size: 14px; line-height: 1.7; padding: 16px; background: rgba(52,211,153,0.06); border: 1px solid rgba(52,211,153,0.2); white-space: pre-wrap">{{ draftOutput.revisedAnswer }}</div>
              </div>
            </div>

            <!-- Debug Toggle -->
            <div style="padding: 12px 20px; border-bottom: 1px solid var(--kimi-border-light)">
              <button class="kimi-btn" style="font-size: 12px" @click="debugExpanded = !debugExpanded">
                {{ debugExpanded ? '收合除錯資訊 ▲' : '展開除錯資訊 ▼' }}
              </button>
            </div>

            <!-- Debug Tabs -->
            <template v-if="debugExpanded">
              <div style="display: flex; border-bottom: 1px solid var(--kimi-border-light); overflow-x: auto">
                <button
                  v-for="tab in (['timeline', 'nodes', 'toolCalls', 'feedback', 'blackboard', 'workflow'] as const)"
                  :key="tab"
                  :style="{ padding: '10px 16px', background: activeTab === tab ? 'var(--kimi-text-light)' : 'transparent', border: 'none', color: activeTab === tab ? 'var(--kimi-bg-light)' : 'var(--kimi-muted)', cursor: 'pointer', fontSize: '12px', fontWeight: activeTab === tab ? '600' : '400', fontFamily: 'var(--kimi-font-body)', letterSpacing: '0.05em' }"
                  @click="activeTab = tab"
                >
                  {{ tab === 'timeline' ? '時間線' : tab === 'nodes' ? '節點' : tab === 'toolCalls' ? '工具' : tab === 'feedback' ? 'Feedback' : tab === 'blackboard' ? 'Blackboard' : 'Workflow' }}
                </button>
              </div>

              <!-- Timeline -->
              <div v-if="activeTab === 'timeline'" style="padding: 0">
                <div v-for="evt in sortedEvents" :key="evt.id" style="display: flex; gap: 12px; padding: 10px 20px; border-bottom: 1px solid var(--kimi-border-light)">
                  <div :style="{ width: '8px', height: '8px', borderRadius: '50%', marginTop: '5px', flexShrink: 0, background: evt.eventType.includes('Failed') ? '#f87171' : evt.eventType.includes('Succeed') ? '#34d399' : evt.eventType.includes('Started') ? '#60a5fa' : '#ccc' }" />
                  <div style="flex: 1; min-width: 0">
                    <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 2px; flex-wrap: wrap">
                      <span style="font-family: var(--kimi-font-mono); font-size: 12px; font-weight: 600">{{ evt.eventType }}</span>
                      <span v-if="evt.agentRunNodeId" style="font-family: var(--kimi-font-mono); font-size: 11px; color: var(--kimi-muted)">node: {{ nodeMap.get(evt.agentRunNodeId)?.nodeKey ?? evt.agentRunNodeId.slice(0, 8) }}</span>
                    </div>
                    <div v-if="evt.message" style="font-size: 13px; color: var(--kimi-muted)">{{ evt.message }}</div>
                    <pre v-if="evt.payloadJson" style="margin-top: 4px; font-family: var(--kimi-font-mono); font-size: 11px; color: var(--kimi-muted); white-space: pre-wrap; word-break: break-all; background: var(--kimi-bg-alt); padding: 8px; border: 1px solid var(--kimi-border-light)">{{ prettyJson(evt.payloadJson) }}</pre>
                  </div>
                  <div style="font-size: 11px; color: var(--kimi-muted); white-space: nowrap">{{ formatDate(evt.createdAtUtc) }}</div>
                </div>
                <div v-if="sortedEvents.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center">暫無事件記錄。</div>
              </div>

              <!-- Nodes -->
              <div v-if="activeTab === 'nodes'">
                <div v-for="node in run.nodes" :key="node.id" style="padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)">
                  <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; gap: 12px">
                    <div style="display: flex; align-items: center; gap: 8px; flex-wrap: wrap">
                      <span style="font-family: var(--kimi-font-mono); font-size: 14px; font-weight: 600">{{ node.nodeKey }}</span>
                      <span class="kimi-tag" style="font-size: 11px">{{ node.nodeType }}</span>
                    </div>
                    <span class="kimi-tag" :style="{ borderColor: statusColor(node.status), color: statusColor(node.status) }">{{ node.status }}</span>
                  </div>
                  <div v-if="node.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 8px">{{ node.errorMessage }}</div>
                  <div style="display: flex; gap: 16px; font-size: 12px; color: var(--kimi-muted); flex-wrap: wrap">
                    <span>開始：{{ formatDate(node.startedAtUtc) }}</span>
                    <span>完成：{{ formatDate(node.completedAtUtc) }}</span>
                    <span v-if="node.durationMs !== null">耗時：{{ node.durationMs }}ms</span>
                  </div>
                  <pre v-if="node.outputJson" style="margin-top: 12px; padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); color: var(--kimi-muted); overflow-x: auto; font-size: 12px">{{ prettyJson(node.outputJson) }}</pre>
                </div>
              </div>

              <!-- Tool Calls -->
              <div v-if="activeTab === 'toolCalls'">
                <div v-for="tc in run.toolCalls" :key="tc.id" style="padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)">
                  <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; gap: 12px">
                    <span style="font-family: var(--kimi-font-mono); font-size: 14px">{{ tc.toolName }}</span>
                    <span class="kimi-tag" :style="{ borderColor: statusColor(tc.status), color: statusColor(tc.status) }">{{ tc.status }}</span>
                  </div>
                  <div v-if="tc.resultPreview" style="font-size: 13px; color: var(--kimi-muted); margin-bottom: 8px">{{ tc.resultPreview }}</div>
                  <div v-if="tc.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 8px">{{ tc.errorMessage }}</div>
                  <pre style="padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); color: var(--kimi-muted); overflow-x: auto; font-size: 12px">{{ prettyJson(tc.argumentsJson) }}</pre>
                </div>
                <div v-if="run.toolCalls.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center">暫無工具呼叫。</div>
              </div>

              <!-- Feedback -->
              <div v-if="activeTab === 'feedback'">
                <div v-for="item in run.feedback" :key="item.id" style="padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)">
                  <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; gap: 12px">
                    <div style="display: flex; align-items: center; gap: 8px; flex-wrap: wrap">
                      <span style="font-family: var(--kimi-font-mono); font-size: 14px">{{ item.feedbackType }}</span>
                      <span v-if="item.agentRunNodeId" style="font-family: var(--kimi-font-mono); font-size: 11px; color: var(--kimi-muted)">node: {{ nodeMap.get(item.agentRunNodeId)?.nodeKey ?? item.agentRunNodeId.slice(0, 8) }}</span>
                    </div>
                    <span class="kimi-tag" :style="{ borderColor: statusColor(item.status), color: statusColor(item.status) }">{{ item.status }}</span>
                  </div>
                  <div style="font-size: 13px; color: var(--kimi-muted); margin-bottom: 8px">{{ item.prompt }}</div>
                  <div style="display: flex; gap: 16px; font-size: 12px; color: var(--kimi-muted); flex-wrap: wrap">
                    <span>建立：{{ formatDate(item.createdAtUtc) }}</span>
                    <span>回覆：{{ formatDate(item.respondedAtUtc) }}</span>
                  </div>
                  <pre v-if="item.responseJson" style="margin-top: 12px; padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); color: var(--kimi-muted); overflow-x: auto; font-size: 12px">{{ prettyJson(item.responseJson) }}</pre>
                </div>
                <div v-if="run.feedback.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center">暫無 feedback。</div>
              </div>

              <!-- Blackboard -->
              <div v-if="activeTab === 'blackboard'" style="padding: 20px">
                <h3 style="margin: 0 0 8px 0; font-size: 14px; font-weight: 600">Blackboard</h3>
                <pre style="padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); color: var(--kimi-muted); overflow-x: auto; white-space: pre-wrap; font-size: 12px">{{ prettyJson(run.blackboardJson) }}</pre>
                <h3 style="margin: 16px 0 8px 0; font-size: 14px; font-weight: 600">Output</h3>
                <pre style="padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); color: var(--kimi-muted); overflow-x: auto; white-space: pre-wrap; font-size: 12px">{{ prettyJson(run.outputJson) }}</pre>
              </div>

              <!-- Workflow -->
              <div v-if="activeTab === 'workflow'" style="padding: 20px">
                <pre style="padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); color: var(--kimi-muted); overflow-x: auto; white-space: pre-wrap; font-size: 12px">{{ prettyJson(run.workflowDefinitionJson) }}</pre>
              </div>
            </template>
          </div>
        </ScrollReveal>

        <div style="height: 80px" />
      </template>
    </div>

    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">AGENT RUN DETAIL</span>
      <span>數據僅供參考</span>
    </footer>
  </div>
</template>
