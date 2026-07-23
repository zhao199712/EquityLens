<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { createAgentWorkflowQuery, type AgentWorkflowQueryCreated } from '../../services/agentQueries'

const router = useRouter()
const question = ref('')
const loading = ref(false)
const error = ref('')
const result = ref<AgentWorkflowQueryCreated | null>(null)
const canSubmit = computed(() => question.value.trim().length > 0 && !loading.value)

async function submit() {
  if (!canSubmit.value) return
  loading.value = true
  error.value = ''
  result.value = null
  try {
    result.value = await createAgentWorkflowQuery(question.value.trim())
    if (result.value.workflowType === 'ResearchInvestigation' && result.value.researchRunId) {
      await router.push({ name: 'research-run-detail', params: { id: result.value.researchRunId } })
    } else {
      await router.push({ name: 'agent-run-detail', params: { id: result.value.agentRunId } })
    }
  } catch (caught: unknown) {
    const value = caught as { response?: { data?: { message?: string } } }
    error.value = value.response?.data?.message ?? '無法判斷適合的 workflow，請稍後再試。'
  } finally {
    loading.value = false
  }
}

function openRun() {
  if (result.value) router.push({ name: 'agent-run-detail', params: { id: result.value.agentRunId } })
}
</script>

<template>
  <div class="prestige-page query-page">
    <section class="query-shell">
      <div class="query-heading">
        <span class="prestige-label">Ask Agent</span>
        <h1>描述你想了解的問題</h1>
        <p>LLM 會先選擇 workflow 與 lead skill，建立研究脈絡後啟動對應 DAG。</p>
      </div>

      <div class="prestige-panel composer">
        <label for="agent-question">問題</label>
        <textarea
          id="agent-question"
          v-model="question"
          maxlength="2000"
          placeholder="例如：我的投資組合最近一年的主要風險與最大拖累是什麼？"
          @keydown.meta.enter="submit"
          @keydown.ctrl.enter="submit"
        />
        <div class="composer-footer">
          <span>{{ question.length }} / 2000</span>
          <button class="prestige-btn prestige-btn-solid" :disabled="!canSubmit" @click="submit">
            {{ loading ? '正在路由問題與選擇 skill…' : '送出問題' }}
          </button>
        </div>
      </div>

      <div class="examples" aria-label="問題範例">
        <button @click="question = '我的投資組合最近一年的波動、最大回撤與集中風險如何？'">投組風險範例</button>
        <button @click="question = '台積電最近一季的營運表現與主要風險是什麼？'">公司研究範例</button>
        <button @click="question = '聯發科法說會相較上季的指引與管理層語氣改變了什麼？'">法說會要點範例</button>
      </div>

      <div v-if="error" class="prestige-error" role="alert">{{ error }}</div>

      <div v-if="result" class="prestige-panel result-card" data-testid="routing-result">
        <div>
          <span class="prestige-label">Selected Workflow</span>
          <h2>{{ result.workflowType }}</h2>
          <strong>{{ result.leadSkillDisplayName }} · {{ result.leadSkill }}</strong>
          <p>{{ result.routingReason }}</p>
        </div>
        <dl>
          <div><dt>狀態</dt><dd>{{ result.status }}</dd></div>
          <div><dt>模型</dt><dd>{{ result.routingModel }}</dd></div>
          <div><dt>信心度</dt><dd>{{ result.routingConfidence }}</dd></div>
          <div><dt>市場／資產</dt><dd>{{ result.contextEnvelope.market }} / {{ result.contextEnvelope.asset }}</dd></div>
          <div><dt>Run ID</dt><dd class="prestige-mono">{{ result.agentRunId }}</dd></div>
        </dl>
        <button class="prestige-btn prestige-btn-solid" @click="openRun">查看執行進度</button>
      </div>
    </section>
  </div>
</template>

<style scoped>
.query-page { min-height: calc(100vh - 60px); padding: clamp(44px, 8vw, 96px) 24px; }
.query-shell { width: min(820px, 100%); margin: 0 auto; }
.query-heading { margin-bottom: 28px; }
.query-heading h1 { margin: 10px 0 12px; color: #f5efe0; font-family: var(--serif); font-size: clamp(32px, 5vw, 54px); font-weight: 500; }
.query-heading p, .result-card p { color: #9a917c; line-height: 1.7; }
.composer { padding: 22px; }
.composer label { display: block; margin-bottom: 10px; color: #c9a86a; font-size: 13px; letter-spacing: .08em; }
.composer textarea { width: 100%; min-height: 150px; resize: vertical; box-sizing: border-box; padding: 16px; border: 1px solid rgba(201,168,106,.25); border-radius: 3px; outline: none; background: rgba(255,255,255,.025); color: #f5efe0; font: inherit; line-height: 1.65; }
.composer textarea:focus { border-color: #c9a86a; }
.composer-footer { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-top: 14px; color: #716a5d; font-size: 12px; }
.examples { display: flex; gap: 10px; flex-wrap: wrap; margin: 16px 0 28px; }
.examples button { border: 0; background: none; color: #9a917c; cursor: pointer; text-decoration: underline; text-underline-offset: 4px; }
.result-card { padding: 24px; display: grid; gap: 20px; }
.result-card h2 { margin: 8px 0; color: #f5efe0; font-family: var(--serif); }
.result-card dl { display: grid; gap: 10px; margin: 0; }
.result-card dl div { display: grid; grid-template-columns: 80px 1fr; gap: 12px; }
.result-card dt { color: #716a5d; }
.result-card dd { margin: 0; color: #d8d0bf; overflow-wrap: anywhere; }
@media (max-width: 600px) { .result-card dl div { grid-template-columns: 1fr; gap: 3px; } }
</style>
