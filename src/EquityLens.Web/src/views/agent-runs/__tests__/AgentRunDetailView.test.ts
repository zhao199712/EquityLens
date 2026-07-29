import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentRunDetailView from '../AgentRunDetailView.vue'
import { decideAgentApproval } from '../../../services/agentRuns'

interface MockRun {
  run: Record<string, unknown>
  nodes: unknown[]
  workflowDefinitionJson: Record<string, unknown>
  toolCalls: unknown[]
  feedback: unknown[]
  blackboardJson: Record<string, unknown>
  outputJson: Record<string, unknown> | null
}

const state = {
  agentRun: ref<MockRun | null>(null),
  isLoading: ref(false),
  isPolling: ref(false),
  error: ref<string | null>(null),
}

vi.mock('../../../composables/useAgentRunPolling', () => ({
  useAgentRunPolling: () => ({
    agentRun: state.agentRun,
    isLoading: state.isLoading,
    isPolling: state.isPolling,
    isTerminalStatus: computed(() => true),
    error: state.error,
    startPolling: vi.fn(),
    refresh: vi.fn(),
  }),
}))

vi.mock('../../../services/agentRuns', () => ({
  cancelAgentRun: vi.fn(),
  createDraftRevision: vi.fn(),
  decideAgentApproval: vi.fn(),
  retryAgentRun: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'run-1' } }),
  useRouter: () => ({ push: vi.fn() }),
}))

vi.stubGlobal('IntersectionObserver', class {
  observe() {}
  unobserve() {}
  disconnect() {}
})

const stubs = { AgentRunProgress: true, CapabilityRequestStatus: true }

function diagnosisRun(riskMetrics: Record<string, number | null> | null, interpretation?: string | null): MockRun {
  return {
    run: {
      id: 'run-1', workflowType: 'PortfolioDiagnosis', agentType: 'PortfolioDiagnosisAgent',
      status: 'Succeeded', parentAgentRunId: null,
      createdAtUtc: '2026-07-27T13:46:23Z', startedAtUtc: '2026-07-27T13:46:24Z',
      completedAtUtc: '2026-07-27T13:47:00Z', errorMessage: null,
    },
    nodes: [],
    workflowDefinitionJson: { nodes: [], edges: [] },
    toolCalls: [],
    feedback: [],
    blackboardJson: {},
    outputJson: {
      summary: '本期投組相對基準報酬為 -3.39 %。',
      portfolioReturn: -0.0486, benchmarkReturn: -0.0147, activeReturn: -0.0339,
      mainDrags: [], mainContributors: [], recommendedAnalyses: [], evidenceStatus: 'complete',
      riskMetrics,
      interpretation,
    },
  }
}

describe('AgentRunDetailView portfolio risk metrics', () => {
  beforeEach(() => {
    state.agentRun.value = null
    state.isLoading.value = false
    state.error.value = null
  })

  it('renders risk metrics when present', () => {
    state.agentRun.value = diagnosisRun({
      annualizedVolatility: 0.3871, maxDrawdown: -0.0858, historicalVaR: -0.0729,
      expectedShortfall: -0.0729, portfolioVolatility: 0.0247,
      concentrationHhi: 1, largestWeight: 1, volatilityRiskShare: 0.999,
    })
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).toContain('風險指標')
    expect(wrapper.text()).toContain('年化波動率')
    expect(wrapper.text()).toContain('38.71%')
    expect(wrapper.text()).toContain('集中度 HHI')
  })

  it('hides the risk metrics section when all values are null', () => {
    state.agentRun.value = diagnosisRun({
      annualizedVolatility: null, maxDrawdown: null, historicalVaR: null,
      expectedShortfall: null, portfolioVolatility: null,
      concentrationHhi: null, largestWeight: null, volatilityRiskShare: null,
    })
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).not.toContain('風險指標')
  })

  it('hides the risk metrics section for legacy runs without riskMetrics', () => {
    state.agentRun.value = diagnosisRun(null)
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).not.toContain('風險指標')
    expect(wrapper.text()).toContain('AI 投組診斷報告')
  })

  it('renders the narrative interpretation as markdown when present', () => {
    state.agentRun.value = diagnosisRun(
      { annualizedVolatility: 0.36, maxDrawdown: -0.09, historicalVaR: null, expectedShortfall: null, portfolioVolatility: null, concentrationHhi: 0.46, largestWeight: 0.62, volatilityRiskShare: null },
      '風險**高度集中**於聯發科。',
    )
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).toContain('分析解讀')
    expect(wrapper.get('.interpretation').html()).toContain('<strong>高度集中</strong>')
  })

  it('hides the interpretation section when absent', () => {
    state.agentRun.value = diagnosisRun({ annualizedVolatility: 0.36, maxDrawdown: null, historicalVaR: null, expectedShortfall: null, portfolioVolatility: null, concentrationHhi: null, largestWeight: null, volatilityRiskShare: null }, null)
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).not.toContain('分析解讀')
  })

  it('renders a rejected diagnosis run without the report panel', () => {
    state.agentRun.value = rejectedDiagnosisRun()
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).not.toContain('AI 投組診斷報告')
    expect(wrapper.text()).toContain('人工拒絕此投組診斷')
  })
})

