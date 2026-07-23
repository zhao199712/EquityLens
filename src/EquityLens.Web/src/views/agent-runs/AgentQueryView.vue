<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useChatStore } from '../../stores/chat'

const chat = useChatStore()
const input = ref('')

onMounted(async () => {
  await chat.loadSessions()
  if (!chat.currentSessionId) await chat.startNewSession()
})

async function send() {
  const value = input.value.trim()
  if (!value || chat.isStreaming) return
  input.value = ''
  await chat.sendMessage(value)
}

function keydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    send()
  }
}
</script>

<template>
  <main class="prestige-page conversation-page">
    <aside class="prestige-panel sessions">
      <div class="session-head">
        <div><p class="prestige-label">CONVERSATIONS</p><h2>對話</h2></div>
        <button class="prestige-btn" @click="chat.startNewSession()">＋ 新對話</button>
      </div>
      <button
        v-for="session in chat.sessions"
        :key="session.id"
        :class="['session-row', session.id === chat.currentSessionId && 'active']"
        @click="chat.selectSession(session.id)"
      >
        <strong>{{ session.title || '新對話' }}</strong>
        <small>{{ session.messageCount }} messages</small>
      </button>
    </aside>

    <section class="conversation-main">
      <header>
        <p class="prestige-label">CONVERSATION AGENT</p>
        <h1>想了解什麼？</h1>
        <p>我會維護本次對話脈絡；需要資料與分析時，再交給受控的投研 workflow。</p>
      </header>

      <div class="prestige-panel message-list">
        <div v-if="!chat.messages.length && !chat.isStreaming" class="welcome">
          <strong>可以直接打招呼，也可以延續追問。</strong>
          <span>例如：「分析聯發科下一季指引」→「那台積電呢？」</span>
        </div>
        <article v-for="message in chat.messages" :key="message.id" :class="['message', message.role]">
          <div class="bubble">
            <p>{{ message.content }}</p>
            <section v-if="message.runCard" class="prestige-panel run-card">
              <div><strong>{{ message.runCard.workflowType }}</strong><span>{{ message.runCard.status }}</span></div>
              <p v-if="message.runCard.currentStageDisplayName">目前階段：{{ message.runCard.currentStageDisplayName }}</p>
              <div class="progress"><span :style="{ width: `${message.runCard.totalNodes ? message.runCard.completedNodes / message.runCard.totalNodes * 100 : 0}%` }"></span></div>
              <small>{{ message.runCard.completedNodes }} / {{ message.runCard.totalNodes }} stages</small>
              <p v-if="message.runCard.finalAnswer" class="final-answer">{{ message.runCard.finalAnswer }}</p>
              <p v-if="message.runCard.errorMessage" class="prestige-error">{{ message.runCard.errorMessage }}</p>
              <RouterLink :to="{ name: 'agent-run-detail', params: { id: message.runCard.agentRunId } }">查看 Agent Run →</RouterLink>
            </section>
          </div>
        </article>
        <article v-if="chat.isStreaming" class="message assistant"><div class="bubble">{{ chat.streamingContent || '正在理解你的問題…' }}</div></article>
      </div>

      <div v-if="chat.error" class="prestige-error">{{ chat.error }}</div>
      <div class="prestige-panel composer">
        <textarea v-model="input" maxlength="2000" placeholder="輸入訊息…" :disabled="chat.isStreaming" @keydown="keydown" />
        <button class="prestige-btn prestige-btn-solid" :disabled="!input.trim() || chat.isStreaming" @click="send">
          {{ chat.isStreaming ? '處理中…' : '送出' }}
        </button>
      </div>
    </section>
  </main>
</template>

<style scoped>
.conversation-page { display: grid; grid-template-columns: 280px minmax(0, 1fr); gap: 20px; min-height: calc(100vh - 60px); padding: 28px; }
.sessions { padding: 16px; align-self: stretch; }.session-head { display: grid; gap: 12px; margin-bottom: 18px; }.session-head h2 { margin: 5px 0 0; color: var(--ivory); font-family: var(--serif); }
.session-row { display: grid; gap: 4px; width: 100%; padding: 12px; border: 0; border-bottom: 1px solid var(--gold-border-soft); background: transparent; color: var(--ivory); text-align: left; cursor: pointer; }.session-row.active { background: rgba(201,168,106,.08); }.session-row small { color: var(--muted); }
.conversation-main { display: grid; grid-template-rows: auto minmax(360px, 1fr) auto; gap: 16px; min-width: 0; }.conversation-main header h1 { margin: 8px 0; color: var(--ivory); font: 500 clamp(32px,5vw,52px)/1.1 var(--serif); }.conversation-main header p:last-child { color: var(--muted); }
.message-list { padding: 22px; overflow: auto; }.welcome { display: grid; gap: 8px; place-content: center; min-height: 260px; color: var(--muted); text-align: center; }
.message { display: flex; margin: 12px 0; }.message.user { justify-content: flex-end; }.bubble { max-width: min(760px,88%); padding: 12px 16px; border: 1px solid var(--gold-border-soft); border-radius: 8px; color: var(--ivory); background: rgba(255,255,255,.025); white-space: pre-wrap; }.user .bubble { background: rgba(201,168,106,.12); }.bubble > p { margin: 0; line-height: 1.65; }
.run-card { display: grid; gap: 10px; margin-top: 14px; padding: 14px; }.run-card > div:first-child { display: flex; justify-content: space-between; color: var(--gold); }.run-card p,.run-card small { color: var(--muted); }.progress { height: 5px; border-radius: 999px; background: rgba(255,255,255,.08); overflow: hidden; }.progress span { display: block; height: 100%; background: var(--gold); }.final-answer { max-height: 340px; overflow: auto; padding-top: 10px; border-top: 1px solid var(--gold-border-soft); white-space: pre-wrap; }.run-card a { color: var(--gold); }
.composer { display: flex; gap: 12px; padding: 14px; }.composer textarea { flex: 1; min-height: 58px; resize: vertical; padding: 12px; border: 1px solid var(--gold-border); background: transparent; color: var(--ivory); font: inherit; }
@media (max-width: 820px) { .conversation-page { grid-template-columns: 1fr; padding: 16px; }.sessions { max-height: 220px; overflow: auto; } }
</style>
