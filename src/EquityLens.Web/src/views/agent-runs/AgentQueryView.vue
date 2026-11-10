<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { useChatStore } from '../../stores/chat'
import { renderMarkdown } from '../../utils/markdown'
import { isSubmitEnter } from '../../utils/keyboard'
import i18n from '../../locales'

const chat = useChatStore()
const input = ref('')
const messageList = ref<HTMLElement | null>(null)
const composer = ref<HTMLTextAreaElement | null>(null)
const english = computed(() => i18n.global.locale.value === 'en')
const suggestedMessage = computed(() => {
  if (chat.isStreaming || chat.isLoadingMessages || chat.error || !chat.currentSessionId) return null
  const message = chat.messages.at(-1)
  const card = message?.runCard
  return message?.role === 'assistant' && card?.status === 'Succeeded' && card.finalAnswer?.trim()
    && !card.errorMessage && !card.qualityIssues?.length && card.suggestedActions?.length ? message : null
})
const suggestedActions = computed(() => (suggestedMessage.value?.runCard?.suggestedActions ?? []).slice(0, 3).map(action => ({
  id: action.id,
  label: english.value ? action.labelEn ?? action.label : action.label,
  prompt: english.value ? action.promptEn ?? action.prompt : action.prompt,
})))

async function fillSuggestion(id: string) {
  if (input.value.length > 0 || chat.isStreaming || chat.isLoadingMessages) return
  const action = suggestedActions.value.find(item => item.id === id)
  if (!action) return
  input.value = action.prompt
  await nextTick()
  resizeComposer()
  composer.value?.focus()
}

const choiceMessageId = computed(() => {
  if (chat.isStreaming || chat.isLoadingMessages || chat.error) return null
  const message = chat.messages.at(-1)
  return message?.role === 'assistant' && message.choices?.length ? message.id : null
})

const workflowLabels: Record<string, string> = {
  ResearchAgent: '公司研究',
  PortfolioDiagnosis: '投組診斷',
  CriticReview: '品質審查',
}
const statusLabels: Record<string, string> = {
  Succeeded: '完成', Failed: '未完成', Cancelled: '已取消', Running: '分析中', Queued: '排隊中',
  Pending: '準備中', Ready: '準備中', Created: '準備中', WaitingForFeedback: '等待回饋', WaitingForApproval: '等待確認',
}
const qualityIssueLabels: Record<string, string> = {
  InsufficientEvidence: '資料不足以回答',
  UntraceableNumbers: '部分數字無法對應工具資料',
  MixedRiskBasis: '混用不同風險口徑',
  UnverifiedScenarioComparison: '情境比較未完整驗證',
  ScenarioComparisonDirectionMismatch: '情境改善方向不一致',
  PartialScenarioFailure: '部分情境無法計算',
  OptimizationScenarioRequired: '缺少優化前後比較',
  OptimizationDataUnavailable: '歷史資料不足以比較',
  UnknownToolEvidence: '引用了不存在的資料',
  MissingToolEvidence: '沒有取得工具資料',
  StaleRiskProfileComparison: '風險偏好比較過期',
  UnverifiedProfileFit: '風險偏好比較未驗證',
  DefaultPreferenceMisrepresented: '預設風險偏好描述不正確',
  GuaranteedProfitClaim: '含保證獲利敘述',
}
const workflowLabel = (type: string) => workflowLabels[type] ?? type
const statusLabel = (status: string) => statusLabels[status] ?? status
const qualityText = (issues: string[]) => [...new Set(issues.map(issue => qualityIssueLabels[issue] ?? '答案品質未通過檢查'))].join('、')
// 缺文件屬資料範圍限制，不是系統錯誤；以提示色呈現。
const isDataGap = (message?: string | null) => !!message && /尚未匯入|資料不足|沒有可用|找不到.*文件/.test(message)

