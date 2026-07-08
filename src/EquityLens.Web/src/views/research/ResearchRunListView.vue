<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import { askResearch, listResearchRuns, type ResearchRunSummary, type ResearchAskResponse } from '../../services/research'

const router = useRouter()
const runs = ref<ResearchRunSummary[]>([])
const loading = ref(false)
const error = ref('')
const search = ref('')
const askLoading = ref(false)
const askError = ref('')
const askResult = ref<ResearchAskResponse | null>(null)
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
  askResult.value = null
  try {
    const result = await askResearch({ ticker: ticker.value.trim(), question: question.value.trim() })
    askResult.value = result
    await loadRuns()
    if (result.researchRunId) {
      router.push({ name: 'research-run-detail', params: { id: result.researchRunId } })
    }
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
    case 'Answered': return '#34d399'
    case 'Failed': return '#f87171'
    case 'Running': return '#60a5fa'
    default: return '#666666'
  }
}
</script>

<template>
  <div class="kimi-page-light">
    <div class="kimi-content">
      <ScrollReveal>
        <div class="kimi-section" style="margin-top: 60px">
          <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light); display: flex; align-items: center; justify-content: space-between">
            <div>
              <h2 style="margin: 0; font-size: 20px; font-weight: 600">Research</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">RESEARCH RUNS</span>
            </div>
            <div style="display: flex; gap: 8px">
              <button class="kimi-btn" @click="showAskForm = !showAskForm">{{ showAskForm ? '關閉' : '+ 提出研究問題' }}</button>
              <button class="kimi-btn" @click="loadRuns">重新整理</button>
            </div>
          </div>

          <!-- Ask Form -->
          <div v-if="showAskForm" style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <h3 style="margin: 0 0 12px 0; font-size: 16px; font-weight: 600">提出研究問題</h3>
            <div style="display: flex; gap: 12px; flex-wrap: wrap; margin-bottom: 12px">
              <input v-model="ticker" placeholder="股票代號（如 2330）" style="padding: 8px 12px; border: 1px solid var(--kimi-border-light); background: transparent; font-size: 13px; width: 200px; font-family: var(--kimi-font-body)" />
              <input v-model="question" placeholder="研究問題" style="flex: 1; min-width: 300px; padding: 8px 12px; border: 1px solid var(--kimi-border-light); background: transparent; font-size: 13px; font-family: var(--kimi-font-body)" @keyup.enter="handleAsk" />
            </div>
            <div style="display: flex; gap: 8px; align-items: center">
              <button class="kimi-btn kimi-btn-solid" :disabled="askLoading || !ticker.trim() || !question.trim()" @click="handleAsk">
                {{ askLoading ? '查詢中...' : '送出研究問題' }}
              </button>
              <span v-if="askError" style="color: #f87171; font-size: 13px">{{ askError }}</span>
            </div>
          </div>

          <!-- Search -->
          <div style="padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <input v-model="search" placeholder="搜尋 ticker / 問題 / ID..." style="width: 100%; max-width: 400px; padding: 8px 12px; border: 1px solid var(--kimi-border-light); background: transparent; font-size: 13px; font-family: var(--kimi-font-body)" />
          </div>

          <!-- Loading / Error -->
          <div v-if="loading" style="padding: 40px 20px; color: var(--kimi-muted); font-size: 14px; text-align: center">載入中...</div>
          <div v-else-if="error" style="padding: 20px; color: #f87171; font-size: 14px">{{ error }}</div>

          <!-- Runs List -->
          <template v-else>
            <div v-for="(run, i) in filteredRuns" :key="run.id">
              <ScrollReveal :delay="i * 0.03">
                <div style="display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light); cursor: pointer; transition: background 0.2s" @click="router.push({ name: 'research-run-detail', params: { id: run.id } })">
                  <div style="flex: 1; min-width: 0">
                    <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 4px">
                      <span class="kimi-tag" style="font-family: var(--kimi-font-mono); font-size: 11px">{{ run.ticker }}</span>
                      <span class="kimi-tag">{{ run.retrievalMode }}</span>
                      <span class="kimi-tag" :style="{ borderColor: statusColor(run.status), color: statusColor(run.status) }">{{ run.status }}</span>
                    </div>
                    <div style="font-size: 14px; font-weight: 500; margin-bottom: 4px">{{ run.question }}</div>
                    <div style="display: flex; gap: 20px; font-size: 12px; color: var(--kimi-muted)">
                      <span>Citations: {{ run.citationCount }}</span>
                      <span>Latency: {{ run.latencyMs }}ms</span>
                      <span>{{ formatDate(run.createdAtUtc) }}</span>
                    </div>
                  </div>
                  <span style="color: var(--kimi-muted); font-size: 18px; margin-left: 12px">&rsaquo;</span>
                </div>
              </ScrollReveal>
            </div>

            <div v-if="filteredRuns.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center; font-size: 14px">沒有研究結果。</div>
          </template>
        </div>
      </ScrollReveal>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">RESEARCH</span>
      <span>數據僅供參考</span>
    </footer>
  </div>
</template>
