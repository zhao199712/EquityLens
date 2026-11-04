import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import App from '../App.vue'
import { useAuthStore } from '../stores/auth'

vi.mock('../services/http', () => ({ http: { defaults: { headers: { common: {} } } } }))
beforeEach(() => sessionStorage.clear())

async function render(authenticated = false, role = 'User') {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  if (authenticated) {
    auth.token = 'test-session'
    auth.user = { id: 'fixture', email: 'fixture@example.invalid', displayName: 'Fixture', role }
  }
  const router = createRouter({ history: createMemoryHistory(), routes: ['home', 'portfolios', 'login', 'register'].map(name => ({
    name, path: name === 'home' ? '/' : `/${name}`, component: { template: '<div>View</div>' },
  })) })
  await router.push('/')
  await router.isReady()
  const i18n = createI18n({ legacy: false, locale: 'en', missingWarn: false, fallbackWarn: false })
  const wrapper = mount(App, { global: { plugins: [pinia, router, i18n], stubs: { NConfigProvider: { template: '<slot />' } } } })
  return { wrapper, router }
}

describe('merged navigation', () => {
  it('offers public access without exposing signed-in actions', async () => {
    const { wrapper } = await render()
    expect(wrapper.get('a[href="/login"]').text()).toBe('nav.login')
    expect(wrapper.get('a[href="/register"]').text()).toBe('nav.register')
    expect(wrapper.find('a[href="/settings"]').exists()).toBe(false)
    expect(wrapper.get('nav').text()).not.toContain('nav.portfolios')
    expect(wrapper.text()).not.toContain('nav.logout')
    wrapper.unmount()
  })

  it.each(['User', 'Admin'])('keeps authenticated navigation and %s permissions', async role => {
    const { wrapper } = await render(true, role)
    expect(wrapper.get('nav').text()).toContain('nav.portfolios')
    expect(wrapper.find('a[href="/settings"]').exists()).toBe(true)
    expect(wrapper.get('nav').text().includes('Admin')).toBe(role === 'Admin')
    expect(wrapper.find('a[href="/login"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('closes the mobile menu after selection and after another route change', async () => {
    const { wrapper, router } = await render(true)
    const toggle = wrapper.get('button[aria-controls="kimi-main-nav"]')
    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    await wrapper.findAll('nav button').find(b => b.text() === 'nav.portfolios')!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('portfolios')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    await toggle.trigger('click')
    await router.push('/')
    await flushPromises()
    expect(toggle.attributes('aria-expanded')).toBe('false')
    await router.push('/login')
    await flushPromises()
    expect(wrapper.find('header').exists()).toBe(false)
    wrapper.unmount()
  })
})
