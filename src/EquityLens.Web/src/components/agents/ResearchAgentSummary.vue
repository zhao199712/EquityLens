<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import type { AgentRunDetail } from '../../services/agentRuns'
import { renderMarkdown } from '../../utils/markdown'

const props = defineProps<{ detail: AgentRunDetail }>()
const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => { timer = setInterval(() => { now.value = Date.now() }, 1000) })
onUnmounted(() => clearInterval(timer))
const object = (value: unknown): Record<string, unknown> => value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
const text = (value: unknown) => typeof value === 'string' ? value : '未提供'
const labels: Record<string, string> = { Pending: '等待啟動', Running: '執行中', Succeeded: '執行完成', Failed: '執行失敗', Cancelled: '已取消', WaitingForFeedback: '等待回饋', WaitingForApproval: '等待批准', Answered: '已回答', InsufficientEvidence: '證據不足', CitationValidationFailed: '引用驗證失敗' }
const coarseQuality = computed(() => props.detail.nodes.some(node => ['qualityGate', 'reviseQualityGate'].includes(node.nodeKey)))
const phaseDefinitions = computed(() => [
  { label: '準備與意圖辨識', keys: ['validate', 'detectIntent'] },
  { label: '研究迴圈', keys: ['researchAgent'] },
  { label: '品質檢查', keys: coarseQuality.value ? ['qualityGate'] : ['outputGate', 'buildEvidencePacket', 'checkEvidence', 'critiqueAnswer', 'finalizeCriticReport', 'routeQuality'] },
  { label: '修訂與複查', keys: coarseQuality.value ? ['reviseAgent', 'reviseQualityGate'] : ['reviseAgent', 'reviseOutputGate', 'reviseEvidencePacket', 'reviseCheckEvidence', 'reviseCritiqueAnswer', 'reviseCriticReport'] },
  { label: '完成', keys: ['finalizeResearch'] },
])
const phases = computed(() => phaseDefinitions.value.map(phase => {
  const nodes = props.detail.nodes.filter(node => phase.keys.includes(node.nodeKey))
  const statuses = nodes.map(node => node.status)
  let state = '等待'
  if (!nodes.length) state = '未提供'
  else if (statuses.includes('Failed')) state = '失敗'
  else if (statuses.includes('Cancelled')) state = '已取消'
  else if (statuses.includes('WaitingForApproval') || statuses.includes('WaitingForFeedback')) state = '等待回饋'
  else if (statuses.includes('Running') || statuses.includes('Ready') || statuses.includes('Queued')) state = '進行中'
  else if (nodes.length === phase.keys.length && statuses.every(status => status === 'Skipped')) state = phase.keys[0] === 'reviseAgent' ? '不需要修訂' : '略過'
  else if (nodes.length === phase.keys.length && statuses.every(status => status === 'Succeeded' || status === 'Skipped')) state = '完成'
  else if (['Failed', 'Cancelled'].includes(props.detail.run.status)) state = '未執行完成'
  else if (statuses.includes('Succeeded')) state = '進行中'
  return { label: phase.label, state }
}))
const finalized = computed(() => props.detail.run.status === 'Succeeded' && props.detail.nodes.some(n => n.nodeKey === 'finalizeResearch' && n.status === 'Succeeded'))
const output = computed(() => finalized.value ? props.detail.outputJson : null)
const answer = computed(() => typeof output.value?.answer === 'string' ? output.value.answer : null)
const research = computed(() => object(props.detail.blackboardJson.researchRun))
const citations = computed(() => finalized.value && Array.isArray(research.value.citations) ? research.value.citations.map(object) : [])
const researchId = computed(() => object(research.value.run).id)
const gate = computed(() => object(output.value?.outputGate))
const critic = computed(() => object(output.value?.criticReview))
const turns = computed(() => [...new Map((props.detail.llmTurns ?? []).map(turn => [turn.id, turn])).values()].sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc) || a.turn - b.turn))
const hasUsage = computed(() => turns.value.length > 0)
const tokens = computed(() => turns.value.reduce((sum, turn) => sum + turn.promptTokens + turn.completionTokens, 0))
const cost = computed(() => turns.value.reduce((sum, turn) => sum + turn.estimatedCostUsd, 0))
const loop = computed(() => object(props.detail.blackboardJson.researchAgent))
const stops = computed(() => props.detail.events.filter(event => event.eventType === 'AgentLoopStopped'))
const elapsed = computed(() => {
  if (!props.detail.run.startedAtUtc) return '尚未開始'
  const end = props.detail.run.completedAtUtc ? Date.parse(props.detail.run.completedAtUtc) : now.value
  const seconds = Math.max(0, Math.floor((end - Date.parse(props.detail.run.startedAtUtc)) / 1000))
  return Number.isFinite(seconds) ? `${seconds} 秒` : '未提供'
})
const artifactLabel = computed(() => output.value?.policyResponse === true ? '系統風險說明' : labels[String(output.value?.status)] ?? text(output.value?.status))
</script>

