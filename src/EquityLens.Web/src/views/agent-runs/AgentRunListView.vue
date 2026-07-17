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
    case 'Succeeded': return '#7fa387'
    case 'Failed': return '#b05c5c'
    case 'Running': return '#d4a24e'
    case 'Cancelled': return '#9a917c'
    case 'Pending': return '#9a917c'
    case 'WaitingForFeedback': return '#c9a86a'
    default: return '#9a917c'
  }
}
</script>

<template>
  <div class="prestige-page">
    <div class="prestige-section">
      <!-- Header -->
      <div class="prestige-section-head">
        <div>
          <span class="prestige-label">Workflow Execution History</span>
          <h2 class="prestige-section-title">Agent Runs</h2>
        </div>
        <button class="prestige-btn" @click="loadRuns">重新整理</button>
      </div>

      <!-- Filters -->
      <div class="filter-row">
        <input v-model="search" placeholder="搜尋 workflow / agent / ID..." class="prestige-input filter-search" />
        <select v-model="filterWorkflow" class="prestige-input filter-select">
          <option value="">全部 Workflow</option>
          <option value="CriticReview">CriticReview</option>
          <option value="DraftRevision">DraftRevision</option>
        </select>
        <select v-model="filterStatus" class="prestige-input filter-select">
          <option value="">全部狀態</option>
          <option value="Pending">Pending</option>
          <option value="Running">Running</option>
          <option value="Succeeded">Succeeded</option>
          <option value="Failed">Failed</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </div>

      <!-- Loading -->
      <div v-if="loading" class="run-list">
        <div v-for="i in 4" :key="i" class="prestige-skeleton list-skeleton" />
      </div>

      <!-- Error -->
      <div v-else-if="error" class="prestige-error">{{ error }}</div>

      <!-- Runs List -->
      <template v-else>
        <div class="run-list">
          <ScrollReveal v-for="(run, i) in filteredRuns" :key="run.id" :delay="i * 0.03">
            <div
              class="prestige-panel prestige-panel-pad run-row"
              @click="router.push({ name: 'agent-run-detail', params: { id: run.id } })"
            >
              <div class="run-main">
                <div class="run-tags">
                  <span class="prestige-tag prestige-mono">{{ run.workflowType }}</span>
                  <span class="prestige-tag">{{ run.agentType }}</span>
                  <span
                    class="prestige-tag"
                    :style="{ borderColor: statusColor(run.status), color: statusColor(run.status) }"
                  >{{ run.status }}</span>
                </div>
                <div v-if="run.errorMessage" class="run-error">{{ run.errorMessage }}</div>
                <div class="run-dates prestige-mono">
                  <span>Created: {{ formatDate(run.createdAtUtc) }}</span>
                  <span>Started: {{ formatDate(run.startedAtUtc) }}</span>
                  <span>Completed: {{ formatDate(run.completedAtUtc) }}</span>
                </div>
              </div>
              <span class="run-chevron">&rsaquo;</span>
            </div>
          </ScrollReveal>
        </div>

        <div v-if="filteredRuns.length === 0" class="prestige-empty">沒有符合條件的 Agent Run。</div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.filter-row {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 20px;
}

.filter-search {
  width: 300px;
}

.filter-select {
  width: 180px;
}

.run-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.list-skeleton {
  min-height: 84px;
}

.run-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: pointer;
  transition: transform 0.25s ease, border-color 0.3s ease;
}

.run-row:hover {
  transform: translateY(-2px);
  border-color: rgba(201, 168, 106, 0.45);
}

.run-main {
  flex: 1;
  min-width: 0;
}

.run-tags {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
  flex-wrap: wrap;
}

.run-error {
  color: var(--down);
  font-size: 13px;
  margin-bottom: 6px;
}

.run-dates {
  display: flex;
  gap: 20px;
  font-size: 12px;
  color: var(--muted);
  flex-wrap: wrap;
}

.run-chevron {
  color: var(--gold);
  font-size: 18px;
  margin-left: 12px;
}

@media (max-width: 720px) {
  .filter-search,
  .filter-select {
    width: 100%;
  }
}
</style>
