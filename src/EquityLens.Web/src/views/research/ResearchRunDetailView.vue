<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import AgentRunProgress from '../../components/agents/AgentRunProgress.vue'
import { getResearchRun, type ResearchRunDetail } from '../../services/research'
import { getAgentRun, listAgentRuns, submitAgentFeedback, type AgentRunDetail, type AgentRunListItem } from '../../services/agentRuns'

const route = useRoute()
const router = useRouter()
const runDetail = ref<ResearchRunDetail | null>(null)
const loading = ref(true)
const error = ref('')
const activeTab = ref<'answer' | 'citations' | 'steps' | 'candidates' | 'agentRuns'>('answer')
const agentRuns = ref<AgentRunListItem[]>([])
const agentRunDetail = ref<AgentRunDetail | null>(null)
const refreshing = ref(false)
const feedbackComment = ref('')
const showCorrection = ref(false)
const feedbackSubmitting = ref(false)
const feedbackMessage = ref('')
const feedbackError = ref('')
let pollTimer: ReturnType<typeof setInterval> | null = null
const investigationRun = computed(() => agentRuns.value.find(run => run.workflowType === 'FeedbackRevision') ?? agentRuns.value.find(run => run.workflowType === 'ResearchInvestigation') ?? agentRuns.value.find(run => run.workflowType === 'ResearchQualityReview') ?? null)
const isTerminal = computed(() => ['Answered', 'Failed', 'Cancelled'].includes(runDetail.value?.run.status ?? ''))

onMounted(() => { loadRun(true); pollTimer = setInterval(() => loadRun(false), 2000) })
onUnmounted(stopPolling)

