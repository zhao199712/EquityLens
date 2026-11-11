import { legacyWorkflow } from '../../../services/legacyWorkflows'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentQueryView from '../AgentQueryView.vue'
import i18n from '../../../locales'

const chat = reactive({
  sessions: [] as Array<{ id: string; title: string | null; messageCount: number; updatedAtUtc: string }>,
  currentSessionId: null as string | null,
  messages: [] as Array<Record<string, unknown>>,
  isStreaming: false,
  isLoadingMessages: false,
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
    i18n.global.locale.value = 'zh-TW'
    chat.isLoadingMessages = false
    chat.sessions = []
    chat.currentSessionId = 'session-1'
    chat.messages = []
    chat.isStreaming = false
    chat.streamingContent = ''
    chat.error = null
  })

  it('uses an AgentLoop card without presenting physical nodes as stages or publishing a draft', async () => {
    chat.messages = [{ id: 'message', role: 'assistant', content: '處理中', runCard: {
      agentRunId: 'run', workflowType: 'ResearchAgent', status: 'Running', completedNodes: 8, totalNodes: 16,
      currentStageDisplayName: '規劃', finalAnswer: '暫存草稿',
    } }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.text()).toContain('正在檢索文件並整理答案')
    expect(wrapper.text()).not.toContain('8 / 16')
    expect(wrapper.find('.progress').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('暫存草稿')
    expect(wrapper.text()).not.toContain('目前階段：規劃')
    const card = chat.messages[0].runCard as Record<string, unknown>
    card.status = 'Succeeded'; card.finalAnswer = '最終答案'
    await flushPromises()
    expect(wrapper.text()).toContain('最終答案')
  })

  const suggestions = [
    { id: 'volatility', label: '如何降低波動率？', prompt: '接著，針對投資組合「高beta」，如何降低波動率？', labelEn: 'How can I reduce volatility?', promptEn: 'For portfolio “高beta”, how could volatility be reduced?' },
    { id: 'contributors', label: '哪些持股貢獻最多風險？', prompt: '接著，針對投資組合「高beta」，哪些持股貢獻最多風險？' },
  ]
  function completedMessage() {
    return { id: 'completed', role: 'assistant', content: '分析完成', runCard: {
      agentRunId: 'run', workflowType: 'PortfolioDiagnosis', status: 'Succeeded', finalAnswer: '風險分析結果',
      completedNodes: 5, totalNodes: 5, suggestedActions: suggestions,
    } }
  }

  it('fills and focuses a suggestion without sending, then sends the reviewed text with IME protection', async () => {
    chat.messages = [completedMessage()]
    const wrapper = mount(AgentQueryView, { attachTo: document.body, global: { stubs: { RouterLink: true } } })
    await wrapper.get('.suggested-action-btn').trigger('click')
    await flushPromises()
    const composer = wrapper.get('textarea')
    expect((composer.element as HTMLTextAreaElement).value).toBe(suggestions[0].prompt)
    expect(document.activeElement).toBe(composer.element)
    expect(chat.sendMessage).not.toHaveBeenCalled()
    expect(chat.startNewSession).not.toHaveBeenCalled()
    expect(chat.selectSession).not.toHaveBeenCalled()
    await composer.setValue(suggestions[0].prompt.replace('高beta', '退休'))
    await composer.trigger('keydown', { key: 'Enter', isComposing: true })
    await composer.trigger('keydown', { key: 'Enter', keyCode: 229 })
    expect(chat.sendMessage).not.toHaveBeenCalled()
    await composer.trigger('keydown', { key: 'Enter' })
    expect(chat.sendMessage).toHaveBeenCalledExactlyOnceWith(suggestions[0].prompt.replace('高beta', '退休'))
    wrapper.unmount()
  })

  it('disables suggestions while any draft exists, including whitespace', async () => {
    chat.messages = [completedMessage()]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    for (const draft of ['我的草稿', ' ']) {
      await wrapper.get('textarea').setValue(draft)
      expect(wrapper.get('.suggested-action-btn').attributes('disabled')).toBeDefined()
      await wrapper.get('.suggested-action-btn').trigger('click')
      expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe(draft)
    }
    expect(chat.sendMessage).not.toHaveBeenCalled()
    await wrapper.get('textarea').setValue('')
    expect(wrapper.get('.suggested-action-btn').attributes('disabled')).toBeUndefined()
  })

  it('shows localized labels and fills the complete English prompt', async () => {
    i18n.global.locale.value = 'en'
    chat.messages = [completedMessage()]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.get('[aria-label="Suggested follow-ups"]').text()).toContain('How can I reduce volatility?')
    await wrapper.get('.suggested-action-btn').trigger('click')
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe(suggestions[0].promptEn)
    expect(chat.sendMessage).not.toHaveBeenCalled()
  })

  it.each(['Pending', 'Running', 'WaitingForApproval', 'WaitingForFeedback', 'Failed', 'Cancelled'])('hides suggestions for %s', (status) => {
    const message = completedMessage(); message.runCard.status = status; chat.messages = [message]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.find('.suggested-actions').exists()).toBe(false)
  })

  it('only shows the latest reply, including after completion, loading or reset', async () => {
    chat.messages = [completedMessage()]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.findAll('.suggested-actions')).toHaveLength(1)
    chat.isLoadingMessages = true
    await flushPromises()
    expect(wrapper.find('.suggested-actions').exists()).toBe(false)
    chat.isLoadingMessages = false
    chat.messages.push({ id: 'next', role: 'user', content: '重置對話' })
    await flushPromises()
    expect(wrapper.find('.suggested-actions').exists()).toBe(false)
    chat.messages.push({ id: 'reset', role: 'assistant', content: '已重置對話' })
    await flushPromises()
    expect(wrapper.find('.suggested-actions').exists()).toBe(false)
    chat.messages = [completedMessage(), { ...completedMessage(), id: 'new' }]
    await flushPromises()
    expect(wrapper.findAll('.suggested-actions')).toHaveLength(1)
    chat.isStreaming = true
    await flushPromises()
    expect(wrapper.find('.suggested-actions').exists()).toBe(false)
  })

  it('treats an absent API field as empty and suppresses quality-limited results', async () => {
    chat.messages = [completedMessage()]
    const card = chat.messages[0].runCard as Record<string, unknown>
    card.suggestedActions = undefined
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.find('.suggested-actions').exists()).toBe(false)
    card.suggestedActions = suggestions
    card.qualityIssues = ['unverified_number']
    await flushPromises()
    expect(wrapper.find('.suggested-actions').exists()).toBe(false)
  })

  it('uses the shared conversation session on mount', async () => {
    mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(chat.loadSessions).toHaveBeenCalled()
  })

  it('keeps an unsaved draft when none is selected', async () => {
    chat.currentSessionId = null
    mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(chat.startNewSession).not.toHaveBeenCalled()
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
    expect(examples.length).toBe(2)
    expect(wrapper.text()).not.toContain('聯發科')
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
      runCard: { agentRunId: 'run-1', workflowType: legacyWorkflow.research, status: 'Running', currentStageDisplayName: '檢索本地證據', completedNodes: 2, totalNodes: 8, finalAnswer: null, errorMessage: null },
    }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const card = wrapper.get('.run-card')
    expect(card.text()).toContain(legacyWorkflow.research)
    expect(card.text()).toContain('檢索本地證據')
    expect(card.get('.run-status').classes()).toContain('run-status--running')
  })

  it('renders the final answer in a succeeded run card', async () => {
    chat.messages = [{
      id: 'm1', role: 'assistant', content: '研究完成。',
      runCard: { agentRunId: 'run-1', workflowType: legacyWorkflow.research, status: 'Succeeded', currentStageDisplayName: null, completedNodes: 8, totalNodes: 8, finalAnswer: '**結論**：基本面穩健。', errorMessage: null },
    }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const answer = wrapper.get('.final-answer')
    expect(answer.html()).toContain('<strong>結論</strong>')
    expect(wrapper.get('.run-status').classes()).toContain('run-status--succeeded')
  })

  it('does not send when Enter only confirms an IME candidate', async () => {
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const composer = wrapper.find('textarea')
    await composer.setValue('台積電')
    await composer.trigger('keydown', { key: 'Enter', isComposing: true })
    await composer.trigger('keydown', { key: 'Enter', keyCode: 229 })
    expect(chat.sendMessage).not.toHaveBeenCalled()
    await composer.trigger('keydown', { key: 'Enter' })
    expect(chat.sendMessage).toHaveBeenCalledWith('台積電')
  })

  it('offers portfolio choices as buttons that send the chosen name', async () => {
    chat.currentSessionId = 'session'
    chat.messages = [{ id: 'ask', role: 'assistant', content: '請選擇投資組合', choices: ['穩健金融', '高波動科技'], runCard: null }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const buttons = wrapper.findAll('.suggested-action-btn')
    expect(buttons.map(b => b.text())).toEqual(['1. 穩健金融', '2. 高波動科技'])
    await buttons[1]!.trigger('click')
    expect(chat.sendMessage).toHaveBeenCalledWith('高波動科技')
  })

  it('shows quality limits and missing documents in plain language instead of internal codes', () => {
    chat.messages = [
      { id: 'a', role: 'assistant', content: '', runCard: { agentRunId: 'r1', workflowType: 'PortfolioDiagnosis', status: 'Succeeded',
        completedNodes: 4, totalNodes: 4, finalAnswer: '| 指標 | 值 |\n|---|---:|\n| 波動 | 56.73% |', qualityIssues: ['UntraceableNumbers'] } },
      { id: 'b', role: 'assistant', content: '', runCard: { agentRunId: 'r2', workflowType: 'ResearchAgent', status: 'Failed',
        completedNodes: 1, totalNodes: 1, errorMessage: '此股票尚未匯入可供研究問答使用的年報或法說會文件。' } },
    ]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    const text = wrapper.text()
    expect(text).toContain('投組診斷')
    expect(text).toContain('部分數字無法對應工具資料')
    expect(text).not.toContain('UntraceableNumbers')
    expect(text).toContain('資料不足')
    expect(text).not.toContain('FAILED')
    expect(wrapper.find('table.md-table').exists()).toBe(true)
  })

  it('folds method notes and fixed notices out of portfolio answers', () => {
    chat.messages = [{ id: 'p', role: 'assistant', content: '', runCard: { agentRunId: 'r', workflowType: 'PortfolioDiagnosis', status: 'Succeeded',
      completedNodes: 4, totalNodes: 4, qualityIssues: [],
      finalAnswer: '年化波動 53.39% [P3]。\n\n波動採樣本標準差年化；高於產品參考上限 18.00% [P3]。\n\n工具唯讀，不代表持倉已調整；不保證獲利。' } }]
    const wrapper = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } })
    expect(wrapper.find('.final-answer').text()).toContain('53.39%')
    expect(wrapper.find('.final-answer').text()).not.toContain('參考上限')
    expect(wrapper.find('details.answer-notes summary').text()).toBe('計算方式與限制')
    expect(wrapper.find('.answer-footer').text()).toBe('工具唯讀，不代表持倉已調整；不保證獲利。')
  })

  it('shows key metrics from tool output even when the answer has no table', () => {
    chat.messages = [{ id: 'k', role: 'assistant', content: '', runCard: { agentRunId: 'r', workflowType: 'PortfolioDiagnosis', status: 'Succeeded',
      completedNodes: 4, totalNodes: 4, qualityIssues: [], finalAnswer: '集中風險明顯 [P1]。',
      keyMetrics: { annualizedVolatility: .5339, maxDrawdown: -.3166, historicalVaR: null, expectedShortfall: null, largestWeight: .7692,
        concentrationHhi: .645, portfolioReturn: 1.1823, benchmarkReturn: .6211 } } }]
    const text = mount(AgentQueryView, { global: { stubs: { RouterLink: true } } }).find('.key-metrics').text()
    expect(text).toContain('年化波動53.39%')
    expect(text).toContain('最大回撤-31.66%')
    expect(text).toContain('大盤 62.11%')
    expect(text).not.toContain('VaR')
  })
})
