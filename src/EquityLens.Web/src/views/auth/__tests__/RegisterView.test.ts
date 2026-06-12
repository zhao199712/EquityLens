import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
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

function mountRegister() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createTestRouter()

  return mount(RegisterView, {
    global: {
      plugins: [pinia, router],
      stubs: { RouterLink: true },
    },
  })
}

describe('RegisterView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('渲染註冊表單', () => {
    const wrapper = mountRegister()

    expect(wrapper.find('h2').text()).toBe('建立帳號')
    expect(wrapper.find('input[placeholder="選填"]').exists()).toBe(true)
    expect(wrapper.find('input[type="email"]').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="至少 6 個字元"]').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="再次輸入密碼"]').exists()).toBe(true)
    expect(wrapper.find('button[type="submit"]').text()).toContain('註冊')
  })

  it('渲染登入連結', () => {
    const wrapper = mountRegister()
    const link = wrapper.findComponent({ name: 'RouterLink' })
    expect(link.props('to')).toBe('/login')
  })

  it('空欄位時顯示錯誤', async () => {
    const wrapper = mountRegister()
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.kimi-error').text()).toBe('請填寫所有欄位')
    expect(mockedHttp.post).not.toHaveBeenCalled()
  })

  it('密碼不足 6 碼時顯示錯誤', async () => {
    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[placeholder="至少 6 個字元"]').setValue('12345')
    await wrapper.find('input[placeholder="再次輸入密碼"]').setValue('12345')
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.kimi-error').text()).toBe('密碼至少需要 6 個字元')
  })

  it('密碼不一致時顯示錯誤', async () => {
    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[placeholder="至少 6 個字元"]').setValue('password123')
    await wrapper.find('input[placeholder="再次輸入密碼"]').setValue('password456')
    await wrapper.find('form').trigger('submit')

    expect(wrapper.find('.kimi-error').text()).toBe('兩次密碼輸入不一致')
  })

  it('註冊成功時跳轉到 dashboard', async () => {
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

    const wrapper = mount(RegisterView, {
      global: { plugins: [pinia, router], stubs: { RouterLink: true } },
    })

    await wrapper.find('input[placeholder="選填"]').setValue('Test User')
    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[placeholder="至少 6 個字元"]').setValue('password123')
    await wrapper.find('input[placeholder="再次輸入密碼"]').setValue('password123')
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

  it('不填顯示名稱時 displayName 傳 undefined', async () => {
    mockedHttp.post.mockResolvedValueOnce({
      data: {
        accessToken: 'token',
        refreshToken: 'refresh',
        user: { id: 'u1', email: 'test@test.com', displayName: 'test' },
      },
    })

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[placeholder="至少 6 個字元"]').setValue('password123')
    await wrapper.find('input[placeholder="再次輸入密碼"]').setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(mockedHttp.post).toHaveBeenCalledWith('/auth/register', {
        email: 'test@test.com',
        password: 'password123',
        displayName: undefined,
      })
    })
  })

  it('409 錯誤時顯示 Email 已被註冊', async () => {
    mockedHttp.post.mockRejectedValueOnce({
      response: { status: 409, data: { message: 'Email already exists' } },
    })

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[placeholder="至少 6 個字元"]').setValue('password123')
    await wrapper.find('input[placeholder="再次輸入密碼"]').setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.kimi-error').text()).toBe('此 Email 已被註冊')
    })
  })

  it('其他錯誤時顯示後端 message', async () => {
    mockedHttp.post.mockRejectedValueOnce({
      response: { status: 500, data: { message: 'Server error' } },
    })

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[placeholder="至少 6 個字元"]').setValue('password123')
    await wrapper.find('input[placeholder="再次輸入密碼"]').setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.kimi-error').text()).toBe('Server error')
    })
  })

  it('網路錯誤時顯示預設訊息', async () => {
    mockedHttp.post.mockRejectedValueOnce(new Error('Network error'))

    const wrapper = mountRegister()

    await wrapper.find('input[type="email"]').setValue('test@test.com')
    await wrapper.find('input[placeholder="至少 6 個字元"]').setValue('password123')
    await wrapper.find('input[placeholder="再次輸入密碼"]').setValue('password123')
    await wrapper.find('form').trigger('submit')

    await vi.waitFor(() => {
      expect(wrapper.find('.kimi-error').text()).toBe('註冊失敗，請稍後再試')
    })
  })
})
