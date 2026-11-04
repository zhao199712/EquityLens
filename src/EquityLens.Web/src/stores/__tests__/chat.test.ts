import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
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

  afterEach(() => { vi.useRealTimers() })

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
  function deferred<T>() {
    let resolve!: (value: T) => void
    const promise = new Promise<T>(r => { resolve = r })
    return { promise, resolve }
  }
  function message(id = 'message'): chatService.ChatMessage {
    return { id, role: 'assistant', content: '完成', toolName: null, sequenceNumber: 1, createdAtUtc: '', messageType: 'AgentRun', agentRunId: 'run', runCard: {
      agentRunId: 'run', researchRunId: null, workflowType: 'PortfolioDiagnosis', status: 'Succeeded', currentStage: null,
      currentStageDisplayName: null, completedNodes: 5, totalNodes: 5, finalAnswer: '結果', errorMessage: null,
      suggestedActions: [{ id: 'risk', label: '風險', prompt: '高beta的風險' }],
    } }
  }

  it('clears old messages immediately and ignores a late session load', async () => {
    const old = deferred<chatService.ChatMessage[]>()
    mocked.getMessages.mockReturnValueOnce(old.promise).mockResolvedValueOnce([message('new')])
    const store = useChatStore()
    store.currentSessionId = 'a'; store.messages = [message()]
    const loading = store.selectSession('a')
    expect(store.messages).toEqual([])
    expect(store.isLoadingMessages).toBe(true)
    await store.selectSession('b')
    old.resolve([message('old')]); await loading
    expect(store.messages.map(m => m.id)).toEqual(['new'])
    expect(store.currentSessionId).toBe('b')
  })

  it('rejects late polling even after switching away and back to the same session', async () => {
    vi.useFakeTimers()
    const late = deferred<chatService.ConversationRunCard>()
    mocked.getRunCard.mockReturnValueOnce(late.promise)
    const store = useChatStore()
    store.currentSessionId = 'a'; store.messages = [message()]
    const poll = store.pollRunCard('message', 'run')
    await vi.advanceTimersByTimeAsync(2000)
    mocked.getMessages.mockResolvedValueOnce([]).mockResolvedValueOnce([{ ...message(), runCard: { ...message().runCard!, suggestedActions: [] } }])
    await store.selectSession('b'); await store.selectSession('a')
    late.resolve(message().runCard!); await poll
    expect(store.messages[0].runCard?.suggestedActions).toEqual([])
  })

  it('reset invalidates in-flight polls and streams and clears old actions', async () => {
    vi.useFakeTimers()
    useAuthStore().token = 'test'
    const late = deferred<chatService.ConversationRunCard>()
    mocked.getRunCard.mockReturnValueOnce(late.promise)
    const store = useChatStore()
    store.currentSessionId = 'a'; store.messages = [message()]
    const poll = store.pollRunCard('message', 'run')
    await vi.advanceTimersByTimeAsync(2000)
    await store.sendMessage('重置對話')
    expect(store.messages[0].runCard?.suggestedActions).toEqual([])
    late.resolve(message().runCard!); await poll
    expect(store.messages[0].runCard?.suggestedActions).toEqual([])
    const callbacks = mocked.sendMessageStream.mock.calls[0]!
    mocked.getMessages.mockResolvedValueOnce([])
    await store.selectSession('b')
    callbacks[3]('late text'); callbacks[7]('late', message().runCard!); callbacks[8]('test', 0, 0)
    expect(store.messages).toEqual([])
    expect(store.streamingContent).toBe('')
    expect(store.currentSessionId).toBe('b')
  })

  it('does not revive actions after a reset response is reloaded', async () => {
    const old = message(); old.runCard!.suggestedActions = []
    mocked.getMessages.mockResolvedValueOnce([old, { ...message('reset'), content: '已重置對話', runCard: null, agentRunId: null, messageType: 'Text' }])
    await useChatStore().selectSession('a')
    expect(useChatStore().messages.at(-1)?.runCard).toBeNull()
    expect(useChatStore().messages[0].runCard?.suggestedActions).toEqual([])
  })

  it('ignores a late session list after clearing the store', async () => {
    const late = deferred<chatService.ChatSession[]>()
    mocked.listSessions.mockReturnValueOnce(late.promise)
    const store = useChatStore()
    const loading = store.loadSessions()
    store.clear()
    late.resolve([session('old', 2)]); await loading
    expect(store.sessions).toEqual([])
  })

  it('keeps older run progress updating while a new message hides its suggestions', async () => {
    vi.useFakeTimers()
    useAuthStore().token = 'test'
    const late = deferred<chatService.ConversationRunCard>()
    mocked.getRunCard.mockReturnValueOnce(late.promise)
    const store = useChatStore()
    store.currentSessionId = 'a'; store.messages = [message()]
    store.messages[0].runCard!.status = 'Running'
    const poll = store.pollRunCard('message', 'run')
    await vi.advanceTimersByTimeAsync(2000)
    await store.sendMessage('下一個問題')
    late.resolve(message().runCard!); await poll
    expect(store.messages[0].runCard?.status).toBe('Succeeded')
    expect(store.messages[0].runCard?.suggestedActions).toEqual([])
    const callbacks = mocked.sendMessageStream.mock.calls[0]!
    callbacks[3]('新回覆'); callbacks[8]('test', 0, 0)
    const count = store.messages.length
    callbacks[7]('old', message().runCard!); callbacks[8]('test', 0, 0)
    expect(store.messages).toHaveLength(count)
    expect(store.messages.at(-1)?.runCard).toBeNull()
  })

})
