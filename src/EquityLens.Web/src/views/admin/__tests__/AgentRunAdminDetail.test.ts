import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'

const push = vi.fn()
const message = { success: vi.fn(), error: vi.fn() }

vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@vicons/ionicons5', () => ({ CheckmarkCircleOutline: {}, CloseCircleOutline: {}, TimeOutline: {}, AlertCircleOutline: {}, ReloadOutline: {}, CloseOutline: {} }))
vi.mock('naive-ui', () => ({
  NSpin: defineComponent({ props: ['show'], template: '<div><slot /></div>' }),
  NTag: defineComponent({ template: '<span><slot /></span>' }),
  NSpace: defineComponent({ template: '<div><slot /></div>' }),
  NIcon: defineComponent({ template: '<span><slot /></span>' }),
  NTimeline: defineComponent({ template: '<div><slot /></div>' }),
  NTimelineItem: defineComponent({ template: '<div><slot /></div>' }),
  useMessage: () => message,
}))
vi.mock('../../../services/agentRuns', () => ({ getAgentRun: vi.fn(), retryAgentRun: vi.fn(), cancelAgentRun: vi.fn(), createEvidenceRemediation: vi.fn(), createEvidenceReanalysis: vi.fn(), decideAgentApproval: vi.fn() }))

import AgentRunAdminDetail from '../components/AgentRunAdminDetail.vue'
import { createEvidenceRemediation, createEvidenceReanalysis, getAgentRun } from '../../../services/agentRuns'

describe('AgentRunAdminDetail evidence remediation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(getAgentRun).mockResolvedValue({
      run: { id: 'critic-1', workflowType: 'CriticReview', agentType: 'CriticAgent', status: 'Succeeded', errorMessage: null, createdAtUtc: new Date().toISOString(), startedAtUtc: null, completedAtUtc: null },
      nodes: [], events: [], toolCalls: [], feedback: [], approvals: [], blackboardJson: {}, outputJson: { requiresMoreEvidence: true }, workflowDefinitionJson: {},
    })
    vi.mocked(createEvidenceRemediation).mockResolvedValue({ id: 'remediation-1', workflowType: 'EvidenceRemediation', agentType: 'ResearchAgent', status: 'Pending', errorMessage: null, createdAtUtc: new Date().toISOString(), startedAtUtc: null, completedAtUtc: null })
    vi.mocked(createEvidenceReanalysis).mockResolvedValue({ id: 'reanalysis-1', workflowType: 'EvidenceReanalysis', agentType: 'AnalysisAgent', status: 'Pending', errorMessage: null, createdAtUtc: new Date().toISOString(), startedAtUtc: null, completedAtUtc: null })
  })

  it('只在 CriticReview 需要更多證據時建立流程並導頁', async () => {
    const wrapper = mount(AgentRunAdminDetail, { props: { runId: 'critic-1' } })
    await flushPromises()

    const button = wrapper.findAll('button').find(item => item.text().includes('補充證據並修訂'))
    expect(button).toBeTruthy()
    await button!.trigger('click')
    await flushPromises()

    expect(createEvidenceRemediation).toHaveBeenCalledWith('critic-1')
    expect(push).toHaveBeenCalledWith({ name: 'admin-agent-run-detail', params: { id: 'remediation-1' } })
  })

  it('只在 EvidenceRemediation 建議重新分析時建立流程並導頁', async () => {
    vi.mocked(getAgentRun).mockResolvedValue({
      run: { id: 'remediation-1', workflowType: 'EvidenceRemediation', agentType: 'ResearchAgent', status: 'Succeeded', errorMessage: null, createdAtUtc: new Date().toISOString(), startedAtUtc: null, completedAtUtc: null },
      nodes: [], events: [], toolCalls: [], feedback: [], approvals: [], blackboardJson: {}, outputJson: { requiresReanalysis: true }, workflowDefinitionJson: {},
    })
    const wrapper = mount(AgentRunAdminDetail, { props: { runId: 'remediation-1' } }); await flushPromises()
    const button = wrapper.findAll('button').find(item => item.text().includes('重新分析')); expect(button).toBeTruthy(); await button!.trigger('click'); await flushPromises()
    expect(createEvidenceReanalysis).toHaveBeenCalledWith('remediation-1'); expect(push).toHaveBeenCalledWith({ name: 'admin-agent-run-detail', params: { id: 'reanalysis-1' } })
  })

  it('顯示 ResearchQualityReview loop 預算與停止原因', async () => {
    vi.mocked(getAgentRun).mockResolvedValue({
      run: { id: 'quality-1', workflowType: 'ResearchQualityReview', agentType: 'CriticAgent', status: 'Succeeded', errorMessage: null, createdAtUtc: new Date().toISOString(), startedAtUtc: null, completedAtUtc: null },
      nodes: [], events: [], toolCalls: [], feedback: [], approvals: [], promptSnapshots: [], blackboardJson: {}, outputJson: {}, workflowDefinitionJson: {},
      loopSummary: { status: 'Stopped', currentIteration: 1, maxIterations: 2, lastAction: 'Complete', stopReason: 'NO_PROGRESS', dynamicNodeCount: 8, maxDynamicNodes: 18, webRetrievalCount: 1, maxWebRetrievals: 1, evidenceCount: 2, unresolvedClaimIds: ['C1'] },
    })

    const wrapper = mount(AgentRunAdminDetail, { props: { runId: 'quality-1' } })
    await flushPromises()

    expect(wrapper.get('[data-testid="admin-loop-summary"]').text()).toContain('NO_PROGRESS')
    expect(wrapper.get('[data-testid="admin-loop-summary"]').text()).toContain('1 / 2')
  })
})
