import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useChatStore } from '../chat'
import { useAuthStore } from '../auth'
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
    vi.resetAllMocks()
  })

  describe('startNewSession()', () => {
    it('新對話先清空畫面，不建立或選取 session', async () => {
      const store = useChatStore()
      store.sessions = [session('s-empty', 0), session('s-used', 4)]

      await store.startNewSession()

      expect(chatService.createSession).not.toHaveBeenCalled()
      expect(store.currentSessionId).toBeNull()
      expect(store.messages).toEqual([])
    })

    it('沒有空對話時也只建立本機草稿', async () => {
      const store = useChatStore()
      store.sessions = [session('s-used', 4)]

      await store.startNewSession()

      expect(chatService.createSession).not.toHaveBeenCalled()
      expect(store.currentSessionId).toBeNull()
      expect(store.sessions[0].id).toBe('s-used')
    })
  })

  describe('first message', () => {
    it('creates a session only on send and prevents duplicate creation while pending', async () => {
      useAuthStore().token = 'test-token'
      let resolve!: (value: ReturnType<typeof session>) => void
      mocked.createSession.mockImplementationOnce(() => new Promise(r => { resolve = r }))
      const store = useChatStore()
      const first = store.sendMessage('你好')
      await store.sendMessage('重複送出')
      expect(mocked.createSession).toHaveBeenCalledTimes(1)
      expect(mocked.sendMessageStream).not.toHaveBeenCalled()
      resolve(session('s-new', 0))
      await first
      expect(store.currentSessionId).toBe('s-new')
      expect(mocked.sendMessageStream.mock.calls[0]?.slice(0, 2)).toEqual(['s-new', '你好'])
    })

    it('reuses a previously empty session on first send', async () => {
      useAuthStore().token = 'test-token'
      const store = useChatStore()
      store.sessions = [session('s-empty', 0)]
      await store.sendMessage('你好')
      expect(mocked.createSession).not.toHaveBeenCalled()
      expect(store.currentSessionId).toBe('s-empty')
    })

    it('does not send to a missing session when creation fails', async () => {
      useAuthStore().token = 'test-token'
      mocked.createSession.mockRejectedValueOnce(new Error('unavailable'))
      const store = useChatStore()
      await store.sendMessage('你好')
      expect(mocked.sendMessageStream).not.toHaveBeenCalled()
      expect(store.isStreaming).toBe(false)
      expect(store.error).toBe('Failed to create session')
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
