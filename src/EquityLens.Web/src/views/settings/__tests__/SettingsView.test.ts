import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import SettingsView from '../SettingsView.vue'
import i18n, { setLocale } from '../../../locales'
import { getRiskPreference, saveRiskPreference } from '../../../services/settings'

vi.mock('../../../services/settings', () => ({ getRiskPreference: vi.fn(), saveRiskPreference: vi.fn() }))
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
  expect(wrapper.findAll('button')).toHaveLength(1)
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
  await wrapper.get('button').trigger('click')
  expect(saveRiskPreference).toHaveBeenCalledWith('Balanced')
  expect(wrapper.text()).not.toContain('Risk preference saved.')
  expect(wrapper.get('button').attributes('disabled')).toBeDefined()
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
  await wrapper.get('button').trigger('click')
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
