import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AgentRunProgress from '../AgentRunProgress.vue'
import type { AgentRunNodeDto } from '../../../services/agentRuns'

function node(id: string, status: string, stage: string, displayName: string): AgentRunNodeDto {
  return {
    id, nodeKey: id, templateNodeKey: id, iteration: 0, nodeType: id,
    displayName, description: `${displayName}說明`, stage, status,
    inputJson: null, outputJson: null, errorMessage: null,
    startedAtUtc: status === 'Pending' ? null : '2026-07-22T09:00:00Z', completedAtUtc: status === 'Succeeded' ? '2026-07-22T09:00:02Z' : null,
    durationMs: status === 'Succeeded' ? 2000 : null,
  }
}

describe('AgentRunProgress', () => {
  it('shows ordered stage progress and current node', () => {
    const wrapper = mount(AgentRunProgress, { props: {
      nodes: [node('draft', 'Pending', 'Draft', '產生答案'), node('retrieve', 'Running', 'Retrieval', '檢索證據'), node('input', 'Succeeded', 'Input', '驗證問題')],
      workflowDefinition: { nodes: [{ id: 'input' }, { id: 'retrieve' }, { id: 'draft' }] },
      runStatus: 'Running', startedAtUtc: '2026-07-22T09:00:00Z',
    } })

    expect(wrapper.text()).toContain('33%')
    expect(wrapper.text()).toContain('資料檢索')
    expect(wrapper.text()).toContain('檢索證據')
    expect(wrapper.text()).toContain('可能需要數分鐘')
    expect(wrapper.findAll('li').map(item => item.text())).toEqual([
      expect.stringContaining('驗證問題'), expect.stringContaining('檢索證據'), expect.stringContaining('產生答案'),
    ])
  })

  it('uses unknown stage values without failing', () => {
    const wrapper = mount(AgentRunProgress, { props: {
      nodes: [node('dynamic', 'Running', 'CustomStage', '動態節點')], runStatus: 'Running', startedAtUtc: null,
    } })
    expect(wrapper.text()).toContain('CustomStage')
    expect(wrapper.text()).toContain('尚未開始')
  })

  it('does not show 100 percent while a dynamic workflow is still materializing nodes', () => {
    const wrapper = mount(AgentRunProgress, { props: {
      nodes: [node('input', 'Succeeded', 'Input', '驗證問題')], runStatus: 'Running', startedAtUtc: '2026-07-22T09:00:00Z',
    } })
    expect(wrapper.text()).toContain('95%')
    expect(wrapper.text()).toContain('規劃下一階段')
  })
})
