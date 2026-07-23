<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import AgentRunProgress from '../../components/agents/AgentRunProgress.vue'
import CapabilityRequestStatus from '../../components/agents/CapabilityRequestStatus.vue'
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

interface AttributionItem {
  evidenceId: string
  name: string
  industry?: string | null
  weight: number
  return: number
  contribution: number
}

interface PortfolioDiagnosisOutput {
  summary: string
  portfolioReturn?: number | null
  benchmarkReturn?: number | null
  activeReturn?: number | null
  mainDrags: AttributionItem[]
  mainContributors: AttributionItem[]
  recommendedAnalyses: Array<{ evidenceId: string; priority: number; analysis: string; reason: string }>
  evidenceStatus: string
}

interface RoutingContext {
  leadSkill: string
  leadSkillDisplayName: string
  objective: string
  routingReason: string
  confidence: string
  routingModel: string
  contextEnvelope: {
    market: string
    asset: string
    depth: string
    horizon?: string | null
    currency?: string | null
    language: string
  }
}

const route = useRoute()
const router = useRouter()
const actionLoading = ref(false)
const activeTab = ref<'timeline' | 'nodes' | 'toolCalls' | 'feedback' | 'blackboard' | 'workflow'>('timeline')
const debugExpanded = ref(false)

const runId = computed(() => route.params.id as string)
const { agentRun: run, isLoading, isPolling, isTerminalStatus, error: pollError, startPolling, refresh } = useAgentRunPolling(runId)
const error = ref('')

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

