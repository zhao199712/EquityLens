<script setup lang="ts">
import { computed } from 'vue'
import type { AgentRunDetail } from '../../services/agentRuns'
import { renderMarkdown } from '../../utils/markdown'
const props = defineProps<{ detail: AgentRunDetail }>()
const loops = computed(() => props.detail.nodes.filter(n => ['PortfolioAgentLoop', 'ResearchAgentLoop'].includes(n.nodeType)))
const turns = computed(() => props.detail.llmTurns?.filter(t => loops.value.some(n => n.id === t.agentRunNodeId)))
const calls = computed(() => props.detail.toolCalls.filter(t => loops.value.some(n => n.id === t.agentRunNodeId)))
const gate = computed(() => props.detail.blackboardJson.portfolioQualityGate as { passed?: boolean; blockingIssues?: string[]; rejectedNumbers?: string[] } | undefined)
const waiting = computed(() => ['WaitingForFeedback', 'WaitingForApproval'].includes(props.detail.run.status))
const draft = computed(() => props.detail.blackboardJson.portfolioDiagnosisDraft as { summary?: string; interpretation?: string } | undefined)
const reasons: Record<string, string> = {
  UntraceableNumbers: '部分數字無法對應引用的工具結果', MixedRiskBasis: '草稿混用了不同方法或快照的風險數字',
  OptimizationScenarioRequired: '尚未完成優化情境比較', OptimizationDataUnavailable: '共同歷史資料不足',
  InsufficientEvidence: '證據不足，未產生可採用的模型建議', PartialScenarioFailure: '部分情境失敗，僅呈現成功結果',
  UnknownToolEvidence: '引用了不存在的工具證據', GuaranteedProfitClaim: '包含無法支持的獲利保證',
}
</script>
<template>
  <section v-if="loops.length" class="prestige-panel prestige-panel-pad" data-testid="agentloop-summary">
    <h3>AgentLoop 執行紀錄</h3>
    <p>{{ waiting ? '已暫停，等待人工審閱' : detail.run.status === 'Succeeded' ? '執行已完成' : detail.run.status }} · LLM {{ turns == null ? '紀錄未提供' : `${turns.length} 輪` }} · 工具 {{ calls.length }} 次（拒絕 {{ calls.filter(c => c.status === 'Denied').length }} 次）</p>
    <p v-for="node in loops" :key="node.id">{{ node.displayName }}：{{ node.outputJson?.stopReason ?? node.status }}</p>
    <div v-if="gate" data-testid="portfolio-quality">
      <strong>{{ gate.passed ? '答案品質檢查通過' : '答案已降級為已驗證的工具摘要' }}</strong>
      <p>執行完成與答案通過品質檢查是不同狀態。</p>
      <ul v-if="gate.blockingIssues?.length"><li v-for="code in gate.blockingIssues" :key="code">{{ reasons[code] ?? code }}（{{ code }}）</li></ul>
      <p v-if="gate.rejectedNumbers?.length">未通過的數字：{{ gate.rejectedNumbers.join('、') }}</p>
    </div>
    <div v-if="waiting && draft" data-testid="approval-draft">
      <h4>待審閱內容</h4>
      <p>核准只會發布診斷報告，不會調整持倉。</p>
      <div v-html="renderMarkdown(draft.summary ?? '')" />
      <div v-if="draft.interpretation && draft.interpretation !== draft.summary" v-html="renderMarkdown(draft.interpretation)" />
    </div>
  </section>
</template>
<style scoped>
section { margin: 16px 0; color: var(--ivory, #eee); } p, li { line-height: 1.7; color: var(--muted, #aaa); }
</style>
