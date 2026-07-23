import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentQueryView from '../AgentQueryView.vue'
import { createAgentWorkflowQuery } from '../../../services/agentQueries'

const push = vi.fn()
const routeMetadata = {
  leadSkill: 'research-investigation',
  leadSkillDisplayName: '一般個股研究',
  routingConfidence: 'high',
  objective: '回答投研問題',
  contextEnvelope: { market: 'TW', asset: 'equity', depth: 'standard', horizon: null, currency: 'TWD', language: 'zh-TW' },
  inferredFields: [],
  clarifyingQuestions: [],
}
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('../../../services/agentQueries', () => ({ createAgentWorkflowQuery: vi.fn() }))

describe('AgentQueryView', () => {
  beforeEach(() => vi.clearAllMocks())

  it('does not submit an empty question', async () => {
    const wrapper = mount(AgentQueryView)
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    expect(createAgentWorkflowQuery).not.toHaveBeenCalled()
  })

  it('automatically opens a portfolio agent run', async () => {
    vi.mocked(createAgentWorkflowQuery).mockResolvedValue({
      agentRunId: 'run-1', researchRunId: null, workflowType: 'PortfolioDiagnosis', status: 'Pending',
      routingReason: '問題涉及投組風險。', routingModel: 'router-model',
      ...routeMetadata, leadSkill: 'portfolio-risk-summary', leadSkillDisplayName: '組合風險摘要',
    })
    const wrapper = mount(AgentQueryView)
    await wrapper.get('textarea').setValue('我的投資組合風險如何？')
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    await flushPromises()

    expect(createAgentWorkflowQuery).toHaveBeenCalledWith('我的投資組合風險如何？')
    expect(push).toHaveBeenCalledWith({ name: 'agent-run-detail', params: { id: 'run-1' } })
  })

  it('automatically opens the research answer page', async () => {
    vi.mocked(createAgentWorkflowQuery).mockResolvedValue({
      agentRunId: 'agent-1', researchRunId: 'research-1', workflowType: 'ResearchInvestigation', status: 'Pending',
      routingReason: '問題涉及公司研究。', routingModel: 'router-model',
      ...routeMetadata,
    })
    const wrapper = mount(AgentQueryView)
    await wrapper.get('textarea').setValue('聯發科最近營運如何？')
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    await flushPromises()
    expect(push).toHaveBeenCalledWith({ name: 'research-run-detail', params: { id: 'research-1' } })
  })

  it('shows a backend clarification message', async () => {
    vi.mocked(createAgentWorkflowQuery).mockRejectedValue({ response: { data: { message: '請在問題中提供股票代號。' } } })
    const wrapper = mount(AgentQueryView)
    await wrapper.get('textarea').setValue('分析這家公司')
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('股票代號')
  })
})