function formatPercent(value: number | null | undefined) {
  return value == null ? '資料不足' : new Intl.NumberFormat('zh-TW', { style: 'percent', minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value)
}

function statusColor(status: string) {
  switch (status) {
    case 'Succeeded': return '#7fa387'
    case 'Failed': return '#b05c5c'
    case 'Running': return '#d4a24e'
    case 'Cancelled': return '#9a917c'
    case 'Pending': return '#9a917c'
    case 'WaitingForFeedback': return '#c9a86a'
    case 'Skipped': return '#9a917c'
    default: return '#9a917c'
  }
}

function severityColor(severity: string) {
  if (severity === 'Critical' || severity === 'High') return '#b05c5c'
  if (severity === 'Medium') return '#d4a24e'
  return '#7fa387'
}

function eventColor(eventType: string) {
  if (eventType.includes('Failed')) return '#b05c5c'
  if (eventType.includes('Succeed')) return '#7fa387'
  if (eventType.includes('Started')) return '#d4a24e'
  return '#9a917c'
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

const portfolioDiagnosisOutput = computed((): PortfolioDiagnosisOutput | null => {
  if (!run.value || run.value.run.workflowType !== 'PortfolioDiagnosis') return null
  return (run.value.outputJson as unknown as PortfolioDiagnosisOutput) ?? null
})

const routingContext = computed((): RoutingContext | null => {
  if (!run.value) return null
  return (run.value.blackboardJson.routingContext as unknown as RoutingContext) ?? null
})

const capabilityAssessment = computed(() => {
  if (!run.value) return null
  return (run.value.blackboardJson.capabilityRequestAssessment as {
    decision: string
    reason: string
    evidenceGaps?: string[]
    confidence: string
    mode: string
  } | null) ?? null
})

const capabilityRequests = computed(() => {
  if (!run.value) return []
  return (run.value.blackboardJson.capabilityRequests as Array<{
    requestId: string
    capabilityId: string
    status: string
    reason: string
    reviewReason?: string | null
  }>) ?? []
})

const nextActionLabel = computed(() => {
  const action = criticOutput.value?.recommendedNextAction
  if (!action) return null
  switch (action) {
    case 'AcceptAnswer': return { text: '答案可接受', color: '#7fa387', bg: 'rgba(127,163,135,0.1)' }
    case 'ReviseAnswer': return { text: '建議修正答案', color: '#d4a24e', bg: 'rgba(212,162,78,0.1)' }
    case 'CollectMoreEvidenceThenReviseAnswer': return { text: '需要更多證據後修正', color: '#b05c5c', bg: 'rgba(176,92,92,0.1)' }
    default: return { text: action, color: '#9a917c', bg: 'transparent' }
  }
})
</script>

<template>
  <div class="prestige-page">
    <div class="prestige-section">
      <div class="back-row">
        <button class="prestige-btn" @click="router.push({ name: 'agent-runs' })">← 返回列表</button>
      </div>

      <div v-if="isLoading" class="prestige-skeleton detail-skeleton" />
      <div v-else-if="error || pollError" class="prestige-error">{{ error || pollError }}</div>

      <template v-else-if="run">
        <ScrollReveal>
          <!-- Header -->
          <div class="prestige-panel prestige-panel-pad header-panel">
            <div class="run-tags">
              <span class="prestige-tag prestige-mono">{{ run.run.workflowType }}</span>
              <span class="prestige-tag">{{ run.run.agentType }}</span>
              <span
                class="prestige-tag"
                :style="{ borderColor: statusColor(run.run.status), color: statusColor(run.run.status) }"
              >{{ run.run.status }}</span>
              <span v-if="duration" class="run-duration prestige-mono">{{ duration }}</span>
            </div>
            <div class="run-id prestige-mono">{{ run.run.id }}</div>

            <div class="action-row">
              <button v-if="run.run.parentAgentRunId" class="prestige-btn" @click="router.push({ name: 'agent-run-detail', params: { id: run.run.parentAgentRunId } })">查看父版本</button>
              <button v-if="run.run.status === 'Failed'" class="prestige-btn prestige-btn-solid" :disabled="actionLoading" @click="handleRetry">重試</button>
              <button v-if="run.run.status === 'Running' || run.run.status === 'Pending'" class="prestige-btn btn-danger" :disabled="actionLoading" @click="handleCancel">取消</button>
              <button v-if="canCreateDraftRevision" class="prestige-btn prestige-btn-solid" :disabled="actionLoading" @click="handleCreateDraftRevision">產生修訂稿</button>
              <button class="prestige-btn" :disabled="actionLoading" @click="refresh">重新整理</button>
            </div>

            <div class="run-dates prestige-mono">
              <span>建立：{{ formatDate(run.run.createdAtUtc) }}</span>
              <span>開始：{{ formatDate(run.run.startedAtUtc) }}</span>
              <span>完成：{{ formatDate(run.run.completedAtUtc) }}</span>
            </div>

            <div v-if="!isTerminalStatus || run.run.errorMessage" class="live-row">
              <span v-if="!isTerminalStatus" class="live-status">
                <span class="live-dot" />
                {{ run.run.status === 'Pending' ? '已加入背景執行佇列，正在等待執行' : '背景執行中，頁面會自動更新' }}
              </span>
              <span v-if="run.run.errorMessage" class="run-error">{{ run.run.errorMessage }}</span>
            </div>

            <div v-if="isPolling" class="polling-row">
              <span class="live-dot live-dot-small" />
              自動重新整理中
            </div>
          </div>

          <div v-if="routingContext" class="prestige-panel prestige-panel-pad routing-stage" data-testid="routing-stage">
            <div>
              <span class="prestige-label">STAGE 0 · 問題路由</span>
              <h3>{{ routingContext.leadSkillDisplayName }}</h3>
              <code>{{ routingContext.leadSkill }}</code>
            </div>
            <dl>
              <div><dt>目標</dt><dd>{{ routingContext.objective }}</dd></div>
              <div><dt>理由</dt><dd>{{ routingContext.routingReason }}</dd></div>
              <div><dt>市場／資產</dt><dd>{{ routingContext.contextEnvelope.market }} / {{ routingContext.contextEnvelope.asset }}</dd></div>
              <div><dt>深度／信心度</dt><dd>{{ routingContext.contextEnvelope.depth }} / {{ routingContext.confidence }}</dd></div>
              <div><dt>模型</dt><dd>{{ routingContext.routingModel }}</dd></div>
            </dl>
          </div>

          <AgentRunProgress
            :nodes="run.nodes"
            :workflow-definition="run.workflowDefinitionJson"
            :run-status="run.run.status"
            :started-at-utc="run.run.startedAtUtc"
            :completed-at-utc="run.run.completedAtUtc"
          />

          <CapabilityRequestStatus
            :assessment="capabilityAssessment"
            :requests="capabilityRequests"
          />

          <!-- Waiting for output -->
          <div v-if="!isTerminalStatus || run.outputJson === null" class="prestige-empty waiting-block">
            <div style="font-size: 14px; margin-bottom: 8px">等待背景工作完成</div>
            <div style="font-size: 12px">結果產出後會自動顯示在這裡</div>
          </div>

          <!-- CriticReview Result -->
          <div v-if="criticOutput" class="prestige-panel prestige-panel-pad result-panel">
            <h3 class="panel-title">Critic Review 結果</h3>

            <div
              v-if="nextActionLabel"
              class="next-action"
              :style="{ color: nextActionLabel.color, background: nextActionLabel.bg, border: `1px solid ${nextActionLabel.color}` }"
            >
              {{ nextActionLabel.text }}
            </div>

            <div class="stat-grid">
              <div>
                <div class="prestige-label stat-caption">Overall Severity</div>
                <div class="stat-value">{{ criticOutput.overallSeverity ?? '-' }}</div>
              </div>
              <div>
                <div class="prestige-label stat-caption">Requires Revision</div>
                <div class="stat-value" :style="{ color: criticOutput.requiresRevision ? '#b05c5c' : '#7fa387' }">{{ criticOutput.requiresRevision ? '是' : '否' }}</div>
              </div>
              <div>
                <div class="prestige-label stat-caption">Requires More Evidence</div>
                <div class="stat-value" :style="{ color: criticOutput.requiresMoreEvidence ? '#d4a24e' : '#7fa387' }">{{ criticOutput.requiresMoreEvidence ? '是' : '否' }}</div>
              </div>
              <div v-if="criticOutput.routeBackTo">
                <div class="prestige-label stat-caption">Route Back To</div>
                <div class="stat-value">{{ criticOutput.routeBackTo }}</div>
              </div>
            </div>

            <div class="summary-block">
              <div class="prestige-label stat-caption">Summary</div>
              <div class="body-text">{{ criticOutput.summary ?? '-' }}</div>
            </div>

            <div v-if="criticOutput.findings && criticOutput.findings.length > 0">
              <div class="prestige-label stat-caption">Findings ({{ criticOutput.findings.length }})</div>
              <div class="finding-list">
                <div v-for="(finding, fi) in criticOutput.findings" :key="fi" class="finding-item">
                  <div class="run-tags finding-tags">
                    <span
                      class="prestige-tag finding-tag"
                      :style="{ borderColor: severityColor(finding.severity), color: severityColor(finding.severity) }"
                    >{{ finding.severity }}</span>
                    <span class="prestige-tag finding-tag">{{ finding.category }}</span>
                  </div>
                  <div class="finding-msg">{{ finding.message }}</div>
                  <div class="finding-rec">建議：{{ finding.recommendation }}</div>
                </div>
              </div>
            </div>

            <div v-if="criticOutput.suggestedAnswerRevision" class="revision-block">
              <div class="prestige-label stat-caption">Suggested Revision</div>
              <div class="suggested-revision">{{ criticOutput.suggestedAnswerRevision }}</div>
            </div>
          </div>

          <!-- DraftRevision Result -->
          <div v-if="draftOutput" class="prestige-panel prestige-panel-pad result-panel">
            <h3 class="panel-title">修訂結果</h3>

            <div class="stat-grid">
              <div>
                <div class="prestige-label stat-caption">Revision Required</div>
                <div class="stat-value" :style="{ color: draftOutput.revisionRequired ? '#d4a24e' : '#7fa387' }">{{ draftOutput.revisionRequired ? '是' : '否' }}</div>
              </div>
              <div v-if="draftOutput.appliedRecommendation">
                <div class="prestige-label stat-caption">Applied Recommendation</div>
                <div class="stat-value">{{ draftOutput.appliedRecommendation }}</div>
              </div>
            </div>

            <div v-if="draftOutput.revisionSummary" class="summary-block">
              <div class="prestige-label stat-caption">修正說明</div>
              <div class="body-text">{{ draftOutput.revisionSummary }}</div>
            </div>

            <div v-if="draftOutput.sourceAnswer" class="summary-block">
              <div class="prestige-label stat-caption">原始答案</div>
              <div class="source-answer">{{ draftOutput.sourceAnswer }}</div>
            </div>

            <div v-if="draftOutput.revisedAnswer">
              <div class="prestige-label stat-caption">修正版答案</div>
              <div class="revised-answer">{{ draftOutput.revisedAnswer }}</div>
            </div>
          </div>

          <!-- PortfolioDiagnosis Result -->
          <div v-if="portfolioDiagnosisOutput" class="prestige-panel prestige-panel-pad result-panel">
            <h3 class="panel-title">AI 投組診斷報告</h3>
            <div class="stat-grid">
              <div><div class="prestige-label stat-caption">投組報酬</div><div class="stat-value">{{ formatPercent(portfolioDiagnosisOutput.portfolioReturn) }}</div></div>
              <div><div class="prestige-label stat-caption">大盤報酬</div><div class="stat-value">{{ formatPercent(portfolioDiagnosisOutput.benchmarkReturn) }}</div></div>
              <div><div class="prestige-label stat-caption">相對報酬</div><div class="stat-value" :style="{ color: (portfolioDiagnosisOutput.activeReturn ?? 0) >= 0 ? '#7fa387' : '#b05c5c' }">{{ formatPercent(portfolioDiagnosisOutput.activeReturn) }}</div></div>
              <div><div class="prestige-label stat-caption">證據覆蓋</div><div class="stat-value">{{ portfolioDiagnosisOutput.evidenceStatus === 'complete' ? '完整' : '部分' }}</div></div>
            </div>
            <div class="summary-block"><div class="prestige-label stat-caption">摘要</div><div class="body-text">{{ portfolioDiagnosisOutput.summary }}</div></div>
            <div class="finding-list" style="margin-top: 18px">
              <div class="prestige-label stat-caption">主要拖累</div>
              <div v-if="portfolioDiagnosisOutput.mainDrags.length === 0" class="body-text">資料不足，無法列出拖累來源。</div>
              <div v-for="item in portfolioDiagnosisOutput.mainDrags" :key="item.evidenceId" class="finding-item"><div class="finding-msg">{{ item.name }}</div><div class="finding-rec">貢獻：{{ formatPercent(item.contribution) }} · 報酬：{{ formatPercent(item.return) }} · 權重：{{ formatPercent(item.weight) }}</div></div>
            </div>
            <div class="finding-list" style="margin-top: 18px">
              <div class="prestige-label stat-caption">主要貢獻</div>
              <div v-if="portfolioDiagnosisOutput.mainContributors.length === 0" class="body-text">資料不足，無法列出貢獻來源。</div>
              <div v-for="item in portfolioDiagnosisOutput.mainContributors" :key="item.evidenceId" class="finding-item"><div class="finding-msg">{{ item.name }}</div><div class="finding-rec">貢獻：{{ formatPercent(item.contribution) }} · 報酬：{{ formatPercent(item.return) }} · 權重：{{ formatPercent(item.weight) }}</div></div>
            </div>
            <div class="finding-list" style="margin-top: 18px">
              <div class="prestige-label stat-caption">建議補做的風險分析</div>
              <div v-for="item in portfolioDiagnosisOutput.recommendedAnalyses" :key="item.evidenceId" class="finding-item"><div class="finding-msg">{{ item.priority }}. {{ item.analysis }}</div><div class="finding-rec">{{ item.reason }}</div></div>
            </div>
          </div>

          <!-- Debug Panel -->
          <div class="prestige-panel debug-panel">
            <div class="debug-toggle-row">
              <button class="prestige-btn debug-toggle" @click="debugExpanded = !debugExpanded">
                {{ debugExpanded ? '收合除錯資訊 ▲' : '展開除錯資訊 ▼' }}
              </button>
            </div>

            <template v-if="debugExpanded">
              <div class="tab-bar">
                <button
                  v-for="tab in (['timeline', 'nodes', 'toolCalls', 'feedback', 'blackboard', 'workflow'] as const)"
                  :key="tab"
                  class="tab-btn"
                  :class="{ active: activeTab === tab }"
                  @click="activeTab = tab"
                >
                  {{ tab === 'timeline' ? '時間線' : tab === 'nodes' ? '節點' : tab === 'toolCalls' ? '工具' : tab === 'feedback' ? 'Feedback' : tab === 'blackboard' ? 'Blackboard' : 'Workflow' }}
                </button>
              </div>

              <!-- Timeline -->
              <div v-if="activeTab === 'timeline'">
                <div v-for="evt in sortedEvents" :key="evt.id" class="list-row event-row">
                  <div class="event-dot" :style="{ background: eventColor(evt.eventType) }" />
                  <div class="event-main">
                    <div class="event-head">
                      <span class="prestige-mono event-type">{{ evt.eventType }}</span>
                      <span v-if="evt.agentRunNodeId" class="prestige-mono event-node">node: {{ nodeMap.get(evt.agentRunNodeId)?.nodeKey ?? evt.agentRunNodeId.slice(0, 8) }}</span>
                    </div>
                    <div v-if="evt.message" class="event-msg">{{ evt.message }}</div>
                    <pre v-if="evt.payloadJson" class="code-block prestige-mono">{{ prettyJson(evt.payloadJson) }}</pre>
                  </div>
                  <div class="event-time prestige-mono">{{ formatDate(evt.createdAtUtc) }}</div>
                </div>
                <div v-if="sortedEvents.length === 0" class="prestige-empty inner-empty">暫無事件記錄。</div>
              </div>

              <!-- Nodes -->
              <div v-if="activeTab === 'nodes'">
                <div v-for="node in run.nodes" :key="node.id" class="list-row">
                  <div class="row-head">
                    <div class="run-tags">
                      <span class="prestige-mono node-key">{{ node.nodeKey }}</span>
                      <span class="prestige-tag finding-tag">{{ node.nodeType }}</span>
                    </div>
                    <span
                      class="prestige-tag"
                      :style="{ borderColor: statusColor(node.status), color: statusColor(node.status) }"
                    >{{ node.status }}</span>
                  </div>
                  <div v-if="node.errorMessage" class="run-error">{{ node.errorMessage }}</div>
                  <div class="row-meta prestige-mono">
                    <span>開始：{{ formatDate(node.startedAtUtc) }}</span>
                    <span>完成：{{ formatDate(node.completedAtUtc) }}</span>
                    <span v-if="node.durationMs !== null">耗時：{{ node.durationMs }}ms</span>
                  </div>
                  <pre v-if="node.outputJson" class="code-block prestige-mono">{{ prettyJson(node.outputJson) }}</pre>
                </div>
              </div>

              <!-- Tool Calls -->
              <div v-if="activeTab === 'toolCalls'">
                <div v-for="tc in run.toolCalls" :key="tc.id" class="list-row">
                  <div class="row-head">
                    <span class="prestige-mono node-key">{{ tc.toolName }}</span>
                    <span
                      class="prestige-tag"
                      :style="{ borderColor: statusColor(tc.status), color: statusColor(tc.status) }"
                    >{{ tc.status }}</span>
                  </div>
                  <div v-if="tc.resultPreview" class="event-msg">{{ tc.resultPreview }}</div>
                  <div v-if="tc.errorMessage" class="run-error">{{ tc.errorMessage }}</div>
                  <pre class="code-block prestige-mono">{{ prettyJson(tc.argumentsJson) }}</pre>
                </div>
                <div v-if="run.toolCalls.length === 0" class="prestige-empty inner-empty">暫無工具呼叫。</div>
              </div>

              <!-- Feedback -->
              <div v-if="activeTab === 'feedback'">
                <div v-for="item in run.feedback" :key="item.id" class="list-row">
                  <div class="row-head">
                    <div class="run-tags">
                      <span class="prestige-mono node-key">{{ item.feedbackType }}</span>
                      <span v-if="item.agentRunNodeId" class="prestige-mono event-node">node: {{ nodeMap.get(item.agentRunNodeId)?.nodeKey ?? item.agentRunNodeId.slice(0, 8) }}</span>
                    </div>
                    <span
                      class="prestige-tag"
                      :style="{ borderColor: statusColor(item.status), color: statusColor(item.status) }"
                    >{{ item.status }}</span>
                  </div>
                  <div class="event-msg">{{ item.prompt }}</div>
                  <div class="row-meta prestige-mono">
                    <span>建立：{{ formatDate(item.createdAtUtc) }}</span>
                    <span>回覆：{{ formatDate(item.respondedAtUtc) }}</span>
                  </div>
                  <button v-if="item.followUpAgentRunId" class="prestige-btn follow-up-link" @click="router.push({ name: 'agent-run-detail', params: { id: item.followUpAgentRunId } })">查看修訂版本 →</button>
                  <pre v-if="item.responseJson" class="code-block prestige-mono">{{ prettyJson(item.responseJson) }}</pre>
                </div>
                <div v-if="run.feedback.length === 0" class="prestige-empty inner-empty">暫無 feedback。</div>
              </div>

              <!-- Blackboard -->
              <div v-if="activeTab === 'blackboard'" class="tab-pad">
                <h3 class="sub-title">Blackboard</h3>
                <pre class="code-block prestige-mono">{{ prettyJson(run.blackboardJson) }}</pre>
                <h3 class="sub-title sub-title-gap">Output</h3>
                <pre class="code-block prestige-mono">{{ prettyJson(run.outputJson) }}</pre>
              </div>

              <!-- Workflow -->
              <div v-if="activeTab === 'workflow'" class="tab-pad">
                <pre class="code-block prestige-mono">{{ prettyJson(run.workflowDefinitionJson) }}</pre>
              </div>
            </template>
          </div>
        </ScrollReveal>
      </template>
    </div>
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.back-row {
  margin-bottom: 16px;
}

.detail-skeleton {
  min-height: 240px;
}

.header-panel,
.result-panel,
.debug-panel,
.waiting-block {
  margin-bottom: 16px;
}

.run-tags {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  flex-wrap: wrap;
}

.run-duration {
  font-size: 12px;
  color: var(--muted);
}

.run-id {
  font-size: 12px;
  color: var(--muted);
  margin-bottom: 12px;
  word-break: break-all;
}

.action-row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.btn-danger {
  border-color: rgba(176, 92, 92, 0.5);
  color: var(--down);
}

.btn-danger:hover {
  border-color: var(--down);
  background: rgba(176, 92, 92, 0.08);
}

.run-dates {
  display: flex;
  gap: 20px;
  margin-top: 12px;
  font-size: 12px;
  color: var(--muted);
  flex-wrap: wrap;
}

.live-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 12px;
  font-size: 13px;
  flex-wrap: wrap;
}

.live-status {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: #d4a24e;
}

.live-dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #d4a24e;
  animation: prestige-dot-pulse 1.5s ease-in-out infinite;
}

