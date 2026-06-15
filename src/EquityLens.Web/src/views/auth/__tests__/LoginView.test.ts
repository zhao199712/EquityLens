import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import en from '../../../locales/en'
import LoginView from '../LoginView.vue'

vi.mock('../../../services/http', () => ({
  http: {
    post: vi.fn(),
    get: vi.fn(),
    defaults: { headers: { common: {} } },
  },
}))

import { http } from '../../../services/http'
const mockedHttp = vi.mocked(http)

function createTestRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/login', name: 'login', component: { template: '<div />' } },
      { path: '/register', name: 'register', component: { template: '<div />' } },
      { path: '/', name: 'dashboard', component: { template: '<div />' } },
    ],
  })
}

function createTestI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: { en },
  })
}

function mountLogin() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createTestRouter()
  const i18n = createTestI18n()

  return mount(LoginView, {
    global: {
      plugins: [pinia, router, i18n],
      stubs: { RouterLink: true },
    },
  })
}

describe('LoginView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('renders login form', () => {
    const wrapper = mountLogin()

    expect(wrapper.find('h2').text()).toBe('Welcome Back')
    expect(wrapper.find('input[type="email"]').exists()).toBe(true)
    expect(wrapper.find('input[type="password"]').exists()).toBe(true)
    expect(wrapper.find('button[type="submit"]').text()).toContain('Sign In')
  })

  it('renders brand area', () => {
    const wrapper = mountLogin()
    expect(wrapper.find('.kimi-login-title').text()).toBe('EQUITYLENS')
  })

  it('renders register link', () => {
    const wrapper = mountLogin()
    const link = wrapper.findComponent({ name: 'RouterLink' })
    expect(link.props('to')).toBe('/register')
  })

  it('shows error when fields are empty', async () => {
    const wrapper = mountLogin()

    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.kimi-error').text()).toBe('Please fill in all fields')
    expect(mockedHttp.post).not.toHaveBeenCalled()
  })

  it('shows error when only email is filled', async () => {
    const wrapper = mountLogin()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.kimi-error').text()).toBe('Please fill in all fields')
  })

  it('redirects to dashboard on successful login', async () => {
    mockedHttp.post.mockResolvedValueOnce({
      data: {
        accessToken: 'token',
        refreshToken: 'refresh',
        user: { id: 'u1', email: 'test@test.com', displayName: 'Test' },
      },
    })

    const router = createTestRouter()
    const pushSpy = vi.spyOn(router, 'push')
    const pinia = createPinia()
    setActivePinia(pinia)

    const wrapper = mount(LoginView, {
      global: { plugins: [pinia, router, createTestI18n()], stubs: { RouterLink: true } },
    })

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(pushSpy).toHaveBeenCalledWith({ name: 'dashboard' })
    })
  })

  it('shows error message on login failure', async () => {
    mockedHttp.post.mockRejectedValueOnce({
      response: { status: 401, data: { message: 'Invalid email or password.' } },
    })

    const wrapper = mountLogin()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('wrong')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.kimi-error').text()).toBe('Invalid email or password.')
    })
  })

  it('shows default error when no message from server', async () => {
    mockedHttp.post.mockRejectedValueOnce(new Error('Network error'))

    const wrapper = mountLogin()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('pw')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.kimi-error').text()).toBe('Login failed, please check your credentials')
    })
  })

  it('shows spinner during loading', async () => {
    let resolveLogin: (v: unknown) => void
    mockedHttp.post.mockImplementation(
      () => new Promise((resolve) => { resolveLogin = resolve }),
    )

    const wrapper = mountLogin()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.find('form').trigger('submit')

    await nextTick()
    expect(wrapper.find('.kimi-spinner').exists()).toBe(true)
    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()

    resolveLogin!({
      data: {
        accessToken: 't',
        refreshToken: 'r',
        user: { id: 'u1', email: 'test@test.com', displayName: 'Test' },
      },
    })
    await vi.waitFor(() => {
      expect(wrapper.find('.kimi-spinner').exists()).toBe(false)
    })
  })
})
