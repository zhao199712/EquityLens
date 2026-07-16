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
  <div class="kimi-page-vscode">
    <div class="kimi-content">
      <ScrollReveal>
        <div class="kimi-section" style="margin-top: 60px">
          <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light); display: flex; align-items: center; justify-content: space-between">
            <div>
              <h2 style="margin: 0; font-size: 20px; font-weight: 600">Agent Runs</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">WORKFLOW EXECUTION HISTORY</span>
            </div>
            <button class="kimi-btn" @click="loadRuns">重新整理</button>
          </div>

          <!-- Filters -->
          <div style="padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light); display: flex; gap: 12px; flex-wrap: wrap">
            <input v-model="search" placeholder="搜尋 workflow / agent / ID..." style="padding: 8px 12px; border: 1px solid var(--kimi-border-light); background: transparent; font-size: 13px; font-family: var(--kimi-font-body); width: 300px" />
            <select v-model="filterWorkflow" style="padding: 8px 12px; border: 1px solid var(--kimi-border-light); background: transparent; font-size: 13px; font-family: var(--kimi-font-body); width: 180px">
              <option value="">全部 Workflow</option>
              <option value="CriticReview">CriticReview</option>
              <option value="DraftRevision">DraftRevision</option>
            </select>
            <select v-model="filterStatus" style="padding: 8px 12px; border: 1px solid var(--kimi-border-light); background: transparent; font-size: 13px; font-family: var(--kimi-font-body); width: 160px">
              <option value="">全部狀態</option>
              <option value="Pending">Pending</option>
              <option value="Running">Running</option>
              <option value="Succeeded">Succeeded</option>
              <option value="Failed">Failed</option>
              <option value="Cancelled">Cancelled</option>
            </select>
          </div>

          <!-- Loading / Error -->
          <div v-if="loading" style="padding: 40px 20px; color: var(--kimi-muted); font-size: 14px; text-align: center">載入中...</div>
          <div v-else-if="error" style="padding: 20px; color: #f87171; font-size: 14px">{{ error }}</div>

          <!-- Runs List -->
          <template v-else>
            <div v-for="(run, i) in filteredRuns" :key="run.id">
              <ScrollReveal :delay="i * 0.03">
                <div style="display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light); cursor: pointer; transition: background 0.2s" @click="router.push({ name: 'agent-run-detail', params: { id: run.id } })">
                  <div style="flex: 1; min-width: 0">
                    <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 4px">
                      <span class="kimi-tag" style="font-family: var(--kimi-font-mono); font-size: 11px">{{ run.workflowType }}</span>
                      <span class="kimi-tag">{{ run.agentType }}</span>
                      <span class="kimi-tag" :style="{ borderColor: statusColor(run.status), color: statusColor(run.status) }">{{ run.status }}</span>
                    </div>
                    <div v-if="run.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 4px">{{ run.errorMessage }}</div>
                    <div style="display: flex; gap: 20px; font-size: 12px; color: var(--kimi-muted)">
                      <span>Created: {{ formatDate(run.createdAtUtc) }}</span>
                      <span>Started: {{ formatDate(run.startedAtUtc) }}</span>
                      <span>Completed: {{ formatDate(run.completedAtUtc) }}</span>
                    </div>
                  </div>
                  <span style="color: var(--kimi-muted); font-size: 18px; margin-left: 12px">&rsaquo;</span>
                </div>
              </ScrollReveal>
            </div>

            <div v-if="filteredRuns.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center; font-size: 14px">沒有符合條件的 Agent Run。</div>
          </template>
        </div>
      </ScrollReveal>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">AGENT RUNS</span>
      <span>數據僅供參考</span>
    </footer>
  </div>
</template>
