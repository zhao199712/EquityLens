<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { useChatStore } from '../../stores/chat'
import { renderMarkdown } from '../../utils/markdown'

const chat = useChatStore()
const input = ref('')
const messageList = ref<HTMLElement | null>(null)
const composer = ref<HTMLTextAreaElement | null>(null)

const examples = [
  { label: '投組風險範例', question: '我的投資組合最近一年的波動、最大回撤與集中風險如何？' },
  { label: '公司研究範例', question: '台積電最近一季的營運表現與主要風險是什麼？' },
  { label: '法說會要點範例', question: '聯發科法說會相較上季的指引與管理層語氣改變了什麼？' },
]

onMounted(async () => {
  await chat.loadSessions()
  if (!chat.currentSessionId) await chat.startNewSession()
})

async function scrollToBottom() {
  await nextTick()
  if (messageList.value) {
    messageList.value.scrollTop = messageList.value.scrollHeight
  }
}

watch(() => chat.streamingContent, () => { scrollToBottom() })
watch(() => chat.messages.length, () => { scrollToBottom() })

async function send(text?: string) {
  const value = (text ?? input.value).trim()
  if (!value || chat.isStreaming) return
  input.value = ''
  resizeComposer()
  await chat.sendMessage(value)
  scrollToBottom()
}

function keydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
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
          <span>例如先問「分析聯發科下一季指引」，再追問「那台積電呢？」</span>
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
                <strong>{{ message.runCard.workflowType }}</strong>
                <span :class="['run-status', `run-status--${message.runCard.status.toLowerCase()}`]">
                  {{ message.runCard.status }}
                </span>
              </div>
              <p v-if="message.runCard.currentStageDisplayName">目前階段：{{ message.runCard.currentStageDisplayName }}</p>
              <div class="progress">
                <span :style="{ width: `${message.runCard.totalNodes ? message.runCard.completedNodes / message.runCard.totalNodes * 100 : 0}%` }"></span>
              </div>
              <small>{{ message.runCard.completedNodes }} / {{ message.runCard.totalNodes }} stages</small>
              <div v-if="message.runCard.finalAnswer" class="final-answer" v-html="renderMarkdown(message.runCard.finalAnswer)"></div>
              <p v-if="message.runCard.errorMessage" class="prestige-error">{{ message.runCard.errorMessage }}</p>
              <RouterLink :to="{ name: 'agent-run-detail', params: { id: message.runCard.agentRunId } }">查看 Agent Run →</RouterLink>
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
          :disabled="chat.isStreaming"
          @keydown="keydown"
          @input="resizeComposer"
        />
        <div class="composer-side">
          <small>{{ input.length }} / 2000</small>
          <button class="prestige-btn prestige-btn-solid" :disabled="!input.trim() || chat.isStreaming" @click="send()">
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

/* Run card */
.run-card { display: grid; gap: 10px; margin-top: 14px; padding: 14px; border: 1px solid var(--gold-border-soft); border-radius: 8px; background: rgba(0,0,0,.2); }
.run-card-head { display: flex; justify-content: space-between; align-items: center; gap: 10px; color: var(--gold); }
.run-status { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
.run-status--running, .run-status--pending { color: var(--gold); }
.run-status--succeeded { color: #4ade80; }
.run-status--failed, .run-status--cancelled { color: #f87171; }
.run-card p, .run-card small { margin: 0; color: var(--muted); }
.progress { height: 5px; border-radius: 999px; background: rgba(255,255,255,.08); overflow: hidden; }
.progress span { display: block; height: 100%; background: var(--gold); transition: width .25s ease; }
.final-answer { max-height: 340px; overflow-y: auto; padding-top: 10px; border-top: 1px solid var(--gold-border-soft); color: var(--ivory); line-height: 1.65; }
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
