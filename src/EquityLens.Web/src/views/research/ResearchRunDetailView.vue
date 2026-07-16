<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import { getResearchRun, type ResearchRunDetail } from '../../services/research'
import { createCriticReview } from '../../services/agentRuns'
import { listAgentRuns, type AgentRunListItem } from '../../services/agentRuns'

const route = useRoute()
const router = useRouter()
const runDetail = ref<ResearchRunDetail | null>(null)
const loading = ref(true)
const error = ref('')
const actionLoading = ref(false)
const activeTab = ref<'answer' | 'citations' | 'steps' | 'candidates' | 'agentRuns'>('answer')
const agentRuns = ref<AgentRunListItem[]>([])

onMounted(loadRun)

async function loadRun() {
  const id = route.params.id as string
  loading.value = true
  error.value = ''
  try {
    runDetail.value = await getResearchRun(id)
    await loadAgentRuns(id)
  } catch {
    error.value = '無法載入研究結果。'
  } finally {
    loading.value = false
  }
}

async function loadAgentRuns(researchRunId: string) {
  try {
    const all = await listAgentRuns({ limit: 50, researchRunId })
    agentRuns.value = all
  } catch {
    // non-critical
  }
}

async function handleCreateCriticReview() {
  if (!runDetail.value) return
  actionLoading.value = true
  error.value = ''
  try {
    const created = await createCriticReview(runDetail.value.run.id)
    router.push({ name: 'agent-run-detail', params: { id: created.id } })
  } catch {
    error.value = '建立 CriticReview 失敗。'
  } finally {
    actionLoading.value = false
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
    default: return '#666666'
  }
}
</script>

