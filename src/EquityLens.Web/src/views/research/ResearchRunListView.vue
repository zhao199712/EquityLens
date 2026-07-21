<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import { createResearchInvestigation, listResearchRuns, type ResearchRunSummary } from '../../services/research'

const router = useRouter()
const runs = ref<ResearchRunSummary[]>([])
const loading = ref(false)
const error = ref('')
const search = ref('')
const askLoading = ref(false)
const askError = ref('')
const showAskForm = ref(false)

const ticker = ref('')
const question = ref('')

const filteredRuns = computed(() => {
  const q = search.value.trim().toLowerCase()
  return runs.value.filter((r) =>
    !q || r.ticker.toLowerCase().includes(q) || r.question.toLowerCase().includes(q) || r.id.toLowerCase().includes(q),
  )
})

onMounted(loadRuns)

async function loadRuns() {
  loading.value = true
  error.value = ''
  try {
    runs.value = await listResearchRuns({ limit: 50 })
  } catch {
    error.value = '無法載入研究結果列表。'
  } finally {
    loading.value = false
  }
}

async function handleAsk() {
  if (!ticker.value.trim() || !question.value.trim()) return
  askLoading.value = true
  askError.value = ''
  try {
    const result = await createResearchInvestigation({ ticker: ticker.value.trim(), question: question.value.trim(), sourcePolicy: 'Auto' })
    router.push({ name: 'agent-run-detail', params: { id: result.agentRunId } })
  } catch {
    askError.value = '研究請求失敗，請稍後再試。'
  } finally {
    askLoading.value = false
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('zh-TW')
}

function statusColor(status: string) {
  switch (status) {
    case 'Answered': return '#7fa387'
    case 'Failed': return '#b05c5c'
    case 'Running': return '#d4a24e'
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
          <span class="prestige-label">Research Runs</span>
          <h2 class="prestige-section-title">Research</h2>
        </div>
        <div class="head-actions">
          <button class="prestige-btn" @click="showAskForm = !showAskForm">{{ showAskForm ? '關閉' : '+ 提出研究問題' }}</button>
          <button class="prestige-btn" @click="loadRuns">重新整理</button>
        </div>
      </div>

      <!-- Ask Form -->
      <ScrollReveal v-if="showAskForm">
        <div class="prestige-panel prestige-panel-pad ask-panel">
          <h3 class="ask-title">提出研究問題</h3>
          <div class="ask-grid">
            <input v-model="ticker" placeholder="股票代號（如 2330）" class="prestige-input ask-ticker" />
            <input v-model="question" placeholder="研究問題" class="prestige-input ask-question" @keyup.enter="handleAsk" />
          </div>
          <div class="ask-actions">
            <button class="prestige-btn prestige-btn-solid" :disabled="askLoading || !ticker.trim() || !question.trim()" @click="handleAsk">
              {{ askLoading ? '查詢中...' : '送出研究問題' }}
            </button>
            <span v-if="askError" class="ask-error">{{ askError }}</span>
          </div>
        </div>
      </ScrollReveal>

      <!-- Search -->
      <div class="search-row">
        <input v-model="search" placeholder="搜尋 ticker / 問題 / ID..." class="prestige-input search-input" />
      </div>

      <!-- Loading skeleton -->
      <div v-if="loading" class="skeleton-stack">
        <div v-for="i in 3" :key="i" class="prestige-skeleton" />
      </div>

      <!-- Error -->
      <div v-else-if="error" class="prestige-error">{{ error }}</div>

      <!-- Runs Table -->
      <ScrollReveal v-else-if="filteredRuns.length > 0">
        <div class="prestige-panel table-panel">
          <table class="prestige-table">
            <thead>
              <tr>
                <th>Ticker</th>
                <th>問題</th>
                <th>檢索模式</th>
                <th>狀態</th>
                <th>引用</th>
                <th>延遲</th>
                <th>建立時間</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="run in filteredRuns"
                :key="run.id"
                class="run-row"
                @click="router.push({ name: 'research-run-detail', params: { id: run.id } })"
              >
                <td><span class="prestige-tag prestige-mono">{{ run.ticker }}</span></td>
                <td class="question-cell">{{ run.question }}</td>
                <td><span class="prestige-tag">{{ run.retrievalMode }}</span></td>
                <td>
                  <span class="prestige-tag" :style="{ borderColor: statusColor(run.status), color: statusColor(run.status) }">{{ run.status }}</span>
                </td>
                <td class="prestige-mono">{{ run.citationCount }}</td>
                <td class="prestige-mono">{{ run.latencyMs }}ms</td>
                <td class="prestige-mono date-cell">{{ formatDate(run.createdAtUtc) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </ScrollReveal>

      <div v-else class="prestige-empty">沒有研究結果。</div>
    </div>
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.head-actions {
  display: flex;
  gap: 8px;
}

.ask-panel {
  margin-bottom: 24px;
}

.ask-title {
  margin: 0 0 14px;
  font-family: var(--serif);
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.ask-grid {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 14px;
}

.ask-ticker {
  width: 200px;
  flex: none;
}

.ask-question {
  flex: 1;
  min-width: 280px;
}

.ask-actions {
  display: flex;
  gap: 12px;
  align-items: center;
}

.ask-error {
  color: var(--down);
  font-size: 13px;
}

.search-row {
  margin-bottom: 20px;
}

.search-input {
  max-width: 400px;
}

.skeleton-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.table-panel {
  overflow-x: auto;
}

.run-row {
  cursor: pointer;
}

.question-cell {
  font-weight: 500;
}

.date-cell {
  color: var(--muted);
  white-space: nowrap;
}
</style>
