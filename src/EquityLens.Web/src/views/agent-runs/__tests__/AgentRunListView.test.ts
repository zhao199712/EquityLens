import { mount, flushPromises } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import AgentRunListView from '../AgentRunListView.vue'
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../../services/agentRuns', () => ({ listAgentRuns: vi.fn().mockResolvedValue([
  { id: 'research', workflowType: 'ResearchAgent', agentType: 'Research', status: 'Succeeded', ticker: '2330', questionSummary: '風險因素有哪些？' },
  { id: 'portfolio', workflowType: 'PortfolioDiagnosis', agentType: 'Portfolio', status: 'Succeeded', portfolioName: '退休投組' },
]) }))
it('shows and searches subject summaries', async () => {
  const wrapper = mount(AgentRunListView, { global: { stubs: { ScrollReveal: { template: '<div><slot /></div>' } } } })
  await flushPromises()
  expect(wrapper.text()).toContain('2330')
  expect(wrapper.text()).toContain('風險因素有哪些？')
  expect(wrapper.text()).toContain('退休投組')
  await wrapper.get('input').setValue('退休')
  expect(wrapper.text()).not.toContain('風險因素有哪些？')
  expect(wrapper.text()).toContain('退休投組')
})

it('defaults to three current workflows and queries historical types by their original value', async () => {
  const { listAgentRuns } = await import('../../../services/agentRuns')
  const { retiredWorkflowTypes } = await import('../../../services/legacyWorkflows')
  const wrapper = mount(AgentRunListView, { global: { stubs: { ScrollReveal: { template: '<div><slot /></div>' } } } })
  await flushPromises()
  const select = wrapper.get('select[aria-label="Workflow"]')
  expect(select.findAll('option').map(option => option.attributes('value')).filter(Boolean))
    .toEqual(['ResearchAgent', 'PortfolioDiagnosis', 'CriticReview'])
  await wrapper.get('input[type="checkbox"]').setValue(true)
  expect(select.findAll('optgroup')[1]?.attributes('label')).toContain('已退役，唯讀')
  for (const workflow of retiredWorkflowTypes) {
    await select.setValue(workflow)
    await flushPromises()
    expect(listAgentRuns).toHaveBeenLastCalledWith({ limit: 50, workflowType: workflow, status: undefined })
  }
  await wrapper.get('input[type="checkbox"]').setValue(false)
  await flushPromises()
  expect(listAgentRuns).toHaveBeenLastCalledWith({ limit: 50, workflowType: undefined, status: undefined })
})
