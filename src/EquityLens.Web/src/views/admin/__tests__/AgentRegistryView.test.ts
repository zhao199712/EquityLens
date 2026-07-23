import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { AgentRegistry } from '../../../services/agentRegistryAdmin'

vi.mock('../../../services/agentRegistryAdmin', () => ({
  getAgentRegistry: vi.fn(),
}))

import AgentRegistryView from '../AgentRegistryView.vue'
import { getAgentRegistry } from '../../../services/agentRegistryAdmin'

const registry: AgentRegistry = {
  skills: [
    {
      id: 'conference-call-takeaways',
      displayName: '法說會要點蒸餾',
      description: 'Distill conference call evidence.',
      capabilities: ['plan-research-retrieval', 'retrieve-web-research-evidence'],
      kind: 'Lead',
      supportedWorkflowTypes: ['ResearchInvestigation'],
      routable: true,
      promptTemplateId: 'conference-call-takeaways',
      promptVersion: 1,
      requiredInputs: ['security', 'conference transcript or notes'],
      systemPrompt: 'prompt',
    },
    {
      id: 'research-investigation',
      displayName: '一般個股研究',
      description: 'Plan an evidence-grounded research answer.',
      capabilities: ['plan-research-retrieval', 'retrieve-web-research-evidence'],
      kind: 'Lead',
      supportedWorkflowTypes: ['ResearchInvestigation'],
      routable: true,
      promptTemplateId: null,
      promptVersion: null,
      requiredInputs: ['security'],
      systemPrompt: null,
    },
    {
      id: 'quality-finalization',
      displayName: '品質定稿',
      description: 'Finish the quality workflow.',
      capabilities: ['finalize-quality'],
      kind: 'Supporting',
      supportedWorkflowTypes: [],
      routable: false,
      promptTemplateId: null,
      promptVersion: null,
      requiredInputs: [],
      systemPrompt: null,
    },
  ],
  capabilities: [
    {
      id: 'plan-research-retrieval', nodeType: 'planResearchRetrieval', description: 'Build strategy.',
      argumentSchema: 'ResearchPlan', requiredKeys: ['question'], producedKeys: ['plan'],
      sideEffectLevel: 'ReadOnly', idempotent: true, supportsLoop: false, maxOccurrences: 1,
    },
    {
      id: 'retrieve-web-research-evidence', nodeType: 'retrieveWebEvidence', description: 'Retrieve Web evidence.',
      argumentSchema: 'RetrieveEvidencePlannerArguments', requiredKeys: ['plan'], producedKeys: ['evidence'],
      sideEffectLevel: 'ExternalRead', idempotent: true, supportsLoop: true, maxOccurrences: 2,
    },
    {
      id: 'finalize-quality', nodeType: 'finalizeQuality', description: 'Finalize a result.',
      argumentSchema: 'QualityResult', requiredKeys: ['review'], producedKeys: ['result'],
      sideEffectLevel: 'Write', idempotent: false, supportsLoop: false, maxOccurrences: 1,
    },
  ],
}

describe('AgentRegistryView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(getAgentRegistry).mockResolvedValue(registry)
  })

  it('loads registry counts and displays both sections', async () => {
    const wrapper = mount(AgentRegistryView)
    await flushPromises()

    expect(getAgentRegistry).toHaveBeenCalledOnce()
    expect(wrapper.text()).toContain('3Skills')
    expect(wrapper.text()).toContain('3Capabilities')
    expect(wrapper.text()).toContain('research-investigation')
    expect(wrapper.text()).toContain('conference-call-takeaways v1')
    expect(wrapper.text()).toContain('finalize-quality')
  })

  it('filters capabilities by selected skill and supports clearing filters', async () => {
    const wrapper = mount(AgentRegistryView)
    await flushPromises()

    await wrapper.find('.skill-select').trigger('click')
    expect(wrapper.text()).toContain('Skill: conference-call-takeaways')
    expect(wrapper.findAll('.registry-table tbody')).toHaveLength(2)

    await wrapper.find('.filters button').trigger('click')
    expect(wrapper.findAll('.registry-table tbody')).toHaveLength(3)
  })

  it('searches fields and expands capability details', async () => {
    const wrapper = mount(AgentRegistryView)
    await flushPromises()

    await wrapper.get('input.search').setValue('retrieveWebEvidence')
    expect(wrapper.findAll('.registry-table tbody')).toHaveLength(1)
    expect(wrapper.text()).toContain('retrieve-web-research-evidence')

    await wrapper.find('.detail-button').trigger('click')
    expect(wrapper.text()).toContain('RetrieveEvidencePlannerArguments')
    expect(wrapper.text()).toContain('Capability Argument Contract')
    expect(wrapper.text()).toContain('Capability Parameter Schema')
    expect(wrapper.text()).toContain('Required Keys')
  })

  it('shows a retryable error state', async () => {
    vi.mocked(getAgentRegistry).mockRejectedValueOnce(new Error('offline'))
    const wrapper = mount(AgentRegistryView)
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('無法載入 Agent Registry')
    vi.mocked(getAgentRegistry).mockResolvedValueOnce(registry)
    await wrapper.get('[role="alert"] button').trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })
})
