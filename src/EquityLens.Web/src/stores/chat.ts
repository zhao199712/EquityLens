import { defineStore } from 'pinia'
import {
  createSession,
  listSessions,
  getMessages,
  deleteSession,
  sendMessageStream,
  getRunCard,
  type ChatSession,
  type ChatMessage,
  type ConversationRunCard,
} from '../services/chat'
import { useAuthStore } from './auth'

function generateId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

interface ToolExecution {
  tool: string
  args: string
  preview?: string
}

interface ChatState {
  sessions: ChatSession[]
  currentSessionId: string | null
  messages: ChatMessage[]
  isStreaming: boolean
  streamingContent: string
  toolExecutions: ToolExecution[]
  error: string | null
  conversationAction: string | null
}

export const useChatStore = defineStore('chat', {
  state: (): ChatState => ({
    sessions: [],
    currentSessionId: null,
    messages: [],
    isStreaming: false,
    streamingContent: '',
    toolExecutions: [],
    error: null,
    conversationAction: null,
  }),

  getters: {
    currentMessages: (state) => state.messages,
    hasMessages: (state) => state.messages.length > 0 || state.isStreaming,
  },

  actions: {
    clear() {
      this.sessions = []
      this.currentSessionId = null
      this.messages = []
      this.isStreaming = false
      this.streamingContent = ''
      this.toolExecutions = []
      this.error = null
      this.conversationAction = null
    },

    async loadSessions() {
      try {
        this.sessions = await listSessions()
        if (this.currentSessionId && !this.sessions.some((s) => s.id === this.currentSessionId)) {
          this.currentSessionId = null
          this.messages = []
        }
      } catch {
        this.error = 'Failed to load sessions'
      }
    },

    async selectSession(sessionId: string) {
      this.currentSessionId = sessionId
      try {
        this.messages = await getMessages(sessionId)
        for (const message of this.messages) {
          if (message.runCard && !['Succeeded', 'Failed', 'Cancelled'].includes(message.runCard.status)) {
            this.pollRunCard(message.id, message.runCard.agentRunId)
          }
        }
      } catch {
        this.error = 'Failed to load messages'
      }
    },

    async startNewSession() {
      const existing = this.sessions.find((s) => s.messageCount === 0)
      if (existing) {
        this.currentSessionId = existing.id
        this.messages = []
        return
      }
      try {
        const session = await createSession()
        this.sessions.unshift(session)
        this.currentSessionId = session.id
        this.messages = []
      } catch {
        this.error = 'Failed to create session'
      }
    },

    async sendMessage(content: string) {
      const authStore = useAuthStore()
      if (!authStore.token) return

      if (!this.currentSessionId) {
        await this.startNewSession()
      }

      const sessionId = this.currentSessionId!
      const userMsg: ChatMessage = {
        id: generateId(),
        role: 'user',
        content,
        toolName: null,
        sequenceNumber: this.messages.length,
        createdAtUtc: new Date().toISOString(),
        messageType: 'Text',
        agentRunId: null,
        runCard: null,
      }
      this.messages.push(userMsg)

      this.isStreaming = true
      this.streamingContent = ''
      this.toolExecutions = []
      this.error = null
      this.conversationAction = null
      let createdMessageId: string | null = null
      let createdRunCard: ConversationRunCard | null = null

      sendMessageStream(
        sessionId,
        content,
        authStore.token,
        (delta) => {
          this.streamingContent += delta
        },
        (tool, args) => {
          this.toolExecutions.push({ tool, args })
        },
        (tool, preview) => {
          const last = this.toolExecutions[this.toolExecutions.length - 1]
          if (last && last.tool === tool) {
            last.preview = preview
          }
        },
        (action) => {
          this.conversationAction = action
        },
        (messageId, runCard) => {
          createdMessageId = messageId
          createdRunCard = runCard
        },
        (_model, _promptTokens, _completionTokens) => {
          const assistantMsg: ChatMessage = {
            id: createdMessageId ?? generateId(),
            role: 'assistant',
            content: this.streamingContent,
            toolName: null,
            sequenceNumber: this.messages.length,
            createdAtUtc: new Date().toISOString(),
            messageType: createdRunCard ? 'AgentRun' : 'Text',
            agentRunId: createdRunCard?.agentRunId ?? null,
            runCard: createdRunCard,
          }
          this.messages.push(assistantMsg)
          this.isStreaming = false
          this.streamingContent = ''
          this.toolExecutions = []
          this.conversationAction = null
          if (assistantMsg.runCard && !['Succeeded', 'Failed', 'Cancelled'].includes(assistantMsg.runCard.status)) {
            this.pollRunCard(assistantMsg.id, assistantMsg.runCard.agentRunId)
          }

          const session = this.sessions.find((s) => s.id === sessionId)
          if (session) {
            session.updatedAtUtc = new Date().toISOString()
            session.messageCount += 2
            if (!session.title) {
              session.title = content.length > 50 ? content.slice(0, 50) + '...' : content
            }
          }
        },
        (message) => {
          this.error = message
          this.isStreaming = false
        },
      )
    },

    async pollRunCard(messageId: string, agentRunId: string) {
      const sessionId = this.currentSessionId
      if (!sessionId) return
      for (let attempt = 0; attempt < 90; attempt++) {
        await new Promise(resolve => setTimeout(resolve, 2000))
        if (this.currentSessionId !== sessionId) return
        try {
          const card = await getRunCard(sessionId, agentRunId)
          const message = this.messages.find(item => item.id === messageId)
          if (message) message.runCard = card
          if (['Succeeded', 'Failed', 'Cancelled'].includes(card.status)) return
        } catch {
          return
        }
      }
    },

    async removeSession(sessionId: string) {
      try {
        await deleteSession(sessionId)
        this.sessions = this.sessions.filter((s) => s.id !== sessionId)
        if (this.currentSessionId === sessionId) {
          this.currentSessionId = null
          this.messages = []
        }
      } catch {
        this.error = 'Failed to delete session'
      }
    },
  },
})
