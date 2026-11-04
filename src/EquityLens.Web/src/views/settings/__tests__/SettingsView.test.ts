import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import SettingsView from '../SettingsView.vue'
import i18n, { setLocale } from '../../../locales'

it('only exposes immediately applied, persisted language preferences', async () => {
  setLocale('en')
  const wrapper = mount(SettingsView, { global: { plugins: [i18n] } })
  expect(wrapper.classes()).toContain('prestige-page')
  expect(wrapper.classes()).not.toContain('page')
  expect(wrapper.findAll('button')).toHaveLength(0)
  expect(wrapper.findAll('select')).toHaveLength(1)
  expect(wrapper.text()).not.toMatch(/GPT|Temperature|Save Settings|Reset Defaults|Critic|Yahoo/)
  await wrapper.get('select').setValue('zh-TW')
  expect(wrapper.get('h1').text()).toBe('設定')
  expect(localStorage.getItem('equitylens-locale')).toBe('zh-TW')
  wrapper.unmount()
  setLocale('en')
})
