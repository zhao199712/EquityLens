import { legacyWorkflow } from '../../../services/legacyWorkflows'
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
vi.mock('../../../services/agentRuns', () => ({ getAgentRun: vi.fn(), retryAgentRun: vi.fn(), cancelAgentRun: vi.fn(), decideAgentApproval: vi.fn() }))

import AgentRunAdminDetail from '../components/AgentRunAdminDetail.vue'
import { getAgentRun } from '../../../services/agentRuns'

describe('AgentRunAdminDetail evidence remediation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(getAgentRun).mockResolvedValue({
      run: { id: 'critic-1', workflowType: 'CriticReview', agentType: 'CriticAgent', status: 'Succeeded', errorMessage: null, createdAtUtc: new Date().toISOString(), startedAtUtc: null, completedAtUtc: null },
      nodes: [], events: [], toolCalls: [], feedback: [], approvals: [], blackboardJson: {}, outputJson: { requiresMoreEvidence: true }, workflowDefinitionJson: {},
    })
  })

  it('評論結果仍可讀，沒有建立舊補證據流程的入口', async () => {
    const wrapper = mount(AgentRunAdminDetail, { props: { runId: 'critic-1' } })
    await flushPromises()
    expect(wrapper.text()).toContain('CriticReview')
    expect(wrapper.findAll('button').some(item => item.text().includes('補充證據並修訂'))).toBe(false)
    expect(wrapper.findAll('button').some(item => item.text().includes('重新分析'))).toBe(false)
  })

  it('顯示歷史品質流程 loop 預算與停止原因', async () => {
    vi.mocked(getAgentRun).mockResolvedValue({
      run: { id: 'quality-1', workflowType: legacyWorkflow.quality, agentType: 'CriticAgent', status: 'Succeeded', errorMessage: null, createdAtUtc: new Date().toISOString(), startedAtUtc: null, completedAtUtc: null },
      nodes: [], events: [], toolCalls: [], feedback: [], approvals: [], promptSnapshots: [], blackboardJson: {}, outputJson: {}, workflowDefinitionJson: {},
      loopSummary: { status: 'Stopped', currentIteration: 1, maxIterations: 2, lastAction: 'Complete', stopReason: 'NO_PROGRESS', dynamicNodeCount: 8, maxDynamicNodes: 18, webRetrievalCount: 1, maxWebRetrievals: 1, evidenceCount: 2, unresolvedClaimIds: ['C1'] },
    })

    const wrapper = mount(AgentRunAdminDetail, { props: { runId: 'quality-1' } })
    await flushPromises()

    expect(wrapper.get('[data-testid="admin-loop-summary"]').text()).toContain('NO_PROGRESS')
    expect(wrapper.get('[data-testid="admin-loop-summary"]').text()).toContain('1 / 2')
  })
})
