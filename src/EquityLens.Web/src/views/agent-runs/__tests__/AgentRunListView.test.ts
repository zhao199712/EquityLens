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
