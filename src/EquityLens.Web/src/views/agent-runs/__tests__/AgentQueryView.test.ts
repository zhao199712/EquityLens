import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentQueryView from '../AgentQueryView.vue'

const chat = reactive({
  sessions: [] as Array<{ id: string; title: string | null; messageCount: number }>,
  currentSessionId: null as string | null,
  messages: [] as Array<Record<string, unknown>>,
  isStreaming: false,
  streamingContent: '',
  error: null as string | null,
  loadSessions: vi.fn(async () => undefined),
  startNewSession: vi.fn(async () => { chat.currentSessionId = 'session-1' }),
  selectSession: vi.fn(async () => undefined),
  sendMessage: vi.fn(async () => undefined),
})

vi.mock('../../../stores/chat', () => ({ useChatStore: () => chat }))

describe('AgentQueryView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    chat.currentSessionId = 'session-1'
    chat.messages = []
    chat.isStreaming = false
    chat.error = null
  })

  it('uses the shared conversation session on mount', async () => {
    mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(chat.loadSessions).toHaveBeenCalled()
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

  it('renders a linked agent run card', async () => {
    chat.messages = [{
      id: 'm1', role: 'assistant', content: '已建立研究。',
      runCard: { agentRunId: 'run-1', workflowType: 'ResearchInvestigation', status: 'Running', currentStageDisplayName: '檢索本地證據', completedNodes: 2, totalNodes: 8, finalAnswer: null, errorMessage: null },
    }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.text()).toContain('ResearchInvestigation')
    expect(wrapper.text()).toContain('檢索本地證據')
  })
})
