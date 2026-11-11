import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '../../../stores/auth'
import { legacyWorkflow } from '../../../services/legacyWorkflows'
import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentRunDetailView from '../AgentRunDetailView.vue'
import { decideAgentApproval } from '../../../services/agentRuns'

interface MockRun {
  run: Record<string, unknown>
  events: unknown[]
  nodes: unknown[]
  workflowDefinitionJson: Record<string, unknown>
  toolCalls: unknown[]
  feedback: unknown[]
  approvals: unknown[]
  blackboardJson: Record<string, unknown>
  outputJson: Record<string, unknown> | null
  loopSummary?: Record<string, unknown> | null
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
  decideAgentApproval: vi.fn(),
  retryAgentRun: vi.fn(),
}))

vi.mock('../../../services/http', () => ({ http: {} }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'run-1' } }),
  useRouter: () => ({ push: vi.fn() }),
}))

vi.stubGlobal('IntersectionObserver', class {
  observe() {}
  unobserve() {}
  disconnect() {}
})

const stubs = { AgentRunProgress: true }

beforeEach(() => { setActivePinia(createPinia()) })

function diagnosisRun(riskMetrics: Record<string, number | null> | null, interpretation?: string | null): MockRun {
  return {
    run: {
      id: 'run-1', workflowType: 'PortfolioDiagnosis', agentType: 'PortfolioDiagnosisAgent',
      status: 'Succeeded', parentAgentRunId: null,
      createdAtUtc: '2026-07-27T13:46:23Z', startedAtUtc: '2026-07-27T13:46:24Z',
      completedAtUtc: '2026-07-27T13:47:00Z', errorMessage: null,
    },
    events: [],
    nodes: [],
    workflowDefinitionJson: { nodes: [], edges: [] },
    toolCalls: [],
    feedback: [],
    approvals: [],
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

  it('distinguishes no negative contributors from incomplete coverage', () => {
    const run = diagnosisRun(null)
    run.outputJson!.attributionCoverage = 'complete'
    state.agentRun.value = run
    expect(mount(AgentRunDetailView, { global: { stubs } }).text()).toContain('本期沒有負貢獻持股')
    run.outputJson!.attributionCoverage = 'partial'
    state.agentRun.value = { ...run }
    expect(mount(AgentRunDetailView, { global: { stubs } }).text()).toContain('資料涵蓋不足')
  })

  it('keeps a long report visible without an intersection callback', () => {
    state.agentRun.value = diagnosisRun(null, '長報告內容。'.repeat(2000))
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    const report = wrapper.get('.interpretation').element
    expect(report.closest('.kimi-reveal:not(.visible)')).toBeNull()
    wrapper.unmount()
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

  it('renders the bounded research quality loop summary', () => {
    const value = diagnosisRun(null)
    value.run.workflowType = legacyWorkflow.quality
    value.loopSummary = {
      status: 'Stopped', currentIteration: 2, maxIterations: 2,
      lastAction: 'Complete', stopReason: 'MAX_ITERATIONS',
      dynamicNodeCount: 12, maxDynamicNodes: 18,
      webRetrievalCount: 1, maxWebRetrievals: 1,
      evidenceCount: 4, unresolvedClaimIds: ['C2'],
    }
    state.agentRun.value = value

    const wrapper = mount(AgentRunDetailView, { global: { stubs } })

    expect(wrapper.get('[data-testid="loop-summary"]').text()).toContain('2 / 2')
    expect(wrapper.get('[data-testid="loop-summary"]').text()).toContain('MAX_ITERATIONS')
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

  it('does not report benchmark data as missing when attribution was not part of the question', () => {
    const run = diagnosisRun({ annualizedVolatility: 0.53, maxDrawdown: -0.31, historicalVaR: null, expectedShortfall: null, portfolioVolatility: null, concentrationHhi: 0.64, largestWeight: 0.77, volatilityRiskShare: null }, null)
    Object.assign(run.outputJson as Record<string, unknown>, { portfolioReturn: null, benchmarkReturn: null, activeReturn: null, attributionCoverage: null })
    state.agentRun.value = run
    const text = mount(AgentRunDetailView, { global: { stubs } }).text()
    expect(text).not.toContain('大盤報酬')
    expect(text).not.toContain('主要拖累')
    expect(text).toContain('本次問題未做績效歸因')
    expect(text).not.toContain('建議補做的風險分析')
  })

  it('shows a summary that repeats the interpretation only once, rendered as markdown', () => {
    const run = diagnosisRun({ annualizedVolatility: 0.53, maxDrawdown: null, historicalVaR: null, expectedShortfall: null, portfolioVolatility: null, concentrationHhi: null, largestWeight: null, volatilityRiskShare: null },
      '年化波動 **53.39%**。\n\n| 指標 | 結果 |\n|---|---|\n| 年化波動 | 53.39% |')
    ;(run.outputJson as Record<string, unknown>).summary = '年化波動 **53.39%**。\n\n| 指標 | 結果 |\n|---|---|\n| 年化波動 | 53.39% |\n\n依預設的風險偏好『穩健』。'
    state.agentRun.value = run
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).not.toContain('摘要')
    expect(wrapper.text()).toContain('分析結果')
    expect(wrapper.findAll('table.md-table')).toHaveLength(1)
    expect(wrapper.text()).not.toContain('|---|')
  })

  it('hides the interpretation section when absent', () => {
    state.agentRun.value = diagnosisRun({ annualizedVolatility: 0.36, maxDrawdown: null, historicalVaR: null, expectedShortfall: null, portfolioVolatility: null, concentrationHhi: null, largestWeight: null, volatilityRiskShare: null }, null)
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).not.toContain('分析解讀')
  })

  it('renders a rejected diagnosis run without the report panel', () => {
    const item = diagnosisRun(null)
    item.run.status = 'Cancelled'
    item.run.errorMessage = '人工拒絕此投組診斷：證據不足。'
    state.agentRun.value = item
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).not.toContain('AI 投組診斷報告')
    expect(wrapper.text()).toContain('人工拒絕此投組診斷')
  })
})

describe('AgentRunDetailView human approval panel', () => {
  beforeEach(() => {
    state.agentRun.value = null
    state.isLoading.value = false
    state.error.value = null
    vi.mocked(decideAgentApproval).mockReset()
  })

  it('renders a pending approval with guarded reject action', async () => {
    const item = diagnosisRun(null)
    item.run.status = 'WaitingForApproval'
    item.approvals = [{
      id: 'approval-1', agentRunId: 'run-1', agentRunNodeId: 'node-1',
      nodeKey: 'danger', nodeType: 'ExternalWrite', status: 'Pending',
      sideEffectLevel: 'ExternalWrite', reason: '需要人工批准',
      requestedAtUtc: '2026-07-27T13:46:24Z', decidedAtUtc: null,
      consumedAtUtc: null, decidedByUserId: null, decisionComment: null, clientRequestId: null,
    }]
    state.agentRun.value = item

    const wrapper = mount(AgentRunDetailView, { global: { stubs } })

    expect(wrapper.get('[data-testid="approval-panel"]').text()).toContain('需要人工批准')
    const reject = wrapper.findAll('button').find(button => button.text().includes('拒絕並取消'))
    expect(reject?.attributes('disabled')).toBeDefined()
    await wrapper.get('.approval-comment').setValue('風險過高')
    expect(reject?.attributes('disabled')).toBeUndefined()
  })
})


describe('AgentRunDetailView role-scoped tabs and legacy data', () => {
  it.each(['User', 'Admin'])('shows only the allowed tabs for %s', async (role) => {
    useAuthStore().user = { id: 'user-1', email: 'test@example.invalid', displayName: 'Test', role }
    state.agentRun.value = diagnosisRun(null)
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    await wrapper.get('.debug-toggle').trigger('click')
    const expected = ['時間線', '節點', '工具', 'Feedback']
    if (role === 'Admin') expected.push('Blackboard', 'Workflow')
    expect(wrapper.findAll('.tab-btn').map(tab => tab.text())).toEqual(expected)
    if (role === 'Admin') {
      await wrapper.findAll('.tab-btn')[4]!.trigger('click')
      expect(wrapper.get('.tab-pad').text()).toContain('Blackboard')
      useAuthStore().user!.role = 'User'
      await wrapper.vm.$nextTick()
      expect(wrapper.find('.tab-pad').exists()).toBe(false)
      expect(wrapper.get('.tab-btn.active').text()).toBe('時間線')
    }
  })

  it.each(['User', 'Admin'])('ignores obsolete fields without breaking the report for %s', (role) => {
    useAuthStore().user = { id: 'user-1', email: 'test@example.invalid', displayName: 'Test', role }
    const item = diagnosisRun(null, '保留投組診斷內容')
    // Historical fields may be incomplete, including a missing contextEnvelope.
    item.blackboardJson = {
      routingContext: { leadSkill: 'retired-skill', leadSkillDisplayName: '舊技能名稱' },
      capabilityRequestAssessment: { decision: 'Request', reason: '舊能力評估' },
      capabilityRequests: [{ capabilityId: 'retired-capability', status: 'Pending' }],
    }
    state.agentRun.value = item
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).toContain('保留投組診斷內容')
    expect(wrapper.find('[data-testid="routing-stage"]').exists()).toBe(false)
    expect(wrapper.text()).not.toMatch(/retired-skill|舊技能名稱|舊能力評估|retired-capability|等待 Planner/)
  })

  it.each([
    ['CriticReview', { summary: '保留審查結論', recommendedNextAction: 'AcceptAnswer' }, '保留審查結論'],
    [legacyWorkflow.draft, { revisedAnswer: '保留修訂答案' }, '保留修訂答案'],
  ])('preserves %s output', (workflow, output, expected) => {
    const item = diagnosisRun(null)
    item.run.workflowType = workflow
    item.outputJson = output
    state.agentRun.value = item
    const wrapper = mount(AgentRunDetailView, { global: { stubs } })
    expect(wrapper.text()).toContain(expected)
  })
})