<template>
  <div class="kimi-page-vscode">
    <div class="kimi-content">
      <div style="margin-top: 60px; margin-bottom: 12px">
        <button class="kimi-btn" @click="router.push({ name: 'research' })">← 返回列表</button>
      </div>

      <div v-if="loading" style="padding: 40px 0; color: var(--kimi-muted); font-size: 14px; text-align: center">載入中...</div>
      <div v-else-if="error" style="padding: 20px; color: #f87171; font-size: 14px">{{ error }}</div>

      <template v-else-if="runDetail">
        <!-- Header -->
        <ScrollReveal>
          <div class="kimi-section">
            <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
              <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 8px; flex-wrap: wrap">
                <span class="kimi-tag" style="font-family: var(--kimi-font-mono)">{{ runDetail.run.ticker }}</span>
                <span class="kimi-tag" :style="{ borderColor: statusColor(runDetail.run.status), color: statusColor(runDetail.run.status) }">{{ runDetail.run.status }}</span>
                <span class="kimi-tag">{{ runDetail.run.retrievalMode }}</span>
              </div>
              <h2 style="margin: 0 0 12px 0; font-size: 20px; font-weight: 600">{{ runDetail.run.question }}</h2>
              <div style="display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 12px">
                <button class="kimi-btn kimi-btn-solid" :disabled="actionLoading" @click="handleCreateCriticReview">
                  {{ actionLoading ? '建立中...' : '執行 Critic Review' }}
                </button>
                <button class="kimi-btn" @click="loadRun">重新整理</button>
              </div>
              <div style="display: flex; gap: 20px; font-size: 12px; color: var(--kimi-muted); flex-wrap: wrap">
                <span>Citations：{{ runDetail.run.citationCount }}</span>
                <span>延遲：{{ runDetail.run.latencyMs }}ms</span>
                <span>建立：{{ formatDate(runDetail.run.createdAtUtc) }}</span>
              </div>
            </div>

            <!-- Tabs -->
            <div style="display: flex; border-bottom: 1px solid var(--kimi-border-light); overflow-x: auto">
              <button
                v-for="tab in (['answer', 'citations', 'steps', 'candidates', 'agentRuns'] as const)"
                :key="tab"
                :style="{ padding: '12px 20px', background: activeTab === tab ? 'var(--kimi-text-light)' : 'transparent', border: 'none', borderBottom: 'none', color: activeTab === tab ? 'var(--kimi-bg-light)' : 'var(--kimi-muted)', cursor: 'pointer', fontSize: '13px', fontWeight: activeTab === tab ? '600' : '400', fontFamily: 'var(--kimi-font-body)', letterSpacing: '0.05em' }"
                @click="activeTab = tab"
              >
                {{ tab === 'answer' ? '研究答案' : tab === 'citations' ? '引用來源' : tab === 'steps' ? '執行步驟' : tab === 'candidates' ? '候選文件' : 'Agent Runs' }}
              </button>
            </div>

            <!-- Tab: Answer -->
            <div v-if="activeTab === 'answer'" style="padding: 24px 20px">
              <div style="white-space: pre-wrap; line-height: 1.8; font-size: 14px">{{ runDetail.answer }}</div>
            </div>

            <!-- Tab: Citations -->
            <div v-if="activeTab === 'citations'">
              <div v-for="cite in runDetail.citations" :key="cite.id" style="display: flex; align-items: flex-start; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)">
                <div style="flex: 1; min-width: 0">
                  <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 4px">
                    <span class="kimi-tag" style="font-family: var(--kimi-font-mono); font-size: 11px">#{{ cite.citationIndex }}</span>
                    <span class="kimi-tag" style="font-size: 11px">{{ cite.sourceType }}</span>
                    <span v-if="cite.sourceRole" class="kimi-tag" style="font-size: 11px">{{ cite.sourceRole }}</span>
                  </div>
                  <div style="font-size: 14px; font-weight: 500; margin-bottom: 4px">{{ cite.title }}</div>
                  <div style="font-size: 13px; color: var(--kimi-muted); font-style: italic">"{{ cite.quoteText }}"</div>
                  <div style="display: flex; gap: 16px; margin-top: 6px; font-size: 12px; color: var(--kimi-muted)">
                    <span>Page {{ cite.pageNumber ?? '-' }}</span>
                    <span>Score: {{ cite.relevanceScore.toFixed(3) }}</span>
                  </div>
                </div>
              </div>
              <div v-if="runDetail.citations.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center">暫無引用來源。</div>
            </div>

            <!-- Tab: Steps -->
            <div v-if="activeTab === 'steps'">
              <div v-for="step in runDetail.steps" :key="step.id" style="padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)">
                <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px">
                  <span style="font-family: var(--kimi-font-mono); font-size: 14px; font-weight: 600">{{ step.stepType }}</span>
                  <span v-if="step.durationMs !== null" style="font-size: 12px; color: var(--kimi-muted)">{{ step.durationMs }}ms</span>
                </div>
                <div v-if="step.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 6px">{{ step.errorMessage }}</div>
                <div style="display: flex; gap: 16px; font-size: 12px; color: var(--kimi-muted); margin-bottom: 8px">
                  <span>開始：{{ formatDate(step.startedAtUtc) }}</span>
                  <span>完成：{{ formatDate(step.completedAtUtc) }}</span>
                </div>
                <pre v-if="step.outputJson" style="padding: 12px; background: var(--kimi-bg-alt); border: 1px solid var(--kimi-border-light); font-size: 12px; color: var(--kimi-muted); overflow-x: auto; white-space: pre-wrap">{{ step.outputJson }}</pre>
              </div>
              <div v-if="runDetail.steps.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center">暫無執行步驟。</div>
            </div>

            <!-- Tab: Candidates -->
            <div v-if="activeTab === 'candidates'">
              <div v-for="c in runDetail.candidates" :key="c.id" style="padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)">
                <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 4px; gap: 8px; flex-wrap: wrap">
                  <div style="display: flex; align-items: center; gap: 8px">
                    <span style="font-size: 14px; font-weight: 500">{{ c.title ?? 'Untitled' }}</span>
                    <span class="kimi-tag" style="font-size: 11px">{{ c.decision }}</span>
                  </div>
                  <span style="font-size: 12px; color: var(--kimi-muted)">Score: {{ c.relevanceScore.toFixed(3) }}</span>
                </div>
                <div v-if="c.discardReason" style="color: #fbbf24; font-size: 12px; margin-bottom: 4px">{{ c.discardReason }}</div>
                <div v-if="c.contentPreview" style="font-size: 13px; color: var(--kimi-muted); line-height: 1.5">{{ c.contentPreview }}</div>
              </div>
              <div v-if="runDetail.candidates.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center">暫無候選文件。</div>
            </div>

            <!-- Tab: Agent Runs -->
            <div v-if="activeTab === 'agentRuns'">
              <div v-for="ar in agentRuns" :key="ar.id" style="display: flex; align-items: center; justify-content: space-between; padding: 14px 20px; border-bottom: 1px solid var(--kimi-border-light); cursor: pointer; transition: background 0.2s" @click="router.push({ name: 'agent-run-detail', params: { id: ar.id } })">
                <div>
                  <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 2px">
                    <span class="kimi-tag" style="font-family: var(--kimi-font-mono); font-size: 11px">{{ ar.workflowType }}</span>
                    <span class="kimi-tag" style="font-size: 11px">{{ ar.agentType }}</span>
                    <span class="kimi-tag" :style="{ borderColor: statusColor(ar.status), color: statusColor(ar.status) }">{{ ar.status }}</span>
                  </div>
                  <div style="font-family: var(--kimi-font-mono); font-size: 12px; color: var(--kimi-muted)">{{ ar.id }}</div>
                </div>
                <span style="color: var(--kimi-muted); font-size: 12px">{{ formatDate(ar.createdAtUtc) }}</span>
              </div>
              <div v-if="agentRuns.length === 0" style="padding: 40px 20px; color: var(--kimi-muted); text-align: center">暫無關聯的 Agent Runs。</div>
            </div>
          </div>
        </ScrollReveal>

        <div style="height: 80px" />
      </template>
    </div>

    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">RESEARCH DETAIL</span>
      <span>數據僅供參考</span>
    </footer>
  </div>
</template>