async function loadRun(showLoading = false) {
  if (refreshing.value) return
  const id = route.params.id as string
  refreshing.value = true
  if (showLoading) loading.value = true
  error.value = ''
  try {
    runDetail.value = await getResearchRun(id)
    await loadAgentRuns(id)
    if (isTerminal.value) stopPolling()
  } catch {
    error.value = '無法載入研究結果。'
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

async function loadAgentRuns(researchRunId: string) {
  try {
    const all = await listAgentRuns({ limit: 50, researchRunId })
    agentRuns.value = all
    const investigation = all.find(run => run.workflowType === 'FeedbackRevision') ?? all.find(run => run.workflowType === 'ResearchInvestigation') ?? all.find(run => run.workflowType === 'ResearchQualityReview')
    agentRunDetail.value = investigation ? await getAgentRun(investigation.id) : null
  } catch {
    // non-critical
  }
}

async function sendFeedback(type: 'Helpful' | 'NeedsCorrection') {
  if (!investigationRun.value || feedbackSubmitting.value) return
  feedbackError.value = ''
  feedbackMessage.value = ''
  if (type === 'NeedsCorrection' && feedbackComment.value.trim().length < 5) {
    feedbackError.value = '請至少輸入 5 個字的修正要求。'
    return
  }
  feedbackSubmitting.value = true
  try {
    const result = await submitAgentFeedback(investigationRun.value.id, type, feedbackComment.value)
    if (result.followUpAgentRun) {
      await router.push({ name: 'agent-run-detail', params: { id: result.followUpAgentRun.id } })
    } else {
      feedbackMessage.value = '謝謝你的回饋。'
      await loadAgentRuns(route.params.id as string)
    }
  } catch (e: any) {
    feedbackError.value = e?.response?.data?.message || '無法送出回饋。'
  } finally {
    feedbackSubmitting.value = false
  }
}

function stopPolling() {
  if (pollTimer) clearInterval(pollTimer)
  pollTimer = null
}

function formatDate(iso: string | null) {
  if (!iso) return '-'
  return new Date(iso).toLocaleString('zh-TW')
}

function statusColor(status: string) {
  switch (status) {
    case 'Succeeded': return '#7fa387'
    case 'Failed': return '#b05c5c'
    case 'Running': return '#d4a24e'
    case 'Cancelled': return '#9a917c'
    case 'Pending': return '#d4a24e'
    default: return '#9a917c'
  }
}
</script>

<template>
  <div class="prestige-page">
    <div class="prestige-section detail-section">
      <div class="back-row">
        <button class="prestige-btn" @click="router.push({ name: 'research' })">← 返回列表</button>
      </div>

      <!-- Loading skeleton -->
      <div v-if="loading" class="skeleton-stack">
        <div v-for="i in 2" :key="i" class="prestige-skeleton" />
      </div>

      <!-- Error -->
      <div v-else-if="error" class="prestige-error">{{ error }}</div>

      <ScrollReveal v-else-if="runDetail">
        <!-- Header -->
        <div class="prestige-panel prestige-panel-pad">
          <div class="tag-row">
            <span class="prestige-tag prestige-mono">{{ runDetail.run.ticker }}</span>
            <span class="prestige-tag" :style="{ borderColor: statusColor(runDetail.run.status), color: statusColor(runDetail.run.status) }">{{ runDetail.run.status }}</span>
            <span class="prestige-tag">{{ runDetail.run.retrievalMode }}</span>
          </div>
          <h2 class="question-title">{{ runDetail.run.question }}</h2>
          <div class="head-actions">
            <button v-if="runDetail.run.parentResearchRunId" class="prestige-btn" @click="router.push({ name: 'research-run-detail', params: { id: runDetail.run.parentResearchRunId } })">查看原始版本</button>
            <button v-if="investigationRun" class="prestige-btn prestige-btn-solid" @click="router.push({ name: 'agent-run-detail', params: { id: investigationRun.id } })">查看 Investigation</button>
            <button class="prestige-btn" :disabled="refreshing" @click="loadRun(false)">重新整理</button>
          </div>
          <div class="meta-row">
            <span>Citations：<span class="prestige-mono">{{ runDetail.run.citationCount }}</span></span>
            <span>延遲：<span class="prestige-mono">{{ runDetail.run.latencyMs }}ms</span></span>
            <span>建立：<span class="prestige-mono">{{ formatDate(runDetail.run.createdAtUtc) }}</span></span>
          </div>
        </div>

        <AgentRunProgress
          v-if="agentRunDetail"
          :nodes="agentRunDetail.nodes"
          :workflow-definition="agentRunDetail.workflowDefinitionJson"
          :run-status="agentRunDetail.run.status"
          :started-at-utc="agentRunDetail.run.startedAtUtc"
          :completed-at-utc="agentRunDetail.run.completedAtUtc"
        />

        <!-- Tabs -->
        <div class="tabs">
          <button
            v-for="tab in (['answer', 'citations', 'steps', 'candidates', 'agentRuns'] as const)"
            :key="tab"
            :class="['tab-btn', { active: activeTab === tab }]"
            @click="activeTab = tab"
          >
            {{ tab === 'answer' ? '研究答案' : tab === 'citations' ? '引用來源' : tab === 'steps' ? '執行步驟' : tab === 'candidates' ? '候選文件' : 'Agent Runs' }}
          </button>
        </div>

        <!-- Tab content -->
        <div class="prestige-panel tab-panel">
          <!-- Tab: Answer -->
          <div v-if="activeTab === 'answer' && runDetail.run.status === 'Answered'">
            <div class="answer-body">{{ runDetail.answer }}</div>
            <div class="feedback-panel">
              <div class="feedback-title">這份研究結果有解決你的問題嗎？</div>
              <div class="feedback-actions">
                <button class="prestige-btn" :disabled="feedbackSubmitting" @click="sendFeedback('Helpful')">有幫助</button>
                <button class="prestige-btn" :disabled="feedbackSubmitting" @click="showCorrection = !showCorrection">要求修正</button>
              </div>
              <div v-if="showCorrection" class="correction-form">
                <textarea v-model="feedbackComment" class="prestige-input correction-input" maxlength="2000" placeholder="請指出需要修正的結論、證據、數字或分析方式…" />
                <button class="prestige-btn prestige-btn-solid" :disabled="feedbackSubmitting || feedbackComment.trim().length < 5" @click="sendFeedback('NeedsCorrection')">
                  {{ feedbackSubmitting ? '建立修訂中…' : '建立動態修訂' }}
                </button>
              </div>
              <div v-if="feedbackMessage" class="feedback-success">{{ feedbackMessage }}</div>
              <div v-if="feedbackError" class="prestige-error feedback-error">{{ feedbackError }}</div>
            </div>
          </div>
          <div v-else-if="activeTab === 'answer' && (runDetail.run.status === 'Failed' || runDetail.run.status === 'Cancelled')" class="prestige-error">
            研究流程未完成。<button v-if="investigationRun" class="prestige-btn" @click="router.push({ name: 'agent-run-detail', params: { id: investigationRun.id } })">查看錯誤詳情</button>
          </div>
          <div v-else-if="activeTab === 'answer'" class="prestige-empty panel-empty">研究仍在背景執行，完成後答案會自動顯示。</div>

          <!-- Tab: Citations -->
          <template v-if="activeTab === 'citations'">
            <div v-for="cite in runDetail.citations" :key="cite.id" class="list-item">
              <div class="tag-row">
                <span class="prestige-tag prestige-mono">#{{ cite.citationIndex }}</span>
                <span class="prestige-tag">{{ cite.sourceType }}</span>
                <span v-if="cite.sourceRole" class="prestige-tag">{{ cite.sourceRole }}</span>
              </div>
              <div class="item-title">{{ cite.title }}</div>
              <div class="item-quote">"{{ cite.quoteText }}"</div>
              <div class="item-meta">
                <span>Page <span class="prestige-mono">{{ cite.pageNumber ?? '-' }}</span></span>
                <span>Score: <span class="prestige-mono">{{ cite.relevanceScore.toFixed(3) }}</span></span>
              </div>
            </div>
            <div v-if="runDetail.citations.length === 0" class="prestige-empty panel-empty">暫無引用來源。</div>
          </template>

          <!-- Tab: Steps -->
          <template v-if="activeTab === 'steps'">
            <div v-for="step in runDetail.steps" :key="step.id" class="list-item">
              <div class="item-head">
                <span class="prestige-mono step-type">{{ step.stepType }}</span>
                <span v-if="step.durationMs !== null" class="prestige-mono item-meta-text">{{ step.durationMs }}ms</span>
              </div>
              <div v-if="step.errorMessage" class="item-error">{{ step.errorMessage }}</div>
              <div class="item-meta">
                <span>開始：<span class="prestige-mono">{{ formatDate(step.startedAtUtc) }}</span></span>
                <span>完成：<span class="prestige-mono">{{ formatDate(step.completedAtUtc) }}</span></span>
              </div>
              <pre v-if="step.outputJson" class="output-json prestige-mono">{{ step.outputJson }}</pre>
            </div>
            <div v-if="runDetail.steps.length === 0" class="prestige-empty panel-empty">暫無執行步驟。</div>
          </template>

          <!-- Tab: Candidates -->
          <template v-if="activeTab === 'candidates'">
            <div v-for="c in runDetail.candidates" :key="c.id" class="list-item">
              <div class="item-head">
                <div class="item-head-left">
                  <span class="item-title candidate-title">{{ c.title ?? 'Untitled' }}</span>
                  <span class="prestige-tag">{{ c.decision }}</span>
                </div>
                <span class="prestige-mono item-meta-text">Score: {{ c.relevanceScore.toFixed(3) }}</span>
              </div>
              <div v-if="c.discardReason" class="item-warn">{{ c.discardReason }}</div>
              <div v-if="c.contentPreview" class="item-preview">{{ c.contentPreview }}</div>
            </div>
            <div v-if="runDetail.candidates.length === 0" class="prestige-empty panel-empty">暫無候選文件。</div>
          </template>

          <!-- Tab: Agent Runs -->
          <template v-if="activeTab === 'agentRuns'">
            <div
              v-for="ar in agentRuns"
              :key="ar.id"
              class="list-item agent-run-row"
              @click="router.push({ name: 'agent-run-detail', params: { id: ar.id } })"
            >
              <div>
                <div class="tag-row">
                  <span class="prestige-tag prestige-mono">{{ ar.workflowType }}</span>
                  <span class="prestige-tag">{{ ar.agentType }}</span>
                  <span class="prestige-tag" :style="{ borderColor: statusColor(ar.status), color: statusColor(ar.status) }">{{ ar.status }}</span>
                </div>
                <div class="prestige-mono agent-run-id">{{ ar.id }}</div>
              </div>
              <span class="prestige-mono item-meta-text">{{ formatDate(ar.createdAtUtc) }}</span>
            </div>
            <div v-if="agentRuns.length === 0" class="prestige-empty panel-empty">暫無關聯的 Agent Runs。</div>
          </template>
        </div>
      </ScrollReveal>
    </div>
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.detail-section {
  padding-top: 32px;
}

.back-row {
  margin-bottom: 20px;
}

.skeleton-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.tag-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.question-title {
  margin: 0 0 14px;
  font-family: var(--serif);
  font-size: 22px;
  font-weight: 600;
  letter-spacing: 0.02em;
  line-height: 1.5;
}

.head-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 14px;
}

.meta-row {
  display: flex;
  gap: 20px;
  flex-wrap: wrap;
  font-size: 12px;
  color: var(--muted);
}

.tabs {
  display: flex;
  gap: 4px;
  margin: 28px 0 16px;
  border-bottom: 1px solid var(--gold-border-soft);
  overflow-x: auto;
}

.tab-btn {
  padding: 12px 18px;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  color: var(--muted);
  font-family: var(--sans);
  font-size: 13px;
  letter-spacing: 0.06em;
  cursor: pointer;
  white-space: nowrap;
  transition: color 0.2s ease, border-color 0.2s ease;
}

.tab-btn:hover {
  color: var(--ivory);
}

.tab-btn.active {
  color: var(--gold);
  border-bottom-color: var(--gold);
  font-weight: 600;
}

.answer-body {
  padding: 24px;
  white-space: pre-wrap;
  line-height: 1.9;
  font-size: 14px;
}

.feedback-panel { margin: 0 24px 24px; padding: 18px; border: 1px solid var(--gold-border-soft); background: rgba(201, 168, 106, 0.04); }
.feedback-title { margin-bottom: 12px; color: var(--ivory); font-size: 13px; }
.feedback-actions { display: flex; gap: 8px; flex-wrap: wrap; }
.correction-form { display: grid; gap: 10px; margin-top: 14px; }
.correction-input { min-height: 110px; padding: 12px; resize: vertical; }
.correction-form .prestige-btn { justify-self: start; }
.feedback-success { margin-top: 10px; color: #7fa387; font-size: 13px; }
.feedback-error { margin-top: 10px; }

.list-item {
  padding: 16px 24px;
  border-bottom: 1px solid var(--gold-border-soft);
}

.list-item:last-child {
  border-bottom: none;
}

.item-title {
  font-size: 14px;
  font-weight: 500;
  margin-bottom: 4px;
}

.candidate-title {
  margin-bottom: 0;
}

.item-quote {
  font-size: 13px;
  color: var(--muted);
  font-style: italic;
  line-height: 1.6;
}

.item-meta {
  display: flex;
  gap: 16px;
  margin-top: 8px;
  font-size: 12px;
  color: var(--muted);
}

.item-meta-text {
  font-size: 12px;
  color: var(--muted);
}

.item-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.item-head-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.step-type {
  font-size: 14px;
  font-weight: 600;
  color: var(--gold);
}

.item-error {
  color: var(--down);
  font-size: 13px;
  margin-bottom: 6px;
}

.item-warn {
  color: #d4a24e;
  font-size: 12px;
  margin-bottom: 4px;
}

.item-preview {
  font-size: 13px;
  color: var(--muted);
  line-height: 1.6;
}

.output-json {
  margin: 10px 0 0;
  padding: 12px;
  background: rgba(11, 18, 32, 0.6);
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  font-size: 12px;
  color: var(--muted);
  overflow-x: auto;
  white-space: pre-wrap;
}

.agent-run-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  cursor: pointer;
  transition: background 0.2s ease;
}

.agent-run-row:hover {
  background: rgba(201, 168, 106, 0.05);
}

.agent-run-id {
  font-size: 12px;
  color: var(--muted);
  margin-top: 4px;
}

.panel-empty {
  margin: 16px;
}
</style>
