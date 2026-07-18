import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, nextTick, reactive } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import type { WorkflowAdmin } from '../../../services/agentWorkflowAdmin'

const message = { error: vi.fn(), success: vi.fn() }

vi.mock('naive-ui', () => ({
  NButton: defineComponent({ name: 'NButton', template: '<button><slot /></button>' }),
  NDataTable: defineComponent({ name: 'NDataTable', props: ['data', 'columns'], template: '<div class="data-table" />' }),
  NModal: defineComponent({ name: 'NModal', template: '<div class="modal"><slot /></div>' }),
  NSpace: defineComponent({ name: 'NSpace', template: '<div><slot /></div>' }),
  NSwitch: defineComponent({ name: 'NSwitch', template: '<div><slot /></div>' }),
  NTag: defineComponent({ name: 'NTag', template: '<span><slot /></span>' }),
  useMessage: () => message,
}))

vi.mock('../../../services/agentWorkflowAdmin', () => ({ listWorkflows: vi.fn(), saveWorkflow: vi.fn() }))

import WorkflowManagementView from '../WorkflowManagementView.vue'
import { listWorkflows, saveWorkflow } from '../../../services/agentWorkflowAdmin'

const workflow: WorkflowAdmin = {
  workflowType: 'ResearchQualityReview', displayName: '研究品質審查', description: '檢查研究答案與證據品質後產生修正版。', agentType: 'CriticAgent', isEnabled: true,
  nodeTypes: ['LoadResearchRun', 'BuildEvidencePacket', 'CheckEvidence', 'CritiqueAnswer', 'FinalizeCriticReport', 'DraftRevisedAnswer', 'FinalizeRevision'],
  edges: [
    { from: 'LoadResearchRun', to: 'BuildEvidencePacket' }, { from: 'BuildEvidencePacket', to: 'CheckEvidence' }, { from: 'CheckEvidence', to: 'CritiqueAnswer' },
    { from: 'CritiqueAnswer', to: 'FinalizeCriticReport' }, { from: 'FinalizeCriticReport', to: 'DraftRevisedAnswer' }, { from: 'DraftRevisedAnswer', to: 'FinalizeRevision' },
  ],
}
type TableColumn = { key: string; title: string; render?: (row: WorkflowAdmin) => any }

describe('WorkflowManagementView', () => {
  beforeEach(() => { vi.clearAllMocks(); vi.mocked(listWorkflows).mockResolvedValue([workflow]) })

  it('顯示 workflow type 與流程摘要', async () => {
    const wrapper = mount(WorkflowManagementView)
    await flushPromises()
    const columns = wrapper.findComponent({ name: 'NDataTable' }).props('columns') as TableColumn[]
    expect(columns.map(column => column.title)).toEqual(['Workflow', 'Agent', 'Flow', '狀態', '管理'])
    const flow = columns.find(column => column.key === 'flow')!.render!(workflow)
    expect(flow.props.title).toContain('7 nodes · 6 edges')
    expect(flow.props.title).toContain('LoadResearchRun → FinalizeRevision')
  })

  it('以 reactive row 開啟設定並只儲存有效設定', async () => {
    vi.mocked(saveWorkflow).mockResolvedValue({ ...workflow, displayName: '新的品質流程' })
    const wrapper = mount(WorkflowManagementView)
    await flushPromises()
    const columns = wrapper.findComponent({ name: 'NDataTable' }).props('columns') as TableColumn[]
    const action = columns.find(column => column.key === 'action')!.render!(reactive(workflow))
    ;(action.props.onClick as () => void)()
    await nextTick()

    expect(wrapper.text()).toContain('流程定義（唯讀）')
    expect(wrapper.text()).toContain('執行步驟（唯讀）')
    expect(wrapper.text()).toContain('DAG Edges（唯讀）')
    expect(wrapper.text()).toContain('LoadResearchRun')
    expect(wrapper.text()).toContain('FinalizeRevision')
    await wrapper.get('input').setValue('新的品質流程')
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    await flushPromises()

    expect(saveWorkflow).toHaveBeenCalledWith('ResearchQualityReview', { isEnabled: true, displayName: '新的品質流程', description: workflow.description })
    expect(message.success).toHaveBeenCalledWith('Workflow 設定已儲存')
  })
})
