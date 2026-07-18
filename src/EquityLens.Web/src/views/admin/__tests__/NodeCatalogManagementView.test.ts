import { describe, expect, it, vi, beforeEach } from 'vitest'
import { defineComponent, nextTick, type VNode } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import type { NodeAdmin } from '../../../services/agentWorkflowAdmin'

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

vi.mock('../../../services/agentWorkflowAdmin', () => ({
  listNodes: vi.fn(),
  saveNode: vi.fn(),
}))

import NodeCatalogManagementView from '../NodeCatalogManagementView.vue'
import { listNodes, saveNode } from '../../../services/agentWorkflowAdmin'

const node: NodeAdmin = {
  nodeType: 'loadResearchRun',
  displayName: '載入研究結果',
  description: '載入既有 Research Run。',
  stage: 'Load',
  sideEffectLevel: 'ReadOnly',
  isEnabled: true,
  timeoutSeconds: 120,
  maxRetryCount: 0,
  metadata: { owner: 'research' },
  contract: {
    nodeType: 'loadResearchRun', version: 1, displayName: '載入研究結果', description: '載入既有 Research Run。', stage: 'Load', sideEffectLevel: 'ReadOnly',
    inputSchema: 'LoadResearchRunInput', outputSchema: 'LoadResearchRunOutput',
    requiredBlackboardKeys: ['researchRunId', 'userId', 'locale'], optionalBlackboardKeys: ['traceId'], producedBlackboardKeys: ['researchRun', 'answer', 'citations'],
    allowedPreviousNodeTypes: [], allowedNextNodeTypes: ['buildEvidencePacket'], defaultPolicy: { timeoutSeconds: 120, maxRetryCount: 0 },
    isIdempotent: true, supportsLoop: false, requiresHumanInput: false,
  },
  requiredBlackboardKeys: ['researchRunId', 'userId', 'locale'],
  producedBlackboardKeys: ['researchRun', 'answer', 'citations'],
  allowedNextNodeTypes: ['buildEvidencePacket'],
}

type TableColumn = { key: string; title: string; render?: (row: NodeAdmin) => any }

function mountView() {
  return mount(NodeCatalogManagementView)
}

describe('NodeCatalogManagementView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(listNodes).mockResolvedValue([node])
  })

  it('提供營運摘要欄位與 Blackboard tooltip', async () => {
    const wrapper = mountView()
    await flushPromises()

    const table = wrapper.findComponent({ name: 'NDataTable' })
    const columns = table.props('columns') as TableColumn[]
    expect(columns.map((column) => column.title)).toEqual(['Node', 'Stage', 'Input / Output', 'Blackboard', 'Side effect', 'Policy', '狀態', '管理'])

    const blackboard = columns.find((column) => column.key === 'blackboard')!.render!(node)
    expect(blackboard.props?.title).toContain('Required: researchRunId, userId, locale')
    expect(blackboard.props?.title).toContain('Produces: researchRun, answer, citations')
    expect((blackboard.children as VNode[]).map((child) => child.children)).toEqual(['In: researchRunId, userId +1', 'Out: researchRun, answer +1'])
    expect(columns.find((column) => column.key === 'policy')!.render!(node)).toBe('120s · 0 retries')
  })

  it('顯示唯讀 Contract 區段並以專屬 payload 儲存有效設定', async () => {
    vi.mocked(saveNode).mockResolvedValue({ ...node, displayName: '新的名稱' })
    const wrapper = mountView()
    await flushPromises()

    const columns = wrapper.findComponent({ name: 'NDataTable' }).props('columns') as TableColumn[]
    const action = columns.find((column) => column.key === 'action')!.render!(node)
    ;(action.props?.onClick as () => void)()
    await nextTick()

    expect(wrapper.text()).toContain('Node Contract（唯讀）')
    expect(wrapper.text()).toContain('原始 Contract JSON')
    expect(wrapper.text()).toContain('進階 Metadata（JSON，可選）')
    expect(wrapper.text()).toContain('Default policy')
    expect(wrapper.text()).toContain('researchRunId, userId, locale')

    await wrapper.get('input').setValue('新的名稱')
    await wrapper.get('button.prestige-btn-solid').trigger('click')
    await flushPromises()

    expect(saveNode).toHaveBeenCalledWith('loadResearchRun', {
      isEnabled: true,
      displayName: '新的名稱',
      description: '載入既有 Research Run。',
      timeoutSeconds: 120,
      maxRetryCount: 0,
      metadata: { owner: 'research' },
    })
    expect(message.success).toHaveBeenCalledWith('Node 設定已儲存')
  })
})
