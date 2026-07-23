<script setup lang="ts">
defineProps<{
  assessment: {
    decision: string
    reason: string
    evidenceGaps?: string[]
    confidence: string
    mode: string
  } | null
  requests: Array<{
    requestId: string
    capabilityId: string
    status: string
    reason: string
    reviewReason?: string | null
  }>
}>()

function statusLabel(status: string) {
  return status === 'Approved' ? '已核准' : status === 'Rejected' ? '未核准' : '等待 Planner 審核'
}
</script>

<template>
  <section v-if="assessment" class="prestige-panel capability-panel" data-testid="capability-request-status">
    <div>
      <span class="prestige-label">NODE AGENT · 能力提案</span>
      <h3>Web Search：{{ assessment.decision === 'Request' ? '建議使用' : '不需要' }}</h3>
      <p>{{ assessment.reason }}</p>
      <small>{{ assessment.mode }} · confidence {{ assessment.confidence }}</small>
    </div>
    <div v-if="requests.length" class="request-list">
      <article v-for="request in requests" :key="request.requestId">
        <code>{{ request.capabilityId }}</code>
        <strong :class="`request-${request.status.toLowerCase()}`">{{ statusLabel(request.status) }}</strong>
        <p v-if="request.reviewReason">Planner：{{ request.reviewReason }}</p>
      </article>
    </div>
    <div v-else class="not-needed">未建立 capability request，流程將使用本地證據繼續。</div>
  </section>
</template>

<style scoped>
.capability-panel { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; padding: 22px; margin-bottom: 16px; }
h3 { margin: 8px 0; color: #f5efe0; font-family: var(--serif); }
p { margin: 6px 0; color: #b8af9e; line-height: 1.5; }
small, .not-needed { color: #716a5d; }
.request-list { display: grid; gap: 10px; }
article { display: grid; gap: 7px; padding: 13px; border: 1px solid rgba(255,255,255,.08); }
article strong { color: #d4a24e; }
article .request-approved { color: #7fa387; }
article .request-rejected { color: #b05c5c; }
code { color: #c9a86a; }
@media (max-width: 720px) { .capability-panel { grid-template-columns: 1fr; } }
</style>
