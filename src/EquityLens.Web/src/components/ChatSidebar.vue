<script setup lang="ts">
import { ref, nextTick, watch } from 'vue'
import { useChatStore } from '../stores/chat'

const chatStore = useChatStore()
const inputText = ref('')
const messagesContainer = ref<HTMLElement | null>(null)

async function handleSend() {
  const text = inputText.value.trim()
  if (!text || chatStore.isStreaming) return
  inputText.value = ''
  await chatStore.sendMessage(text)
  await scrollToBottom()
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    handleSend()
  }
}

async function scrollToBottom() {
  await nextTick()
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  }
}

watch(() => chatStore.streamingContent, () => { scrollToBottom() })
watch(() => chatStore.messages.length, () => { scrollToBottom() })

</script>

<template>
  <Teleport to="body">
    <Transition name="chat-sidebar">
      <div v-if="chatStore.isOpen" class="chat-overlay" @click.self="chatStore.closeSidebar()">
        <aside class="chat-sidebar">
          <!-- Header -->
          <div class="chat-header">
            <div class="chat-header-left">
              <span class="chat-logo">EL</span>
              <span class="chat-title">EquityLens AI</span>
            </div>
            <button class="chat-close-btn" @click="chatStore.closeSidebar()">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6L6 18M6 6l12 12"/></svg>
            </button>
          </div>

          <!-- Session list -->
          <div v-if="!chatStore.currentSessionId" class="chat-sessions">
            <button class="chat-new-btn" @click="chatStore.startNewSession()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 5v14M5 12h14"/></svg>
              新對話
            </button>
            <div class="session-list">
              <div
                v-for="session in chatStore.sessions"
                :key="session.id"
                class="session-item"
                @click="chatStore.selectSession(session.id)"
              >
                <span class="session-title">{{ session.title || '新對話' }}</span>
                <span class="session-time">{{ new Date(session.updatedAtUtc).toLocaleDateString() }}</span>
              </div>
              <div v-if="chatStore.sessions.length === 0" class="session-empty">
                尚無對話記錄
              </div>
            </div>
          </div>

          <!-- Chat messages -->
          <div v-else class="chat-messages" ref="messagesContainer">
            <div class="chat-messages-inner">
              <button class="chat-back-btn" @click="chatStore.currentSessionId = null; chatStore.loadSessions()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>
                返回
              </button>

              <div
                v-for="msg in chatStore.messages"
                :key="msg.id"
                :class="['chat-msg', `chat-msg--${msg.role}`]"
              >
                <div v-if="msg.role === 'assistant'" class="chat-msg-avatar">AI</div>
                <div class="chat-msg-bubble">
                  <div class="chat-msg-content" v-html="renderMarkdown(msg.content || '')"></div>
                  <div v-if="msg.runCard" class="conversation-run-card">
                    <div class="run-card-head">
                      <strong>{{ msg.runCard.workflowType }}</strong>
                      <span :class="`run-status run-status--${msg.runCard.status.toLowerCase()}`">{{ msg.runCard.status }}</span>
                    </div>
                    <p v-if="msg.runCard.currentStageDisplayName">{{ msg.runCard.currentStageDisplayName }}</p>
                    <div class="run-progress">
                      <span :style="{ width: `${msg.runCard.totalNodes ? (msg.runCard.completedNodes / msg.runCard.totalNodes) * 100 : 0}%` }"></span>
                    </div>
                    <small>{{ msg.runCard.completedNodes }} / {{ msg.runCard.totalNodes }} stages</small>
                    <div v-if="msg.runCard.finalAnswer" class="run-answer" v-html="renderMarkdown(msg.runCard.finalAnswer)"></div>
                    <div v-if="msg.runCard.errorMessage" class="chat-error">{{ msg.runCard.errorMessage }}</div>
                    <RouterLink :to="{ name: 'agent-run-detail', params: { id: msg.runCard.agentRunId } }">查看執行詳情 →</RouterLink>
                  </div>
                </div>
              </div>

              <!-- Streaming content -->
              <div v-if="chatStore.isStreaming && chatStore.streamingContent" class="chat-msg chat-msg--assistant">
                <div class="chat-msg-avatar">AI</div>
                <div class="chat-msg-bubble">
                  <div class="chat-msg-content" v-html="renderMarkdown(chatStore.streamingContent)"></div>
                  <span class="chat-cursor">|</span>
                </div>
              </div>

              <!-- Loading -->
              <div v-if="chatStore.isStreaming && !chatStore.streamingContent" class="chat-msg chat-msg--assistant">
                <div class="chat-msg-avatar">AI</div>
                <div class="chat-msg-bubble">
                  <div class="chat-typing">
                    <span></span><span></span><span></span>
                  </div>
                </div>
              </div>

              <!-- Error -->
              <div v-if="chatStore.error" class="chat-error">
                {{ chatStore.error }}
              </div>
            </div>
          </div>

          <!-- Input -->
          <div v-if="chatStore.currentSessionId" class="chat-input-area">
            <textarea
              v-model="inputText"
              class="chat-input"
              placeholder="輸入你的問題..."
              rows="1"
              :disabled="chatStore.isStreaming"
              @keydown="handleKeydown"
            />
            <button
              class="chat-send-btn"
              :disabled="!inputText.trim() || chatStore.isStreaming"
              @click="handleSend"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 2L11 13M22 2l-7 20-4-9-9-4 20-7z"/></svg>
            </button>
          </div>
        </aside>
      </div>
    </Transition>
  </Teleport>
