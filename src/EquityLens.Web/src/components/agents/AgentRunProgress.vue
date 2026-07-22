<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import type { AgentRunNodeDto } from '../../services/agentRuns'

const props = defineProps<{
  nodes: AgentRunNodeDto[]
  workflowDefinition?: Record<string, unknown>
  runStatus: string
  startedAtUtc: string | null
  completedAtUtc?: string | null
}>()

const now = ref(Date.now())
let clock: ReturnType<typeof setInterval> | null = null
const completeStatuses = new Set(['Succeeded', 'Skipped'])
const stageLabels: Record<string, string> = {
  Input: '準備輸入', Load: '載入資料', Planning: '規劃', Retrieval: '資料檢索', Evidence: '證據處理',
  Decide: '決策', Routing: '路由', Analyze: '分析', Analysis: '分析', Validation: '驗證', Draft: '生成初稿',
  Generation: '生成內容', Critic: '品質審查', Finalize: '完成', Finalization: '完成', Processing: '處理中',
}

onMounted(() => { clock = setInterval(() => { now.value = Date.now() }, 1000) })
onUnmounted(() => { if (clock) clearInterval(clock) })

const orderedNodes = computed(() => {
  const definitionNodes = Array.isArray(props.workflowDefinition?.nodes)
    ? props.workflowDefinition.nodes as Array<{ id?: string }>
    : []
  const order = new Map(definitionNodes.map((node, index) => [node.id, index]))
  return [...props.nodes].sort((left, right) => {
    const leftOrder = order.get(left.nodeKey) ?? Number.MAX_SAFE_INTEGER
    const rightOrder = order.get(right.nodeKey) ?? Number.MAX_SAFE_INTEGER
    if (leftOrder !== rightOrder) return leftOrder - rightOrder
    return (left.startedAtUtc ?? left.nodeKey).localeCompare(right.startedAtUtc ?? right.nodeKey)
  })
})
const completedCount = computed(() => props.nodes.filter(node => completeStatuses.has(node.status)).length)
const progressPercent = computed(() => {
  if (props.nodes.length === 0) return 0
  const calculated = Math.round(completedCount.value / props.nodes.length * 100)
  return ['Succeeded', 'Failed', 'Cancelled'].includes(props.runStatus) ? calculated : Math.min(calculated, 95)
})
const currentNode = computed(() => orderedNodes.value.find(node => node.status === 'Running')
  ?? orderedNodes.value.find(node => node.status === 'Pending'))
const elapsed = computed(() => {
  if (!props.startedAtUtc) return '尚未開始'
  const end = props.completedAtUtc ? new Date(props.completedAtUtc).getTime() : now.value
  const seconds = Math.max(0, Math.floor((end - new Date(props.startedAtUtc).getTime()) / 1000))
  if (seconds < 60) return `${seconds} 秒`
  return `${Math.floor(seconds / 60)} 分 ${seconds % 60} 秒`
})
const currentStageLabel = computed(() => {
  if (currentNode.value) return stageLabel(currentNode.value.stage)
  if (props.runStatus === 'Succeeded') return '已完成'
  if (props.runStatus === 'Running') return '規劃下一階段'
  if (props.runStatus === 'Pending') return '等待啟動'
  return props.runStatus
})
const isExternalWait = computed(() => currentNode.value?.stage === 'Retrieval' || currentNode.value?.nodeType.includes('Web'))

function stageLabel(stage: string) { return stageLabels[stage] ?? stage }
function nodeStateLabel(status: string) {
  return status === 'Succeeded' ? '完成' : status === 'Skipped' ? '略過' : status === 'Running' ? '進行中' : status === 'Failed' ? '失敗' : '等待'
}
</script>

