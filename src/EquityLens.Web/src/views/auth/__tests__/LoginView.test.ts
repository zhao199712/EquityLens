import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
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

function mountLogin() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createTestRouter()

  return mount(LoginView, {
    global: {
      plugins: [pinia, router],
      stubs: { RouterLink: true },
    },
  })
}

describe('LoginView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('渲染登入表單', () => {
    const wrapper = mountLogin()

    expect(wrapper.find('h2').text()).toBe('歡迎回來')
    expect(wrapper.find('input[type="email"]').exists()).toBe(true)
    expect(wrapper.find('input[type="password"]').exists()).toBe(true)
    expect(wrapper.find('button[type="submit"]').text()).toContain('登入')
  })

  it('渲染品牌區域', () => {
    const wrapper = mountLogin()
    expect(wrapper.find('.kimi-login-title').text()).toBe('EQUITYLENS')
  })

  it('渲染註冊連結', () => {
    const wrapper = mountLogin()
    const link = wrapper.findComponent({ name: 'RouterLink' })
    expect(link.props('to')).toBe('/register')
  })

  it('空欄位時顯示錯誤訊息', async () => {
    const wrapper = mountLogin()

    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.kimi-error').text()).toBe('請填寫所有欄位')
    expect(mockedHttp.post).not.toHaveBeenCalled()
  })

  it('只填 email 不填 password 時顯示錯誤', async () => {
    const wrapper = mountLogin()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.kimi-error').text()).toBe('請填寫所有欄位')
  })

  it('登入成功時跳轉到 dashboard', async () => {
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
      global: { plugins: [pinia, router], stubs: { RouterLink: true } },
    })

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(pushSpy).toHaveBeenCalledWith({ name: 'dashboard' })
    })
  })

  it('登入失敗時顯示錯誤訊息', async () => {
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

  it('登入失敗無 message 時顯示預設錯誤', async () => {
    mockedHttp.post.mockRejectedValueOnce(new Error('Network error'))

    const wrapper = mountLogin()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[type="password"]').setValue('pw')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.kimi-error').text()).toBe('登入失敗，請檢查帳號密碼')
    })
  })

  it('loading 時按鈕顯示 spinner', async () => {
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
