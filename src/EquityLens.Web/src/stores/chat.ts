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
  isLoadingMessages: boolean
  viewVersion: number
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
    isLoadingMessages: false,
    viewVersion: 0,
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
    resetView(sessionId: string | null = null) {
      this.viewVersion++
      this.currentSessionId = sessionId
      this.messages = []
      this.isLoadingMessages = false
      this.isStreaming = false
      this.streamingContent = ''
      this.toolExecutions = []
      this.error = null
      this.conversationAction = null
    },

    clear() {
      this.resetView()
      this.sessions = []
    },

    async loadSessions() {
      const version = this.viewVersion
      try {
        const sessions = await listSessions()
        if (this.viewVersion !== version) return
        this.sessions = sessions
        if (this.currentSessionId && !this.sessions.some((s) => s.id === this.currentSessionId)) {
          this.resetView()
        }
      } catch {
        if (this.viewVersion === version) this.error = 'Failed to load sessions'
      }
    },

    async selectSession(sessionId: string) {
      this.resetView(sessionId)
      const version = this.viewVersion
      this.isLoadingMessages = true
      try {
        const messages = await getMessages(sessionId)
        if (this.viewVersion !== version) return
        this.messages = messages
        for (const message of this.messages) {
          if (message.runCard && !['Succeeded', 'Failed', 'Cancelled'].includes(message.runCard.status)) {
            this.pollRunCard(message.id, message.runCard.agentRunId)
          }
        }
      } catch {
        if (this.viewVersion === version) this.error = 'Failed to load messages'
      } finally {
        if (this.viewVersion === version) this.isLoadingMessages = false
      }
    },

    async startNewSession() {
      if (this.isStreaming) return
      this.resetView()
    },

    async sendMessage(content: string) {
      const authStore = useAuthStore()
      if (!authStore.token || !content.trim() || this.isStreaming || this.isLoadingMessages) return
      const version = this.viewVersion
      let finished = false
      const isCurrent = () => this.viewVersion === version && !finished
      this.isStreaming = true

      if (!this.currentSessionId) {
        try {
          const existing = this.sessions.find(session => session.messageCount === 0)
          const session = existing ?? await createSession()
          if (!isCurrent()) return
          if (!existing) this.sessions.unshift(session)
          this.currentSessionId = session.id
        } catch {
          if (!isCurrent()) return
          this.error = 'Failed to create session'
          this.isStreaming = false
          return
        }
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
      for (const message of this.messages) {
        if (message.runCard) message.runCard.suggestedActions = []
        message.choices = null
      }
      this.messages.push(userMsg)

      this.isStreaming = true
      this.streamingContent = ''
      this.toolExecutions = []
      this.error = null
      this.conversationAction = null
      let createdMessageId: string | null = null
      let createdRunCard: ConversationRunCard | null = null
      let createdChoices: string[] | null = null

      sendMessageStream(
        sessionId,
        content,
        authStore.token,
        (delta) => {
          if (!isCurrent()) return
          this.streamingContent += delta
        },
        (tool, args) => {
          if (!isCurrent()) return
          this.toolExecutions.push({ tool, args })
        },
        (tool, preview) => {
          if (!isCurrent()) return
          const last = this.toolExecutions[this.toolExecutions.length - 1]
          if (last && last.tool === tool) {
            last.preview = preview
          }
        },
        (action) => {
          if (!isCurrent()) return
          this.conversationAction = action
        },
        (messageId, runCard) => {
          if (!isCurrent()) return
          createdMessageId = messageId
          createdRunCard = runCard
        },
        (_model, _promptTokens, _completionTokens) => {
          if (!isCurrent()) return
          finished = true
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
            choices: createdChoices,
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
          if (!isCurrent()) return
          finished = true
          this.error = message
          this.isStreaming = false
        },
        (_messageId, choices) => {
          if (!isCurrent()) return
          createdChoices = choices.length ? choices : null
        },
      )
    },

    async pollRunCard(messageId: string, agentRunId: string) {
      const sessionId = this.currentSessionId
      const version = this.viewVersion
      if (!sessionId) return
      for (let attempt = 0; attempt < 90; attempt++) {
        await new Promise(resolve => setTimeout(resolve, 2000))
        if (this.currentSessionId !== sessionId || this.viewVersion !== version) return
        try {
          const card = await getRunCard(sessionId, agentRunId)
          if (this.currentSessionId !== sessionId || this.viewVersion !== version) return
          const message = this.messages.find(item => item.id === messageId)
          if (message) message.runCard = message === this.messages.at(-1) ? card : { ...card, suggestedActions: [] }
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
          this.resetView()
        }
      } catch {
        this.error = 'Failed to delete session'
      }
    },
  },
})
