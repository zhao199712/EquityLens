import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useChatStore } from '../chat'
import * as chatService from '../../services/chat'

vi.mock('../../services/chat', () => ({
  createSession: vi.fn(),
  listSessions: vi.fn(),
  getMessages: vi.fn(),
  deleteSession: vi.fn(),
  sendMessageStream: vi.fn(),
  getRunCard: vi.fn(),
}))

vi.mock('../../services/http', () => ({
  http: { defaults: { headers: { common: {} } } },
}))

const mocked = vi.mocked(chatService)

function session(id: string, messageCount: number) {
  return { id, title: null, createdAtUtc: '', updatedAtUtc: '', messageCount }
}

describe('chat store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  describe('startNewSession()', () => {
    it('重用既有的空對話而不建立新對話', async () => {
      const store = useChatStore()
      store.sessions = [session('s-empty', 0), session('s-used', 4)]

      await store.startNewSession()

      expect(chatService.createSession).not.toHaveBeenCalled()
      expect(store.currentSessionId).toBe('s-empty')
      expect(store.messages).toEqual([])
    })

    it('沒有空對話時建立新對話', async () => {
      mocked.createSession.mockResolvedValueOnce(session('s-new', 0))
      const store = useChatStore()
      store.sessions = [session('s-used', 4)]

      await store.startNewSession()

      expect(chatService.createSession).toHaveBeenCalled()
      expect(store.currentSessionId).toBe('s-new')
      expect(store.sessions[0].id).toBe('s-new')
    })
  })

  describe('loadSessions()', () => {
    it('目前對話不在新清單時清空選取與訊息', async () => {
      mocked.listSessions.mockResolvedValueOnce([session('s-other', 2)])
      const store = useChatStore()
      store.currentSessionId = 's-gone'
      store.messages = [{ id: 'm1' }] as never[]

      await store.loadSessions()

      expect(store.currentSessionId).toBeNull()
      expect(store.messages).toEqual([])
    })

    it('目前對話仍在清單時保留', async () => {
      mocked.listSessions.mockResolvedValueOnce([session('s-keep', 2)])
      const store = useChatStore()
      store.currentSessionId = 's-keep'

      await store.loadSessions()

      expect(store.currentSessionId).toBe('s-keep')
    })
  })

  describe('clear()', () => {
    it('重置所有狀態', () => {
      const store = useChatStore()
      store.sessions = [session('s1', 2)]
      store.currentSessionId = 's1'
      store.messages = [{ id: 'm1' }] as never[]
      store.error = 'err'

      store.clear()

      expect(store.sessions).toEqual([])
      expect(store.currentSessionId).toBeNull()
      expect(store.messages).toEqual([])
      expect(store.error).toBeNull()
    })
  })
})
