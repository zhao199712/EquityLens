import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentQueryView from '../AgentQueryView.vue'

const chat = reactive({
  sessions: [] as Array<{ id: string; title: string | null; messageCount: number; updatedAtUtc: string }>,
  currentSessionId: null as string | null,
  messages: [] as Array<Record<string, unknown>>,
  isStreaming: false,
  streamingContent: '',
  error: null as string | null,
  loadSessions: vi.fn(async () => undefined),
  startNewSession: vi.fn(async () => { chat.currentSessionId = 'session-1' }),
  selectSession: vi.fn(async () => undefined),
  sendMessage: vi.fn(async () => undefined),
  removeSession: vi.fn(async () => undefined),
})

vi.mock('../../../stores/chat', () => ({ useChatStore: () => chat }))

describe('AgentQueryView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    chat.sessions = []
    chat.currentSessionId = 'session-1'
    chat.messages = []
    chat.isStreaming = false
    chat.streamingContent = ''
    chat.error = null
  })

  it('uses the shared conversation session on mount', async () => {
    mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(chat.loadSessions).toHaveBeenCalled()
  })

  it('starts a new session when none is selected', async () => {
    chat.currentSessionId = null
    mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(chat.startNewSession).toHaveBeenCalled()
  })

  it('does not submit an empty message', async () => {
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    expect(chat.sendMessage).not.toHaveBeenCalled()
  })

  it('sends through the conversation store', async () => {
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    await wrapper.get('textarea').setValue('聯發科最近營運如何？')
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    expect(chat.sendMessage).toHaveBeenCalledWith('聯發科最近營運如何？')
  })

  it('sends an example question directly from the welcome state', async () => {
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const examples = wrapper.findAll('.example-btn')
    expect(examples.length).toBe(3)
    await examples[1].trigger('click')
    expect(chat.sendMessage).toHaveBeenCalledWith('台積電最近一季的營運表現與主要風險是什麼？')
  })

  it('renders sessions and selects one on click', async () => {
    chat.sessions = [
      { id: 'session-1', title: '台積電研究', messageCount: 4, updatedAtUtc: '2026-07-20T10:00:00Z' },
      { id: 'session-2', title: null, messageCount: 0, updatedAtUtc: '2026-07-21T10:00:00Z' },
    ]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const rows = wrapper.findAll('.session-row')
    expect(rows.length).toBe(2)
    expect(rows[0].text()).toContain('台積電研究')
    expect(rows[1].text()).toContain('新對話')
    await rows[1].trigger('click')
    expect(chat.selectSession).toHaveBeenCalledWith('session-2')
  })

  it('confirms before removing a session', async () => {
    chat.sessions = [
      { id: 'session-1', title: '台積電研究', messageCount: 4, updatedAtUtc: '2026-07-20T10:00:00Z' },
    ]
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    await wrapper.get('.session-delete').trigger('click')
    expect(confirmSpy).toHaveBeenCalled()
    expect(chat.removeSession).toHaveBeenCalledWith('session-1')
    confirmSpy.mockRestore()
  })

  it('renders assistant markdown content', async () => {
    chat.messages = [{ id: 'm1', role: 'assistant', content: '**重點**：營收創高。', runCard: null }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.get('.message-content').html()).toContain('<strong>重點</strong>')
  })

  it('renders a linked agent run card', async () => {
    chat.messages = [{
      id: 'm1', role: 'assistant', content: '已建立研究。',
      runCard: { agentRunId: 'run-1', workflowType: 'ResearchInvestigation', status: 'Running', currentStageDisplayName: '檢索本地證據', completedNodes: 2, totalNodes: 8, finalAnswer: null, errorMessage: null },
    }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const card = wrapper.get('.run-card')
    expect(card.text()).toContain('ResearchInvestigation')
    expect(card.text()).toContain('檢索本地證據')
    expect(card.get('.run-status').classes()).toContain('run-status--running')
  })

  it('renders the final answer in a succeeded run card', async () => {
    chat.messages = [{
      id: 'm1', role: 'assistant', content: '研究完成。',
      runCard: { agentRunId: 'run-1', workflowType: 'ResearchInvestigation', status: 'Succeeded', currentStageDisplayName: null, completedNodes: 8, totalNodes: 8, finalAnswer: '**結論**：基本面穩健。', errorMessage: null },
    }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const answer = wrapper.get('.final-answer')
    expect(answer.html()).toContain('<strong>結論</strong>')
    expect(wrapper.get('.run-status').classes()).toContain('run-status--succeeded')
  })
})
