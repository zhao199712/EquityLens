import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ResearchRunListView from '../ResearchRunListView.vue'

const push = vi.fn()
const createResearchInvestigation = vi.fn(async () => ({ agentRunId: 'run-1' }))

vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('../../../services/research', () => ({
  listResearchRuns: vi.fn(async () => []),
  createResearchInvestigation: (...args: unknown[]) => createResearchInvestigation(...(args as [])),
}))

async function openAskForm() {
  const wrapper = mount(ResearchRunListView, { global: { stubs: { ScrollReveal: { template: '<div><slot /></div>' } } } })
  await flushPromises()
  const toggle = wrapper.findAll('button').find(button => button.text().includes('提出研究問題'))
  await toggle!.trigger('click')
  await wrapper.find('.ask-ticker').setValue('2330')
  await wrapper.find('.ask-question').setValue('台積電毛利率變化')
  return wrapper
}

describe('ResearchRunListView ask form', () => {
  beforeEach(() => vi.clearAllMocks())

  it('does not submit when Enter only confirms an IME candidate', async () => {
    const wrapper = await openAskForm()
    await wrapper.find('.ask-question').trigger('keydown', { key: 'Enter', isComposing: true })
    await wrapper.find('.ask-question').trigger('keydown', { key: 'Enter', keyCode: 229 })
    await flushPromises()
    expect(createResearchInvestigation).not.toHaveBeenCalled()
  })

  it('submits once on a plain Enter', async () => {
    const wrapper = await openAskForm()
    await wrapper.find('.ask-question').trigger('keydown', { key: 'Enter' })
    await flushPromises()
    expect(createResearchInvestigation).toHaveBeenCalledTimes(1)
    expect(push).toHaveBeenCalledWith({ name: 'agent-run-detail', params: { id: 'run-1' } })
  })
})
