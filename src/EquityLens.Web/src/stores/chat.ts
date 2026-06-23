import { defineStore } from 'pinia'
import {
  createSession,
  listSessions,
  getMessages,
  deleteSession,
  sendMessageStream,
  type ChatSession,
  type ChatMessage,
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
  isOpen: boolean
  sessions: ChatSession[]
  currentSessionId: string | null
  messages: ChatMessage[]
  isStreaming: boolean
  streamingContent: string
  toolExecutions: ToolExecution[]
  error: string | null
}

export const useChatStore = defineStore('chat', {
  state: (): ChatState => ({
    isOpen: false,
    sessions: [],
    currentSessionId: null,
    messages: [],
    isStreaming: false,
    streamingContent: '',
    toolExecutions: [],
    error: null,
  }),

  getters: {
    currentMessages: (state) => state.messages,
    hasMessages: (state) => state.messages.length > 0 || state.isStreaming,
  },

  actions: {
    toggleSidebar() {
      this.isOpen = !this.isOpen
      if (this.isOpen && this.sessions.length === 0) {
        this.loadSessions()
      }
    },

    openSidebar() {
      this.isOpen = true
      if (this.sessions.length === 0) {
        this.loadSessions()
      }
    },

    closeSidebar() {
      this.isOpen = false
    },

    async loadSessions() {
      try {
        this.sessions = await listSessions()
      } catch {
        this.error = 'Failed to load sessions'
      }
    },

    async selectSession(sessionId: string) {
      this.currentSessionId = sessionId
      try {
        this.messages = await getMessages(sessionId)
      } catch {
        this.error = 'Failed to load messages'
      }
    },

    async startNewSession() {
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
      }
      this.messages.push(userMsg)

      this.isStreaming = true
      this.streamingContent = ''
      this.toolExecutions = []
      this.error = null

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
        (_model, _promptTokens, _completionTokens) => {
          const assistantMsg: ChatMessage = {
            id: generateId(),
            role: 'assistant',
            content: this.streamingContent,
            toolName: null,
            sequenceNumber: this.messages.length,
            createdAtUtc: new Date().toISOString(),
          }
          this.messages.push(assistantMsg)
          this.isStreaming = false
          this.streamingContent = ''
          this.toolExecutions = []

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