</template>

<script lang="ts">
function renderMarkdown(text: string): string {
  if (!text) return ''
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
    .replace(/\*(.*?)\*/g, '<em>$1</em>')
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\n/g, '<br>')
}
</script>

<style scoped>
.chat-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  background: rgba(0, 0, 0, 0.3);
  backdrop-filter: blur(2px);
}

.chat-sidebar {
  position: fixed;
  top: 0;
  right: 0;
  width: 420px;
  max-width: 100vw;
  height: 100vh;
  background: #ffffff;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  font-family: var(--kimi-font-body, -apple-system, sans-serif);
}

.chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--kimi-border-light, #e5e5e5);
}

.chat-header-left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.chat-logo {
  display: inline-grid;
  width: 28px;
  height: 28px;
  place-items: center;
  background: #000;
  color: #fff;
  font-size: 10px;
  font-weight: 700;
}

.chat-title {
  font-size: 15px;
  font-weight: 600;
  color: #111;
}

.chat-close-btn {
  background: none;
  border: none;
  cursor: pointer;
  padding: 4px;
  color: #999;
  transition: color 0.2s;
}
.chat-close-btn:hover { color: #333; }

.chat-sessions {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 16px;
  overflow-y: auto;
}

.chat-new-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 12px 16px;
  background: #f5f5f5;
  border: 1px solid #e5e5e5;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  color: #333;
  transition: all 0.2s;
  font-family: inherit;
}
.chat-new-btn:hover { background: #eee; border-color: #ccc; }

.session-list {
  margin-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.session-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  cursor: pointer;
  border-radius: 6px;
  transition: background 0.15s;
}
.session-item:hover { background: #f5f5f5; }

.session-title {
  font-size: 13px;
  color: #333;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 280px;
}

.session-time {
  font-size: 11px;
  color: #999;
  flex-shrink: 0;
}

.session-empty {
  text-align: center;
  color: #999;
  font-size: 13px;
  padding: 32px 0;
}

.chat-messages {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}

.chat-messages-inner {
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.chat-back-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: none;
  border: none;
  cursor: pointer;
  font-size: 13px;
  color: #999;
  padding: 4px 0;
  margin-bottom: 8px;
  font-family: inherit;
}
.chat-back-btn:hover { color: #333; }

.chat-msg {
  display: flex;
  gap: 10px;
  max-width: 90%;
}

.chat-msg--user {
  align-self: flex-end;
  flex-direction: row-reverse;
}

.chat-msg--assistant {
  align-self: flex-start;
}

.chat-msg-avatar {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  background: #000;
  color: #fff;
  display: grid;
  place-items: center;
  font-size: 10px;
  font-weight: 700;
  flex-shrink: 0;
}

.chat-msg-bubble {
  padding: 10px 14px;
  border-radius: 12px;
  font-size: 14px;
  line-height: 1.6;
  color: #333;
}

.chat-msg--user .chat-msg-bubble {
  background: #000;
  color: #fff;
  border-bottom-right-radius: 4px;
}

.chat-msg--assistant .chat-msg-bubble {
  background: #f5f5f5;
  border-bottom-left-radius: 4px;
}

.chat-msg-content {
  word-break: break-word;
}

.chat-msg-content :deep(code) {
  background: rgba(0,0,0,0.06);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 13px;
}

.chat-msg--user .chat-msg-content :deep(code) {
  background: rgba(255,255,255,0.2);
}

.chat-cursor {
  animation: blink 1s step-end infinite;
  font-weight: 300;
}

@keyframes blink {
  50% { opacity: 0; }
}

.chat-typing {
  display: flex;
  gap: 4px;
  padding: 4px 0;
}
.chat-typing span {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #999;
  animation: typing 1.4s infinite ease-in-out;
}
.chat-typing span:nth-child(2) { animation-delay: 0.2s; }
.chat-typing span:nth-child(3) { animation-delay: 0.4s; }

@keyframes typing {
  0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
  40% { transform: scale(1); opacity: 1; }
}

.chat-tool {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  background: #f0f7ff;
  border-radius: 6px;
  font-size: 12px;
  color: #3b82f6;
  align-self: flex-start;
}

.chat-tool-icon {
  display: flex;
  align-items: center;
}

.spin {
  animation: spin 1s linear infinite;
}
@keyframes spin {
  to { transform: rotate(360deg); }
}

.chat-error {
  padding: 8px 12px;
  background: #fef2f2;
  color: #dc2626;
  border-radius: 6px;
  font-size: 13px;
  align-self: center;
}

.chat-input-area {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding: 16px 20px;
  border-top: 1px solid var(--kimi-border-light, #e5e5e5);
}

.chat-input {
  flex: 1;
  padding: 10px 14px;
  border: 1px solid #e5e5e5;
  border-radius: 10px;
  font-size: 14px;
  font-family: inherit;
  resize: none;
  outline: none;
  max-height: 120px;
  line-height: 1.5;
  color: #333;
  transition: border-color 0.2s;
}
.chat-input:focus { border-color: #999; }
.chat-input:disabled { opacity: 0.6; }

.chat-send-btn {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  background: #000;
  color: #fff;
  border: none;
  cursor: pointer;
  display: grid;
  place-items: center;
  flex-shrink: 0;
  transition: opacity 0.2s;
}
.chat-send-btn:disabled { opacity: 0.3; cursor: not-allowed; }
.chat-send-btn:not(:disabled):hover { background: #333; }

.conversation-run-card { display: grid; gap: 9px; margin-top: 12px; padding: 12px; border: 1px solid #dedede; border-radius: 8px; background: #fafafa; color: #222; }
.run-card-head { display: flex; justify-content: space-between; gap: 10px; align-items: center; }
.run-card-head strong { font-size: 12px; }
.run-status { font-size: 10px; color: #666; text-transform: uppercase; }
.run-status--succeeded { color: #16803d; }.run-status--failed { color: #b42318; }
.conversation-run-card p, .conversation-run-card small { margin: 0; color: #666; font-size: 11px; }
.run-progress { height: 4px; overflow: hidden; border-radius: 999px; background: #e5e5e5; }
.run-progress span { display: block; height: 100%; background: #c9a86a; transition: width .25s ease; }
.run-answer { max-height: 240px; overflow: auto; padding-top: 8px; border-top: 1px solid #e5e5e5; font-size: 12px; line-height: 1.6; }
.conversation-run-card a { color: #85651d; font-size: 11px; }

/* Transition */
.chat-sidebar-enter-active,
.chat-sidebar-leave-active {
  transition: opacity 0.25s ease;
}
.chat-sidebar-enter-active .chat-sidebar,
.chat-sidebar-leave-active .chat-sidebar {
  transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1);
}
.chat-sidebar-enter-from,
.chat-sidebar-leave-to {
  opacity: 0;
}
.chat-sidebar-enter-from .chat-sidebar,
.chat-sidebar-leave-to .chat-sidebar {
  transform: translateX(100%);
}
</style>