const examples = [
  { label: '投組風險範例', question: '我的投資組合最近一年的波動、最大回撤與集中風險如何？' },
  { label: '公司研究範例', question: '台積電最近一季的營運表現與主要風險是什麼？' },
]

onMounted(async () => {
  await chat.loadSessions()
})

async function scrollToBottom() {
  await nextTick()
  if (messageList.value) {
    messageList.value.scrollTop = messageList.value.scrollHeight
  }
}

watch(() => chat.streamingContent, () => { scrollToBottom() })
watch(() => chat.messages.length, () => { scrollToBottom() })
watch(suggestedMessage, () => { scrollToBottom() })

async function send(text?: string) {
  const value = (text ?? input.value).trim()
  if (!value || chat.isStreaming || chat.isLoadingMessages) return
  input.value = ''
  resizeComposer()
  await chat.sendMessage(value)
  scrollToBottom()
}

function keydown(event: KeyboardEvent) {
  if (isSubmitEnter(event)) {
    event.preventDefault()
    send()
  }
}

function resizeComposer() {
  const el = composer.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${Math.min(el.scrollHeight, 160)}px`
}

function confirmRemove(sessionId: string) {
  if (window.confirm('確定要刪除這個對話嗎？')) {
    chat.removeSession(sessionId)
  }
}

function formatTime(updatedAtUtc: string) {
  return dayjs(updatedAtUtc).format('MM/DD HH:mm')
}
</script>

<template>
  <main class="prestige-page conversation-page">
    <aside class="prestige-panel sessions">
      <div class="session-head">
        <div>
          <p class="prestige-label">CONVERSATIONS</p>
          <h2>對話</h2>
        </div>
        <button class="prestige-btn" @click="chat.startNewSession()">＋ 新對話</button>
      </div>
      <div class="session-list">
        <div
          v-for="session in chat.sessions"
          :key="session.id"
          :class="['session-row', session.id === chat.currentSessionId && 'active']"
          @click="chat.selectSession(session.id)"
        >
          <div class="session-row-main">
            <strong>{{ session.title || '新對話' }}</strong>
            <small>{{ formatTime(session.updatedAtUtc) }} · {{ session.messageCount }} 則訊息</small>
          </div>
          <button
            class="session-delete"
            title="刪除對話"
            @click.stop="confirmRemove(session.id)"
          >×</button>
        </div>
        <p v-if="!chat.sessions.length" class="session-empty">尚無對話記錄</p>
      </div>
    </aside>

    <section class="conversation-main">
      <header>
        <p class="prestige-label">ASK AGENT</p>
        <h1>想了解什麼？</h1>
        <p>我會維護本次對話脈絡；需要資料與分析時，再交給受控的投研 workflow。</p>
      </header>

      <div ref="messageList" class="prestige-panel message-list">
        <div v-if="!chat.messages.length && !chat.isStreaming" class="welcome">
          <strong>可以直接打招呼，也可以延續追問。</strong>
          <span>例如先問「鴻海最近一季的營運表現如何？」，再追問「那台積電呢？」</span>
          <div class="examples">
            <button
              v-for="example in examples"
              :key="example.label"
              class="example-btn"
              @click="send(example.question)"
            >
              <small>{{ example.label }}</small>
              {{ example.question }}
            </button>
          </div>
        </div>

        <article v-for="message in chat.messages" :key="message.id" :class="['message', message.role]">
          <div v-if="message.role === 'assistant'" class="avatar">EL</div>
          <div class="bubble">
            <div class="message-content" v-html="renderMarkdown(message.content || '')"></div>
            <section v-if="message.runCard" class="run-card">
              <div class="run-card-head">
                <strong>{{ workflowLabel(message.runCard.workflowType) }}</strong>
                <span :class="['run-status', `run-status--${isDataGap(message.runCard.errorMessage) ? 'datagap' : message.runCard.status.toLowerCase()}`]">
                  {{ isDataGap(message.runCard.errorMessage) ? '資料不足' : statusLabel(message.runCard.status) }}
                </span>
              </div>
              <p v-if="message.runCard.workflowType !== 'ResearchAgent' && message.runCard.currentStageDisplayName">目前階段：{{ message.runCard.currentStageDisplayName }}</p>
              <div v-if="message.runCard.workflowType !== 'ResearchAgent'" class="progress">
                <span :style="{ width: `${message.runCard.totalNodes ? message.runCard.completedNodes / message.runCard.totalNodes * 100 : 0}%` }"></span>
              </div>
              <small v-if="message.runCard.workflowType !== 'ResearchAgent'">步驟 {{ message.runCard.completedNodes }} / {{ message.runCard.totalNodes }}</small>
              <small v-else-if="!['Succeeded', 'Failed', 'Cancelled'].includes(message.runCard.status)">正在檢索文件並整理答案…</small>
              <div v-if="message.runCard.status === 'Succeeded' && message.runCard.finalAnswer" class="final-answer" v-html="renderMarkdown(message.runCard.finalAnswer)"></div>
              <p v-if="message.runCard.qualityIssues?.length" class="quality-note">
                已隱藏未通過驗證的內容（{{ qualityText(message.runCard.qualityIssues) }}），以上只保留可對應工具資料的部分。可以換個問法，或開啟分析詳情查看原因。
              </p>
              <p v-if="['WaitingForFeedback', 'WaitingForApproval'].includes(message.runCard.status)">已暫停，請開啟 Agent Run 審閱草稿。</p>
              <p v-if="message.runCard.errorMessage" :class="isDataGap(message.runCard.errorMessage) ? 'quality-note' : 'prestige-error'">{{ message.runCard.errorMessage }}</p>
              <RouterLink :to="{ name: 'agent-run-detail', params: { id: message.runCard.agentRunId } }">查看分析詳情 →</RouterLink>
            </section>
            <section v-if="choiceMessageId === message.id" class="suggested-actions" aria-label="選擇投資組合">
              <div class="suggested-action-options">
                <button
                  v-for="(choice, index) in message.choices"
                  :key="choice"
                  type="button"
                  class="suggested-action-btn"
                  :disabled="chat.isStreaming || chat.isLoadingMessages"
                  @click="send(choice)"
                >{{ index + 1 }}. {{ choice }}</button>
              </div>
              <small>點選投組，或直接輸入名稱、序號。</small>
            </section>
            <section v-if="suggestedMessage?.id === message.id" class="suggested-actions" :aria-label="english ? 'Suggested follow-ups' : '建議追問'">
              <p>{{ english ? 'You can ask next' : '你也可以接著問' }}</p>
              <div class="suggested-action-options">
                <button
                  v-for="action in suggestedActions"
                  :key="action.id"
                  type="button"
                  class="suggested-action-btn"
                  :title="action.prompt"
                  :disabled="input.length > 0 || chat.isStreaming || chat.isLoadingMessages"
                  @click="fillSuggestion(action.id)"
                >{{ action.label }}</button>
              </div>
              <small>{{ input.length > 0
                ? (english ? 'Clear your draft to choose another question.' : '清空草稿後即可選擇其他追問。')
                : (english ? 'Choose a question to edit, then send when ready.' : '點選後填入輸入框，可修改並確認後送出。') }}</small>
            </section>
          </div>
        </article>

        <article v-if="chat.isStreaming" class="message assistant">
          <div class="avatar">EL</div>
          <div class="bubble">
            <template v-if="chat.streamingContent">
              <span class="message-content" v-html="renderMarkdown(chat.streamingContent)"></span>
              <span class="cursor">|</span>
            </template>
            <div v-else class="typing"><span></span><span></span><span></span></div>
          </div>
        </article>
      </div>

      <div v-if="chat.error" class="prestige-error">{{ chat.error }}</div>

      <div class="prestige-panel composer">
        <textarea
          ref="composer"
          v-model="input"
          maxlength="2000"
          rows="1"
          placeholder="輸入訊息…（Enter 送出，Shift+Enter 換行）"
          :disabled="chat.isStreaming || chat.isLoadingMessages"
          @keydown="keydown"
          @input="resizeComposer"
        />
        <div class="composer-side">
          <small>{{ input.length }} / 2000</small>
          <button class="prestige-btn prestige-btn-solid" :disabled="!input.trim() || chat.isStreaming || chat.isLoadingMessages" @click="send()">
            {{ chat.isStreaming ? '處理中…' : '送出' }}
          </button>
        </div>
      </div>
    </section>
  </main>
</template>

<style scoped>
.conversation-page { display: grid; grid-template-columns: 300px minmax(0, 1fr); gap: 20px; height: calc(100vh - 60px); padding: 28px; box-sizing: border-box; }

/* Sessions */
.sessions { display: flex; flex-direction: column; padding: 16px; overflow: hidden; }
.session-head { display: grid; gap: 12px; margin-bottom: 16px; }
.session-head h2 { margin: 5px 0 0; color: var(--ivory); font-family: var(--serif); }
.session-list { flex: 1; overflow-y: auto; display: flex; flex-direction: column; }
.session-row { display: flex; align-items: center; gap: 8px; width: 100%; padding: 12px; border-bottom: 1px solid var(--gold-border-soft); color: var(--ivory); cursor: pointer; }
.session-row.active { background: rgba(201,168,106,.08); }
.session-row-main { flex: 1; min-width: 0; display: grid; gap: 4px; text-align: left; }
.session-row-main strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 500; }
.session-row-main small { color: var(--muted); }
.session-delete { flex-shrink: 0; width: 24px; height: 24px; border: 0; border-radius: 6px; background: transparent; color: var(--muted); font-size: 16px; line-height: 1; cursor: pointer; opacity: 0; transition: opacity .15s; }
.session-row:hover .session-delete { opacity: 1; }
.session-delete:hover { color: #f87171; background: rgba(248,113,113,.12); }
.session-empty { color: var(--muted); text-align: center; padding: 24px 0; }

/* Main column */
.conversation-main { display: grid; grid-template-rows: auto minmax(0, 1fr) auto auto; gap: 16px; min-width: 0; min-height: 0; }
.conversation-main header h1 { margin: 8px 0; color: var(--ivory); font: 500 clamp(28px,4vw,44px)/1.1 var(--serif); }
.conversation-main header p:last-child { color: var(--muted); margin: 0; }

/* Messages */
.message-list { padding: 22px; overflow-y: auto; }
.welcome { display: grid; gap: 10px; place-content: center; justify-items: center; min-height: 100%; color: var(--muted); text-align: center; }
.examples { display: grid; gap: 10px; margin-top: 14px; width: min(520px, 100%); }
.example-btn { display: grid; gap: 4px; padding: 12px 16px; border: 1px solid var(--gold-border-soft); border-radius: 8px; background: transparent; color: var(--ivory); text-align: left; cursor: pointer; font: inherit; transition: border-color .15s, background .15s; }
.example-btn small { color: var(--gold); letter-spacing: .08em; }
.example-btn:hover { border-color: var(--gold); background: rgba(201,168,106,.06); }

.message { display: flex; gap: 10px; margin: 14px 0; }
.message.user { justify-content: flex-end; }
.avatar { flex-shrink: 0; width: 30px; height: 30px; display: grid; place-items: center; border: 1px solid var(--gold-border); border-radius: 8px; color: var(--gold); font-size: 11px; font-weight: 700; letter-spacing: .06em; }
.bubble { max-width: min(760px, 85%); padding: 12px 16px; border: 1px solid var(--gold-border-soft); border-radius: 10px; color: var(--ivory); background: rgba(255,255,255,.025); line-height: 1.65; }
.message.user .bubble { background: rgba(201,168,106,.12); }
.message-content { word-break: break-word; }
.message-content :deep(code) { background: rgba(201,168,106,.14); padding: 2px 6px; border-radius: 4px; font-size: .92em; }

.cursor { animation: blink 1s step-end infinite; color: var(--gold); }
@keyframes blink { 50% { opacity: 0; } }
.typing { display: flex; gap: 5px; padding: 6px 0; }
.typing span { width: 6px; height: 6px; border-radius: 50%; background: var(--gold); animation: typing 1.4s infinite ease-in-out; }
.typing span:nth-child(2) { animation-delay: .2s; }
.typing span:nth-child(3) { animation-delay: .4s; }
@keyframes typing { 0%, 80%, 100% { transform: scale(.6); opacity: .4; } 40% { transform: scale(1); opacity: 1; } }

/* Suggested questions */
.suggested-actions { margin-top: 16px; padding-top: 12px; border-top: 1px solid var(--gold-border-soft); }
.suggested-actions p { margin: 0 0 8px; color: var(--gold); font-size: 13px; }
.suggested-actions small { display: block; margin-top: 8px; color: var(--muted); font-size: 12px; }
.suggested-action-options { display: flex; flex-wrap: wrap; gap: 8px; }
.suggested-action-btn { max-width: 100%; padding: 8px 12px; border: 1px solid var(--gold-border); border-radius: 18px; background: rgba(201,168,106,.06); color: var(--gold); font: inherit; font-size: 13px; text-align: left; overflow-wrap: anywhere; cursor: pointer; }
.suggested-action-btn:hover:not(:disabled) { background: rgba(201,168,106,.14); }
.suggested-action-btn:focus-visible { outline: 2px solid var(--gold); outline-offset: 3px; }
.suggested-action-btn:disabled { opacity: .5; cursor: not-allowed; }

/* Run card */
.run-card { display: grid; gap: 10px; margin-top: 14px; padding: 14px; border: 1px solid var(--gold-border-soft); border-radius: 8px; background: rgba(0,0,0,.2); }
.run-card-head { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 10px; color: var(--gold); }
.run-status { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
.run-status--running, .run-status--pending { color: var(--gold); }
.run-status--succeeded { color: #4ade80; }
.run-status--failed, .run-status--cancelled { color: #f87171; }
.run-status--datagap { color: #e8c07d; }
.quality-note { margin: 10px 0 0; padding: 10px 12px; border-left: 3px solid #e8c07d; background: rgba(232, 192, 125, .08); color: var(--ivory); font-size: .9em; line-height: 1.6; }
.run-card p, .run-card small { margin: 0; color: var(--muted); }
.progress { height: 5px; border-radius: 999px; background: rgba(255,255,255,.08); overflow: hidden; }
.progress span { display: block; height: 100%; background: var(--gold); transition: width .25s ease; }
.final-answer { padding-top: 10px; border-top: 1px solid var(--gold-border-soft); color: var(--ivory); line-height: 1.65; }
.final-answer :deep(code) { background: rgba(201,168,106,.14); padding: 2px 6px; border-radius: 4px; font-size: .92em; }
.run-card a { color: var(--gold); }

/* Composer */
.composer { display: flex; align-items: flex-end; gap: 12px; padding: 14px; }
.composer textarea { flex: 1; min-height: 44px; max-height: 160px; resize: none; padding: 12px; border: 1px solid var(--gold-border); border-radius: 8px; background: transparent; color: var(--ivory); font: inherit; line-height: 1.5; }
.composer textarea:focus { outline: none; border-color: var(--gold); }
.composer textarea:disabled { opacity: .6; }
.composer-side { display: grid; gap: 8px; justify-items: end; }
.composer-side small { color: var(--muted); }

@media (max-width: 900px) {
  .conversation-page { grid-template-columns: 1fr; grid-template-rows: auto minmax(0, 1fr); height: auto; min-height: calc(100vh - 60px); padding: 16px; }
  .sessions { max-height: 180px; }
  .conversation-main { grid-template-rows: auto minmax(320px, 1fr) auto auto; }
}
</style>
