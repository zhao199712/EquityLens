import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import ResearchAgentSummary from '../ResearchAgentSummary.vue'
import type { AgentRunDetail, AgentRunNodeDto, AgentLlmTurnDto } from '../../../services/agentRuns'
const node = (key: string, status = 'Succeeded') => ({ id: key, nodeKey: key, status } as AgentRunNodeDto)
function fixture(): AgentRunDetail {
  return {
    run: { id: 'run', workflowType: 'ResearchAgent', agentType: 'Research', status: 'Succeeded', createdAtUtc: '', startedAtUtc: '2026-11-03T00:00:00Z', completedAtUtc: '2026-11-03T00:00:10Z', errorMessage: null },
    nodes: [node('finalizeResearch'), ...['reviseAgent', 'reviseOutputGate', 'reviseEvidencePacket', 'reviseCheckEvidence', 'reviseCritiqueAnswer', 'reviseCriticReport'].map(key => node(key, 'Skipped'))],
    events: [], toolCalls: [], feedback: [], approvals: [], workflowDefinitionJson: {},
    blackboardJson: { researchRun: { run: { id: 'research' }, citations: [{ citationIndex: 1, title: '台積電年報', pageNumber: 58, quoteText: '66.26' }] } },
    outputJson: { answer: '基本 EPS 66.26 [1]', status: 'Answered', outputGate: { passed: true }, criticReview: { recommendedNextAction: 'AcceptAnswer' } },
  }
}
const render = (detail = fixture()) => mount(ResearchAgentSummary, { props: { detail }, global: { stubs: { RouterLink: true } } })
describe('ResearchAgentSummary', () => {
  it('shows final answer, sources, elapsed and optional revision without fake node percentages', () => {
    const wrapper = render()
    expect(wrapper.text()).toContain('不需要修訂')
    expect(wrapper.text()).toContain('66.26')
    expect(wrapper.text()).toContain('第 58 頁')
    expect(wrapper.text()).toContain('10 秒')
    expect(wrapper.text()).not.toContain('100%')
    expect(wrapper.text()).not.toContain('US$0')
  })
  it.each(['Running', 'Failed', 'Cancelled', 'WaitingForFeedback'])('never publishes a provisional answer in %s', status => {
    const detail = fixture(); detail.run.status = status
    expect(render(detail).text()).not.toContain('66.26')
  })
  it.each(['InsufficientEvidence', 'CitationValidationFailed'])('separates successful runtime from %s artifact', status => {
    const detail = fixture(); detail.outputJson = { answer: '無法回答', status, outputGate: { passed: false } }
    detail.blackboardJson = {}
    const wrapper = render(detail)
    expect(wrapper.text()).toContain('執行完成')
    expect(wrapper.text()).toContain('未通過')
    expect(wrapper.text()).not.toContain('已回答')
  })
  it('does not describe policy response as quality success', () => {
    const detail = fixture(); detail.outputJson = { answer: '投資有風險', status: 'Answered', policyResponse: true, outputGate: { passed: false } }
    expect(render(detail).text()).toContain('系統風險說明')
    expect(render(detail).text()).toContain('未通過')
  })
  it('shows completed revision and keeps retry usage by durable turn ID', () => {
    const detail = fixture(); detail.nodes.forEach(node => { node.status = 'Succeeded' })
    const turn = { id: 'one', agentRunNodeId: 'reviseAgent', mode: 'Revise', turn: 1, executionAttempt: 1, promptTokens: 10, completionTokens: 5, estimatedCostUsd: .001, createdAtUtc: '2026-11-03T00:00:00Z' } as AgentLlmTurnDto
    detail.llmTurns = [turn, turn, { ...turn, id: 'two', executionAttempt: 2 }]
    const wrapper = render(detail)
    expect(wrapper.text()).not.toContain('不需要修訂')
    expect(wrapper.text()).toContain('US$0.002000')
    expect(wrapper.text()).toContain('attempt 2')
  })
  it('handles old incomplete records without assuming final success', () => {
    const detail = fixture(); detail.nodes = []; detail.outputJson = null; detail.blackboardJson = {}
    expect(render(detail).text()).toContain('缺少可確認的最終結果')
    expect(render(detail).text()).toContain('未提供')
  })
})
