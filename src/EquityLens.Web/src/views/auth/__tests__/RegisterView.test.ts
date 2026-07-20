import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import en from '../../../locales/en'
import RegisterView from '../RegisterView.vue'

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

function mountRegister() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createTestRouter()
  const i18n = createTestI18n()

  return mount(RegisterView, {
    global: {
      plugins: [pinia, router, i18n],
      stubs: { RouterLink: true },
    },
  })
}

describe('RegisterView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('renders register form', () => {
    const wrapper = mountRegister()

    expect(wrapper.find('h2').text()).toBe('Create Account')
    expect(wrapper.find('input[placeholder="Optional"]').exists()).toBe(true)
    expect(wrapper.find('input[type="email"]').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="••••••••"]').exists()).toBe(true)
    expect(wrapper.find('button[type="submit"]').text()).toContain('Register')
  })

  it('renders login link', () => {
    const wrapper = mountRegister()
    const link = wrapper.findComponent({ name: 'RouterLink' })
    expect(link.props('to')).toBe('/login')
  })

  it('shows error when fields are empty', async () => {
    const wrapper = mountRegister()
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.prestige-error').text()).toBe('Please fill in all fields')
    expect(mockedHttp.post).not.toHaveBeenCalled()
  })

  it('shows error when password is less than 6 characters', async () => {
    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('12345')
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.prestige-error').text()).toBe('Password must be at least 6 characters')
  })

  it('shows error when passwords do not match', async () => {
    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.findAll('input[type="password"]')[1].setValue('password456')
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.prestige-error').text()).toBe('Passwords do not match')
  })

  it('redirects to dashboard on successful registration', async () => {
    mockedHttp.post.mockResolvedValueOnce({
      data: {
        accessToken: 'token',
        refreshToken: 'refresh',
        user: { id: 'u1', email: 'test@test.com', displayName: 'Test', role: 'User' },
      },
    })

    const router = createTestRouter()
    const pushSpy = vi.spyOn(router, 'push')
    const pinia = createPinia()
    setActivePinia(pinia)

    const wrapper = mount(RegisterView, {
      global: { plugins: [pinia, router, createTestI18n()], stubs: { RouterLink: true } },
    })

    await wrapper.find('input[placeholder="Optional"]').setValue('Test User')
    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.findAll('input[type="password"]')[1].setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(mockedHttp.post).toHaveBeenCalledWith('/auth/register', {
        email: 'test@test.com',
        password: 'password123',
        displayName: 'Test User',
      })
      expect(pushSpy).toHaveBeenCalledWith({ name: 'dashboard' })
    })
  })

  it('sends undefined displayName when not filled', async () => {
    mockedHttp.post.mockResolvedValueOnce({
      data: {
        accessToken: 'token',
        refreshToken: 'refresh',
        user: { id: 'u1', email: 'test@test.com', displayName: 'test', role: 'User' },
      },
    })

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.findAll('input[type="password"]')[1].setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(mockedHttp.post).toHaveBeenCalledWith('/auth/register', {
        email: 'test@test.com',
        password: 'password123',
        displayName: undefined,
      })
    })
  })

  it('shows email taken error on 409', async () => {
    mockedHttp.post.mockRejectedValueOnce({
      response: { status: 409, data: { message: 'Email already exists' } },
    })

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.findAll('input[type="password"]')[1].setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.prestige-error').text()).toBe('This email is already registered')
    })
  })

  it('shows backend message on other errors', async () => {
    mockedHttp.post.mockRejectedValueOnce({
      response: { status: 500, data: { message: 'Server error' } },
    })

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.findAll('input[type="password"]')[1].setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.prestige-error').text()).toBe('Server error')
    })
  })

  it('shows default error on network error', async () => {
    mockedHttp.post.mockRejectedValueOnce(new Error('Network error'))

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.findAll('input[type="password"]')[1].setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.prestige-error').text()).toBe('Registration failed, please try again later')
    })
  })
})