function rejectedDiagnosisRun(): MockRun {
  return {
    run: {
      id: 'run-1', workflowType: 'PortfolioDiagnosis', agentType: 'PortfolioDiagnosisAgent',
      status: 'Failed', parentAgentRunId: null,
      createdAtUtc: '2026-07-28T00:00:00Z', startedAtUtc: '2026-07-28T00:00:01Z',
      completedAtUtc: '2026-07-28T00:05:00Z', errorMessage: '人工拒絕此投組診斷：證據不足。',
    },
    nodes: [],
    workflowDefinitionJson: { nodes: [], edges: [] },
    toolCalls: [],
    feedback: [],
    blackboardJson: {},
    outputJson: { rejected: true, decision: 'Rejected', comment: '證據不足。', reviewerId: 'user-1' },
  }
}

function waitingRun(): MockRun {
  return {
    run: {
      id: 'run-1', workflowType: 'HumanApprovalTest', agentType: 'AnalysisAgent',
      status: 'WaitingForFeedback', parentAgentRunId: null,
      createdAtUtc: '2026-07-28T00:00:00Z', startedAtUtc: '2026-07-28T00:00:01Z',
      completedAtUtc: null, errorMessage: null,
    },
    nodes: [],
    workflowDefinitionJson: { nodes: [], edges: [] },
    toolCalls: [],
    feedback: [],
    blackboardJson: {
      approvalRequest: { approvalType: 'ApproveReject', prompt: '請審核診斷結果', nodeKey: 'waitForHumanApproval', requestedAtUtc: '2026-07-28T00:00:05Z' },
    },
    outputJson: null,
  }
}

describe('AgentRunDetailView human approval panel', () => {
  beforeEach(() => {
    state.agentRun.value = null
    state.isLoading.value = false
    state.error.value = null
    vi.mocked(decideAgentApproval).mockReset()
  })

  it('renders the approval panel when waiting for feedback', () => {
    state.agentRun.value = waitingRun()
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.find('[data-testid="approval-panel"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('等待人工批准')
    expect(wrapper.text()).toContain('請審核診斷結果')
  })

  it('does not render the panel for non-waiting runs', () => {
    state.agentRun.value = diagnosisRun(null)
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.find('[data-testid="approval-panel"]').exists()).toBe(false)
  })

  it('approves by calling decideAgentApproval with Approved', async () => {
    state.agentRun.value = waitingRun()
    vi.mocked(decideAgentApproval).mockResolvedValue({} as never)
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    await wrapper.find('[data-testid="approval-approve"]').trigger('click')
    expect(decideAgentApproval).toHaveBeenCalledWith('run-1', 'Approved', '')
  })

  it('requires a comment before rejecting', async () => {
    state.agentRun.value = waitingRun()
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    await wrapper.find('[data-testid="approval-reject"]').trigger('click')
    expect(decideAgentApproval).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('拒絕時必須提供說明。')
  })

  it('rejects with a comment', async () => {
    state.agentRun.value = waitingRun()
    vi.mocked(decideAgentApproval).mockResolvedValue({} as never)
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    await wrapper.find('[data-testid="approval-comment"]').setValue('證據不足')
    await wrapper.find('[data-testid="approval-reject"]').trigger('click')
    expect(decideAgentApproval).toHaveBeenCalledWith('run-1', 'Rejected', '證據不足')
  })
})
