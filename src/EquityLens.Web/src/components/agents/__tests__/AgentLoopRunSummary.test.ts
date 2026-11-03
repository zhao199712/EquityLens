import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AgentLoopRunSummary from '../AgentLoopRunSummary.vue'
import type { AgentRunDetail } from '../../../services/agentRuns'
describe('persisted AgentLoop summary', () => {
  it('shows actual turns, denials, approval draft and quality limitation instead of legacy defaults', () => {
    const detail = {
      run: { status: 'WaitingForFeedback' }, nodes: [{ id: 'loop', nodeType: 'PortfolioAgentLoop', displayName: '投組分析', outputJson: { stopReason: 'AnswerSubmitted' } }],
      llmTurns: Array.from({ length: 5 }, (_, i) => ({ id: `${i}`, agentRunNodeId: 'loop' })),
      toolCalls: [{ agentRunNodeId: 'loop', status: 'Denied' }, { agentRunNodeId: 'loop', status: 'Succeeded' }],
      blackboardJson: { portfolioQualityGate: { passed: false, blockingIssues: ['MixedRiskBasis'] }, portfolioDiagnosisDraft: { summary: '已驗證的比較', interpretation: '不建議調整' } },
    } as unknown as AgentRunDetail
    const wrapper = mount(AgentLoopRunSummary, { props: { detail } })
    expect(wrapper.text()).toContain('5 輪')
    expect(wrapper.text()).toContain('工具 2 次（拒絕 1 次）')
    expect(wrapper.text()).toContain('等待人工審閱')
    expect(wrapper.text()).toContain('不同方法或快照')
    expect(wrapper.get('[data-testid="approval-draft"]').text()).toContain('不建議調整')
    expect(wrapper.text()).not.toMatch(/NotStarted|Pending|動態節點/)
  })
})