<template>
  <section class="prestige-panel summary" aria-label="AgentLoop 研究摘要">
    <header><div><span class="prestige-label">Research Agent · AgentLoop</span><h3>{{ labels[detail.run.status] ?? detail.run.status }}</h3></div><span>耗時 {{ elapsed }}</span></header>
    <ol class="phases"><li v-for="phase in phases" :key="phase.label"><strong>{{ phase.label }}</strong><span>{{ phase.state }}</span></li></ol>
    <p v-if="detail.run.errorMessage" class="error">{{ detail.run.errorMessage }}</p>
    <section v-if="answer" class="answer">
      <h3>研究結果 · {{ artifactLabel }}</h3>
      <div v-html="renderMarkdown(answer)" />
      <h4>引用來源</h4>
      <p v-if="!citations.length">此結果沒有引用來源。</p>
      <details v-for="(citation, index) in citations" :key="index"><summary>[{{ citation.citationIndex ?? index + 1 }}] {{ citation.title }} · 第 {{ citation.pageNumber ?? '未提供' }} 頁</summary><blockquote>{{ citation.quoteText }}</blockquote></details>
      <RouterLink v-if="typeof researchId === 'string'" :to="{ name: 'research-run-detail', params: { id: researchId } }">查看完整研究資料 →</RouterLink>
      <h4>品質檢查</h4>
      <p v-if="output?.errorCode === 'quality_gate_rejected'" class="error">品質檢查拒絕：模型答案未通過，已改為拒答或系統風險說明。</p>
      <ul v-if="Array.isArray(output?.rejectedNumbers)"><li v-for="number in output.rejectedNumbers" :key="String(number)">未能追溯：{{ number }}</li></ul>
      <p>OutputGate：{{ gate.passed === true ? '通過' : gate.passed === false ? '未通過' : '未提供' }} · Critic：{{ critic.recommendedNextAction === 'AcceptAnswer' ? '接受答案' : text(critic.recommendedNextAction) }}</p>
      <ul v-if="Array.isArray(gate.blockingIssues)"><li v-for="issue in gate.blockingIssues" :key="String(issue)">{{ issue }}</li></ul>
      <ul v-if="Array.isArray(gate.warnings)"><li v-for="warning in gate.warnings" :key="String(warning)">{{ warning }}</li></ul>
    </section>
    <p v-else>{{ ['Failed', 'Cancelled'].includes(detail.run.status) ? '本次執行未產出已確認的最終答案。' : detail.run.status === 'Succeeded' ? '此紀錄缺少可確認的最終結果，請查看技術明細。' : '研究進行中，完成品質檢查後顯示最終答案。' }}</p>
    <h4>執行統計</h4>
    <dl class="stats">
      <div><dt>研究／修訂 LLM 呼叫（含重試）</dt><dd>{{ hasUsage ? turns.length : '未提供' }}</dd></div>
      <div><dt>已記錄工具呼叫</dt><dd>{{ detail.toolCalls.length }}</dd></div>
      <div><dt>最後一輪證據數</dt><dd>{{ loop.ledgerSize ?? '未提供' }}</dd></div>
      <div><dt>已記錄 LLM tokens</dt><dd>{{ hasUsage ? tokens : '未提供' }}</dd></div>
      <div><dt>已記錄 LLM 估算費用</dt><dd>{{ hasUsage ? `US$${cost.toFixed(6)}` : '未提供' }}</dd></div>
    </dl>
    <p class="muted">費用僅涵蓋研究／修訂迴圈的已記錄 LLM 呼叫，不含 Router、Critic、embedding、reranker 與其他服務。</p>
    <p v-for="stop in stops" :key="stop.id">迴圈停止：{{ stop.message ?? '未提供' }}</p>
    <p v-if="!stops.length">最後一輪停止原因：{{ text(loop.stopReason) }}</p>
    <details><summary>LLM 呼叫明細（{{ turns.length }} 筆）</summary>
      <p v-if="!turns.length">此紀錄未提供 LLM 呼叫資料。</p>
      <article v-for="turn in turns" :key="turn.id" class="turn">
        <strong>{{ turn.mode }} · attempt {{ turn.executionAttempt }} · turn {{ turn.turn }}</strong>
        <p>{{ detail.nodes.find(node => node.id === turn.agentRunNodeId)?.nodeKey ?? turn.agentRunNodeId }} · {{ turn.model }} · {{ turn.action }} · {{ turn.latencyMs }} ms</p>
        <p v-if="turn.errorMessage" class="error">{{ turn.errorMessage }}</p>
      </article>
    </details>
  </section>
</template>

<style scoped>
.summary { padding: 24px; margin-bottom: 16px; color: #d8d0bf; overflow-wrap: anywhere; }
header { display:flex; justify-content:space-between; gap:16px; align-items:center; }
h3 { color:#f5efe0; } h4 { margin-top:24px; color:#c9a86a; }
.phases { display:grid; grid-template-columns:repeat(5,minmax(0,1fr)); padding:0; list-style:none; gap:12px; margin:24px 0; }
.phases li { border-top:2px solid #c9a86a; padding-top:12px; display:grid; gap:8px; }
.phases span,.muted,dt { color:#9a917c; font-size:12px; line-height:1.6; }
.stats { display:flex; flex-wrap:wrap; gap:24px; } dd { margin:8px 0; }
.answer { border-top:1px solid #ffffff18; line-height:1.8; } blockquote { white-space:pre-wrap; margin:12px; }
details { margin:12px 0; } summary { cursor:pointer; color:#c9a86a; } .turn { border-bottom:1px solid #ffffff18; padding:12px 0; } .error { color:#e09b9b; }
@media(max-width:640px) { .phases { grid-template-columns:1fr; } header { align-items:flex-start; flex-direction:column; } .summary { padding:16px; } }
</style>
