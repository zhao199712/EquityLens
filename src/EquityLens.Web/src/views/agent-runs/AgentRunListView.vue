<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import { listAgentRuns, type AgentRunListItem } from '../../services/agentRuns'

const router = useRouter()
const search = ref('')
const runs = ref<AgentRunListItem[]>([])
const loading = ref(false)
const error = ref('')
const filterWorkflow = ref('')
const filterStatus = ref('')

const filteredRuns = computed(() => {
  const q = search.value.trim().toLowerCase()
  return runs.value.filter((run) =>
    (!q || run.workflowType.toLowerCase().includes(q) || run.agentType.toLowerCase().includes(q) || run.id.toLowerCase().includes(q)) &&
    (!filterWorkflow.value || run.workflowType === filterWorkflow.value) &&
    (!filterStatus.value || run.status === filterStatus.value),
  )
})

onMounted(loadRuns)

async function loadRuns() {
  loading.value = true
  error.value = ''
  try {
    runs.value = await listAgentRuns({ limit: 50 })
  } catch {
    error.value = '無法載入 Agent Run 列表，請稍後再試。'
  } finally {
    loading.value = false
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
    default: return '#666666'
  }
}
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px; padding-bottom: 80px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">Agent Runs</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">WORKFLOW EXECUTION HISTORY</span>
        </div>
        <button class="kimi-btn-dark" @click="loadRuns">重新整理</button>
      </div>

      <div style="display: flex; gap: 12px; margin-bottom: 24px; flex-wrap: wrap">
        <input v-model="search" placeholder="搜尋 workflow / agent / ID..." class="kimi-input-dark" style="width: 300px" />
        <select v-model="filterWorkflow" class="kimi-input-dark" style="width: 180px">
          <option value="">全部 Workflow</option>
          <option value="CriticReview">CriticReview</option>
          <option value="DraftRevision">DraftRevision</option>
        </select>
        <select v-model="filterStatus" class="kimi-input-dark" style="width: 160px">
          <option value="">全部狀態</option>
          <option value="Pending">Pending</option>
          <option value="Running">Running</option>
          <option value="Succeeded">Succeeded</option>
          <option value="Failed">Failed</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </div>

      <div v-if="loading" style="color: #666666; font-size: 14px">載入中...</div>
      <div v-else-if="error" style="color: #f87171; font-size: 14px; margin-bottom: 24px">{{ error }}</div>

      <div v-else style="display: flex; flex-direction: column; gap: 12px">
        <ScrollReveal v-for="(run, i) in filteredRuns" :key="run.id" :delay="i * 0.05">
          <div class="kimi-panel-dark" style="cursor: pointer; transition: all 0.2s ease; padding: 20px" @click="router.push({ name: 'agent-run-detail', params: { id: run.id } })">
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px">
              <div style="display: flex; align-items: center; gap: 8px">
                <span class="kimi-tag-dark" style="font-family: monospace; font-size: 11px">{{ run.workflowType }}</span>
                <span class="kimi-tag-dark">{{ run.agentType }}</span>
              </div>
              <span class="kimi-tag-dark" :style="{ borderColor: statusColor(run.status), color: statusColor(run.status) }">{{ run.status }}</span>
            </div>

            <div style="font-family: monospace; font-size: 12px; color: #999999; margin-bottom: 12px">{{ run.id }}</div>
            <div v-if="run.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 12px">{{ run.errorMessage }}</div>

            <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 16px">
              <div>
                <div style="font-size: 11px; color: #666666; margin-bottom: 2px">Created</div>
                <div style="font-size: 13px; color: #FFFFFF">{{ formatDate(run.createdAtUtc) }}</div>
              </div>
              <div>
                <div style="font-size: 11px; color: #666666; margin-bottom: 2px">Started</div>
                <div style="font-size: 13px; color: #FFFFFF">{{ formatDate(run.startedAtUtc) }}</div>
              </div>
              <div>
                <div style="font-size: 11px; color: #666666; margin-bottom: 2px">Completed</div>
                <div style="font-size: 13px; color: #FFFFFF">{{ formatDate(run.completedAtUtc) }}</div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <div v-if="filteredRuns.length === 0" style="color: #666666; margin-top: 24px; text-align: center; padding: 40px">沒有符合條件的 Agent Run。</div>
      </div>
    </div>
  </div>
</template>
