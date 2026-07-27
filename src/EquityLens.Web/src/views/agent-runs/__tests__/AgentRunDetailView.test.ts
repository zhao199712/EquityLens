import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentRunDetailView from '../AgentRunDetailView.vue'

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

function diagnosisRun(riskMetrics: Record<string, number | null> | null): MockRun {
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
})