<template>
  <section class="prestige-panel progress-panel" aria-label="Workflow 執行進度">
    <div class="progress-head">
      <div>
        <span class="prestige-label">Workflow Progress</span>
        <h3>{{ currentStageLabel }}</h3>
        <p v-if="currentNode">{{ currentNode.displayName }}<span v-if="currentNode.description"> · {{ currentNode.description }}</span></p>
      </div>
      <div class="progress-stats">
        <strong>{{ progressPercent }}%</strong>
        <span>{{ completedCount }} / {{ nodes.length }} nodes</span>
        <span>已等待 {{ elapsed }}</span>
      </div>
    </div>
    <div class="progress-track"><span :style="{ width: `${progressPercent}%` }" /></div>
    <div v-if="isExternalWait" class="wait-hint">正在讀取外部證據，這個階段可能需要數分鐘，頁面會自動更新。</div>
    <ol class="node-progress-list">
      <li v-for="node in orderedNodes" :key="node.id" :class="`state-${node.status.toLowerCase()}`">
        <span class="node-marker" />
        <div class="node-copy">
          <div><span class="stage-chip">{{ stageLabel(node.stage) }}</span><strong>{{ node.displayName }}</strong></div>
          <small>{{ node.description ?? node.nodeType }}</small>
          <span v-if="node.errorMessage" class="node-error">{{ node.errorMessage }}</span>
        </div>
        <span class="node-state">{{ nodeStateLabel(node.status) }}</span>
      </li>
    </ol>
  </section>
</template>

<style scoped>
.progress-panel { padding: 22px; margin-bottom: 16px; }
.progress-head { display: flex; justify-content: space-between; gap: 20px; }
.progress-head h3 { margin: 7px 0; color: #f5efe0; font-family: var(--serif); font-size: 22px; }
.progress-head p { margin: 0; color: #9a917c; line-height: 1.5; }
.progress-stats { display: grid; justify-items: end; align-content: start; gap: 3px; color: #9a917c; font-size: 12px; white-space: nowrap; }
.progress-stats strong { color: #c9a86a; font-size: 24px; }
.progress-track { height: 4px; margin: 18px 0; overflow: hidden; background: rgba(255,255,255,.08); }
.progress-track span { display: block; height: 100%; background: #c9a86a; transition: width .35s ease; }
.wait-hint { margin-bottom: 16px; padding: 10px 12px; background: rgba(212,162,78,.08); color: #d4a24e; font-size: 12px; }
.node-progress-list { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }
.node-progress-list li { display: grid; grid-template-columns: 16px 1fr auto; gap: 12px; min-height: 54px; position: relative; color: #716a5d; }
.node-progress-list li:not(:last-child)::before { content: ''; position: absolute; left: 5px; top: 13px; bottom: -3px; width: 1px; background: rgba(255,255,255,.1); }
.node-marker { width: 9px; height: 9px; margin-top: 5px; z-index: 1; border: 1px solid #716a5d; border-radius: 50%; background: #0b1220; }
.node-copy { display: grid; align-content: start; gap: 4px; }
.node-copy > div { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.node-copy strong { color: #b8af9e; font-size: 13px; }
.node-copy small { color: #716a5d; line-height: 1.4; }
.stage-chip { color: #9a917c; font-size: 10px; letter-spacing: .05em; text-transform: uppercase; }
.node-state { font-size: 11px; padding-top: 3px; }
.state-succeeded .node-marker, .state-skipped .node-marker { border-color: #7fa387; background: #7fa387; }
.state-succeeded .node-copy strong { color: #d8d0bf; }
.state-running .node-marker { border-color: #d4a24e; background: #d4a24e; box-shadow: 0 0 0 4px rgba(212,162,78,.12); }
.state-running .node-copy strong, .state-running .node-state { color: #d4a24e; }
.state-failed .node-marker { border-color: #b05c5c; background: #b05c5c; }
.state-failed .node-copy strong, .state-failed .node-state, .node-error { color: #b05c5c; }
.node-error { font-size: 12px; }
@media (max-width: 640px) { .progress-head { flex-direction: column; } .progress-stats { justify-items: start; } .node-progress-list li { grid-template-columns: 16px 1fr; } .node-state { grid-column: 2; padding: 0 0 10px; } }
</style>