.live-dot-small {
  width: 6px;
  height: 6px;
}

@keyframes prestige-dot-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.polling-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 8px;
  font-size: 12px;
  color: var(--muted);
}

.run-error {
  color: var(--down);
  font-size: 13px;
  margin-bottom: 8px;
}

.panel-title {
  margin: 0 0 16px;
  font-family: var(--serif);
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.next-action {
  display: inline-block;
  padding: 6px 14px;
  margin-bottom: 16px;
  border-radius: 4px;
  font-size: 13px;
  font-weight: 600;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 16px;
  margin-bottom: 16px;
}

.stat-caption {
  margin-bottom: 4px;
}

.stat-value {
  font-size: 14px;
  font-weight: 600;
}

.summary-block {
  margin-bottom: 16px;
}

.body-text {
  font-size: 14px;
  line-height: 1.6;
}

.finding-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.finding-item {
  padding: 12px;
  background: var(--panel-bg);
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
}

.finding-tags {
  margin-bottom: 4px;
}

.finding-tag {
  font-size: 11px;
}

.finding-msg {
  font-size: 13px;
  margin-bottom: 4px;
}

.finding-rec {
  font-size: 12px;
  color: var(--muted);
}

.revision-block {
  margin-top: 16px;
}

.suggested-revision {
  font-size: 13px;
  color: #d4a24e;
  line-height: 1.6;
  padding: 12px;
  background: rgba(212, 162, 78, 0.08);
  border: 1px solid rgba(212, 162, 78, 0.25);
  border-radius: 4px;
}

.source-answer {
  font-size: 13px;
  color: var(--muted);
  line-height: 1.6;
  padding: 12px;
  background: var(--panel-bg);
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  white-space: pre-wrap;
}

.revised-answer {
  font-size: 14px;
  line-height: 1.7;
  padding: 16px;
  background: rgba(127, 163, 135, 0.06);
  border: 1px solid rgba(127, 163, 135, 0.25);
  border-radius: 4px;
  white-space: pre-wrap;
}

.debug-toggle-row {
  padding: 12px 20px;
}

.debug-toggle {
  font-size: 12px;
}

.tab-bar {
  display: flex;
  border-top: 1px solid var(--gold-border-soft);
  border-bottom: 1px solid var(--gold-border-soft);
  overflow-x: auto;
}

.tab-btn {
  padding: 10px 16px;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  color: var(--muted);
  cursor: pointer;
  font-family: var(--sans);
  font-size: 12px;
  font-weight: 400;
  letter-spacing: 0.05em;
  transition: color 0.2s ease, border-color 0.2s ease;
}

.tab-btn:hover {
  color: var(--ivory);
}

.tab-btn.active {
  color: var(--gold);
  font-weight: 600;
  border-bottom-color: var(--gold);
}

.list-row {
  padding: 16px 20px;
  border-bottom: 1px solid var(--gold-border-soft);
}

.list-row:last-child {
  border-bottom: none;
}

.event-row {
  display: flex;
  gap: 12px;
}

.event-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-top: 5px;
  flex-shrink: 0;
}

