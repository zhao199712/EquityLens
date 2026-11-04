import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import SettingsView from '../SettingsView.vue'
import i18n, { setLocale } from '../../../locales'
import { getRiskPreference, saveRiskPreference, submitRiskQuestionnaire, deleteRiskPreference } from '../../../services/settings'

vi.mock('../../../services/settings', () => ({ getRiskPreference: vi.fn(), saveRiskPreference: vi.fn(), submitRiskQuestionnaire: vi.fn(), deleteRiskPreference: vi.fn() }))
const defaultProfile = { value: 'Balanced' as const, label: '穩健', isDefault: true, statement: '預設' }
beforeEach(() => {
  vi.resetAllMocks()
  setLocale('en')
  vi.mocked(getRiskPreference).mockResolvedValue(defaultProfile)
})

it('only exposes immediately applied language and the real risk preference', async () => {
  const wrapper = mount(SettingsView, { global: { plugins: [i18n] } })
  await flushPromises()
  expect(wrapper.classes()).toContain('prestige-page')
  expect(wrapper.classes()).not.toContain('page')
  expect(wrapper.findAll('button')).toHaveLength(2)
  expect(wrapper.findAll('select')).toHaveLength(2)
  expect(wrapper.text()).not.toMatch(/GPT|Temperature|Save Settings|Reset Defaults|Critic|Yahoo/)
  await wrapper.get('#display-language').setValue('zh-TW')
  expect(wrapper.get('h1').text()).toBe('設定')
  expect(localStorage.getItem('equitylens-locale')).toBe('zh-TW')
  expect(saveRiskPreference).not.toHaveBeenCalled()
  wrapper.unmount()
  setLocale('en')
})

it('keeps the default label until the server confirms an explicit preference', async () => {
  let resolve!: (profile: typeof defaultProfile) => void
  vi.mocked(saveRiskPreference).mockReturnValue(new Promise(r => { resolve = r }))
  const wrapper = mount(SettingsView, { global: { plugins: [i18n] } })
  await flushPromises()
  expect(wrapper.get('[data-testid="saved-risk-preference"]').text()).toContain('default')
  await wrapper.get('[data-testid="save-manual-risk"]').trigger('click')
  expect(saveRiskPreference).toHaveBeenCalledWith('Balanced')
  expect(wrapper.text()).not.toContain('Risk preference saved.')
  expect(wrapper.get('[data-testid="save-manual-risk"]').attributes('disabled')).toBeDefined()
  resolve({ ...defaultProfile, isDefault: false })
  await flushPromises()
  expect(wrapper.get('[data-testid="saved-risk-preference"]').text()).toContain('explicitly saved')
  expect(wrapper.get('[role="status"]').text()).toBe('Risk preference saved.')
})

it('never claims success or replaces the saved preference after a failed save', async () => {
  vi.mocked(saveRiskPreference).mockRejectedValue(new Error('network'))
  const wrapper = mount(SettingsView, { global: { plugins: [i18n] } })
  await flushPromises()
  await wrapper.get('#risk-preference').setValue('Aggressive')
  await wrapper.get('[data-testid="save-manual-risk"]').trigger('click')
  await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Could not save')
  expect(wrapper.get('[data-testid="saved-risk-preference"]').text()).toContain('Balanced')
  expect(wrapper.text()).not.toContain('Risk preference saved.')
})

it('does not invent a preference when loading fails', async () => {
  vi.mocked(getRiskPreference).mockRejectedValue(new Error('network'))
  const wrapper = mount(SettingsView, { global: { plugins: [i18n] } })
  await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Unable to load')
  expect(wrapper.find('#risk-preference').exists()).toBe(false)
  expect(wrapper.find('button').exists()).toBe(false)
})

it('submits all six answers, displays sublevels and source, and deletes on the server', async () => {
  const wrapper = mount(SettingsView, { global: { plugins: [i18n] } })
  await flushPromises()
  await wrapper.get('[data-testid="assess-risk"]').trigger('click')
  expect(wrapper.get('[data-testid="submit-questionnaire"]').attributes('disabled')).toBeDefined()
  for (const [key,value] of Object.entries({ c1:1,c2:3,c3:3,w1:3,w2:3,w3:0 }))
    await wrapper.get(`#risk-${key}`).setValue(value)
  vi.mocked(submitRiskQuestionnaire).mockResolvedValue({ ...defaultProfile, value:'Conservative', isDefault:false, source:'Questionnaire', capacityLevel:'Conservative', willingnessLevel:'Aggressive', assessedAt:'2026-01-02T00:00:00Z' })
  await wrapper.get('form').trigger('submit'); await flushPromises()
  expect(submitRiskQuestionnaire).toHaveBeenCalledWith({c1:1,c2:3,c3:3,w1:3,w2:3,w3:0})
  expect(wrapper.get('[data-testid="risk-sublevels"]').text()).toContain('Conservative')
  expect(wrapper.get('[data-testid="risk-sublevels"]').text()).toContain('Aggressive')
  expect(wrapper.text()).toContain('Questionnaire'); expect(wrapper.text()).toContain('2026')
  expect(wrapper.find('form').exists()).toBe(false)
  vi.mocked(deleteRiskPreference).mockResolvedValue(defaultProfile)
  await wrapper.get('[data-testid="delete-risk"]').trigger('click'); await flushPromises()
  expect(deleteRiskPreference).toHaveBeenCalledOnce()
  expect(wrapper.get('[data-testid="saved-risk-preference"]').text()).toContain('default')
  await wrapper.get('[data-testid="assess-risk"]').trigger('click')
  expect((wrapper.get('#risk-c1').element as HTMLSelectElement).selectedIndex).toBe(0)
})

it('retains the saved profile on questionnaire/delete failure and warns when stale', async () => {
  vi.mocked(getRiskPreference).mockResolvedValue({ ...defaultProfile, isDefault:false, source:'Manual', stale:true })
  const wrapper=mount(SettingsView,{global:{plugins:[i18n]}}); await flushPromises()
  expect(wrapper.text()).toContain('over 12 months')
  vi.mocked(deleteRiskPreference).mockRejectedValue(new Error('network'))
  await wrapper.get('[data-testid="delete-risk"]').trigger('click'); await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Could not save')
  expect(wrapper.find('[data-testid="delete-risk"]').exists()).toBe(true)
  await wrapper.get('[data-testid="assess-risk"]').trigger('click')
  for (const key of ['c1','c2','c3','w1','w2','w3']) await wrapper.get(`#risk-${key}`).setValue(2)
  vi.mocked(submitRiskQuestionnaire).mockRejectedValue(new Error('network'))
  await wrapper.get('form').trigger('submit'); await flushPromises()
  expect(wrapper.get('[role="alert"]').text()).toContain('Could not save')
  expect(wrapper.find('form').exists()).toBe(true)
  expect(localStorage.getItem('risk-answers')).toBeNull()
})
