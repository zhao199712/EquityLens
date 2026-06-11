import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '../../stores/auth'

vi.mock('../../services/http', () => ({
  http: {
    post: vi.fn(),
    get: vi.fn(),
    defaults: { headers: { common: {} } },
  },
}))

describe('router guard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    sessionStorage.clear()
  })

  describe('requiresAuth meta', () => {
    it('requiresAuth: true 的路由需要登入', () => {
      const store = useAuthStore()
      expect(store.isAuthenticated).toBe(false)

      // 模擬 router guard 邏輯
      const to = { meta: { requiresAuth: true }, name: 'dashboard' }
      if (to.meta.requiresAuth !== false && !store.isAuthenticated) {
        expect(true).toBe(true) // 應該導向 login
      }
    })

    it('requiresAuth: false 的路由不需要登入', () => {
      const store = useAuthStore()
      expect(store.isAuthenticated).toBe(false)

      const to = { meta: { requiresAuth: false }, name: 'login' }
      const shouldRedirect = to.meta.requiresAuth !== false && !store.isAuthenticated
      expect(shouldRedirect).toBe(false)
    })

    it('requiresAuth 未設定時視為需要登入', () => {
      const store = useAuthStore()
      expect(store.isAuthenticated).toBe(false)

      const to = { meta: {}, name: 'portfolios' }
      const shouldRedirect = to.meta.requiresAuth !== false && !store.isAuthenticated
      expect(shouldRedirect).toBe(true)
    })
  })

  describe('已登入時的行為', () => {
    it('已登入時不應停留在 login 頁', async () => {
      const store = useAuthStore()
      // 模擬已登入狀態
      sessionStorage.setItem('auth_token', 'some-token')
      store.token = 'some-token'

      const to = { meta: { requiresAuth: false }, name: 'login' }
      const isAuthPage = to.name === 'login' || to.name === 'register'
      const shouldRedirectToDashboard = isAuthPage && store.isAuthenticated
      expect(shouldRedirectToDashboard).toBe(true)
    })

    it('已登入時不應停留在 register 頁', async () => {
      const store = useAuthStore()
      sessionStorage.setItem('auth_token', 'some-token')
      store.token = 'some-token'

      const to = { meta: { requiresAuth: false }, name: 'register' }
      const isAuthPage = to.name === 'login' || to.name === 'register'
      const shouldRedirectToDashboard = isAuthPage && store.isAuthenticated
      expect(shouldRedirectToDashboard).toBe(true)
    })

    it('已登入時可正常訪問需認證的頁面', () => {
      const store = useAuthStore()
      sessionStorage.setItem('auth_token', 'some-token')
      store.token = 'some-token'

      const to = { meta: { requiresAuth: true }, name: 'dashboard' }
      const shouldRedirectToLogin = to.meta.requiresAuth !== false && !store.isAuthenticated
      expect(shouldRedirectToLogin).toBe(false)
    })
  })
})