.event-main {
  flex: 1;
  min-width: 0;
}

.event-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 2px;
  flex-wrap: wrap;
}

.event-type {
  font-size: 12px;
  font-weight: 600;
}

.event-node {
  font-size: 11px;
  color: var(--muted);
}

.event-msg {
  font-size: 13px;
  color: var(--muted);
  margin-bottom: 8px;
}

.event-time {
  font-size: 11px;
  color: var(--muted);
  white-space: nowrap;
}

.row-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
  gap: 12px;
}

.row-head .run-tags {
  margin-bottom: 0;
}

.node-key {
  font-size: 14px;
  font-weight: 600;
}

.row-meta {
  display: flex;
  gap: 16px;
  font-size: 12px;
  color: var(--muted);
  flex-wrap: wrap;
}

.code-block {
  margin: 12px 0 0;
  padding: 12px;
  background: rgba(11, 18, 32, 0.6);
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  color: var(--ivory);
  font-size: 12px;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

.tab-pad {
  padding: 20px;
}

.sub-title {
  margin: 0 0 8px;
  font-family: var(--serif);
  font-size: 14px;
  font-weight: 600;
}

.sub-title-gap {
  margin-top: 16px;
}

.inner-empty {
  margin: 16px 20px;
}

.routing-stage {
  display: grid;
  grid-template-columns: minmax(220px, .7fr) minmax(0, 1.3fr);
  gap: 24px;
  margin-top: 16px;
  border-color: var(--gold-border);
}

.routing-stage h3 {
  margin: 8px 0 5px;
  color: var(--ivory);
  font-family: var(--serif);
  font-size: 22px;
}

.routing-stage code { color: var(--gold); overflow-wrap: anywhere; }
.routing-stage dl { display: grid; gap: 7px; margin: 0; }
.routing-stage dl div { display: grid; grid-template-columns: 92px minmax(0, 1fr); gap: 12px; }
.routing-stage dt { color: var(--muted); }.routing-stage dd { margin: 0; color: var(--ivory); }

@media (max-width: 700px) {
  .routing-stage { grid-template-columns: 1fr; }
  .routing-stage dl div { grid-template-columns: 1fr; gap: 2px; }
}
</style>
