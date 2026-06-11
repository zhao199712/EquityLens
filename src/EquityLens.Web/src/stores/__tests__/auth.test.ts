import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from '../auth'

vi.mock('../../services/http', () => ({
  http: {
    post: vi.fn(),
    get: vi.fn(),
    defaults: { headers: { common: {} } },
  },
}))

import { http } from '../../services/http'

const mockedHttp = vi.mocked(http)

function mockAuthResponse(overrides = {}) {
  return {
    data: {
      accessToken: 'access-123',
      refreshToken: 'refresh-123',
      tokenType: 'Bearer',
      expiresIn: 60,
      user: { id: 'u1', email: 'test@test.com', displayName: 'Test' },
      ...overrides,
    },
  }
}

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
    delete http.defaults.headers.common['Authorization']
  })

  describe('初始狀態', () => {
    it('未登入時 token 為 null', () => {
      const store = useAuthStore()
      expect(store.token).toBeNull()
      expect(store.refreshTokenValue).toBeNull()
      expect(store.user).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })

    it('sessionStorage 有 token 時自動恢復', () => {
      sessionStorage.setItem('auth_token', 'saved-token')
      sessionStorage.setItem('refresh_token', 'saved-refresh')
      const store = useAuthStore()
      expect(store.token).toBe('saved-token')
      expect(store.refreshTokenValue).toBe('saved-refresh')
      expect(store.isAuthenticated).toBe(true)
    })
  })

  describe('register()', () => {
    it('註冊成功後設定 token 與使用者', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())

      const store = useAuthStore()
      await store.register('test@test.com', 'password123', 'Test User')

      expect(mockedHttp.post).toHaveBeenCalledWith('/auth/register', {
        email: 'test@test.com',
        password: 'password123',
        displayName: 'Test User',
      })
      expect(store.token).toBe('access-123')
      expect(store.refreshTokenValue).toBe('refresh-123')
      expect(store.user).toEqual({ id: 'u1', email: 'test@test.com', displayName: 'Test' })
      expect(store.isAuthenticated).toBe(true)
      expect(sessionStorage.getItem('auth_token')).toBe('access-123')
      expect(http.defaults.headers.common['Authorization']).toBe('Bearer access-123')
    })

    it('可選不帶 displayName', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())

      const store = useAuthStore()
      await store.register('test@test.com', 'password123')

      expect(mockedHttp.post).toHaveBeenCalledWith('/auth/register', {
        email: 'test@test.com',
        password: 'password123',
        displayName: undefined,
      })
    })

    it('註冊失敗時拋出錯誤', async () => {
      const error = { response: { status: 409, data: { message: 'Email exists' } } }
      mockedHttp.post.mockRejectedValueOnce(error)

      const store = useAuthStore()
      await expect(store.register('test@test.com', 'password123')).rejects.toThrow()
      expect(store.token).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })
  })

  describe('login()', () => {
    it('登入成功後設定 token 與使用者', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())

      const store = useAuthStore()
      await store.login('test@test.com', 'password123')

      expect(mockedHttp.post).toHaveBeenCalledWith('/auth/login', {
        email: 'test@test.com',
        password: 'password123',
      })
      expect(store.token).toBe('access-123')
      expect(store.user).toEqual({ id: 'u1', email: 'test@test.com', displayName: 'Test' })
      expect(store.isAuthenticated).toBe(true)
      expect(sessionStorage.getItem('auth_token')).toBe('access-123')
      expect(sessionStorage.getItem('refresh_token')).toBe('refresh-123')
    })

    it('登入失敗時拋出錯誤', async () => {
      const error = { response: { status: 401, data: { message: 'Invalid credentials' } } }
      mockedHttp.post.mockRejectedValueOnce(error)

      const store = useAuthStore()
      await expect(store.login('test@test.com', 'wrong')).rejects.toThrow()
      expect(store.token).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })
  })

  describe('logout()', () => {
    it('清除所有狀態並呼叫 API', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())
      const store = useAuthStore()
      await store.login('test@test.com', 'password123')

      mockedHttp.post.mockResolvedValueOnce({}) // logout API
      await store.logout()

      expect(mockedHttp.post).toHaveBeenCalledWith('/auth/logout')
      expect(store.token).toBeNull()
      expect(store.user).toBeNull()
      expect(store.isAuthenticated).toBe(false)
      expect(sessionStorage.getItem('auth_token')).toBeNull()
      expect(http.defaults.headers.common['Authorization']).toBeUndefined()
    })

    it('API 失敗仍清除本地狀態', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())
      const store = useAuthStore()
      await store.login('test@test.com', 'password123')

      mockedHttp.post.mockRejectedValueOnce(new Error('Network error'))
      await store.logout()

      expect(store.token).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })

    it('未登入時不呼叫 logout API', async () => {
      const store = useAuthStore()
      await store.logout()

      expect(mockedHttp.post).not.toHaveBeenCalledWith('/auth/logout')
    })
  })

  describe('refreshAccessToken()', () => {
    it('有 refreshToken 時換取新 token', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())
      const store = useAuthStore()
      await store.login('test@test.com', 'password123')

      mockedHttp.post.mockResolvedValueOnce({
        data: {
          accessToken: 'new-access',
          refreshToken: 'new-refresh',
        },
      })
      const result = await store.refreshAccessToken()

      expect(result).toBe('new-access')
      expect(store.token).toBe('new-access')
      expect(store.refreshTokenValue).toBe('new-refresh')
    })

    it('無 refreshToken 時回傳 null', async () => {
      const store = useAuthStore()
      const result = await store.refreshAccessToken()
      expect(result).toBeNull()
    })

    it('refresh 失敗時清除所有認證', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())
      const store = useAuthStore()
      await store.login('test@test.com', 'password123')

      mockedHttp.post.mockRejectedValueOnce(new Error('expired'))
      const result = await store.refreshAccessToken()

      expect(result).toBeNull()
      expect(store.token).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })
  })

  describe('fetchUser()', () => {
    it('有 token 時取得使用者資訊', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())
      const store = useAuthStore()
      await store.login('test@test.com', 'password123')

      mockedHttp.get.mockResolvedValueOnce({
        data: { user: { id: 'u1', email: 'test@test.com', displayName: 'Updated' } },
      })
      await store.fetchUser()

      expect(mockedHttp.get).toHaveBeenCalledWith('/auth/me')
      expect(store.user?.displayName).toBe('Updated')
    })

    it('無 token 時不呼叫 API', async () => {
      const store = useAuthStore()
      await store.fetchUser()
      expect(mockedHttp.get).not.toHaveBeenCalled()
    })

    it('API 失敗時清除認證', async () => {
      mockedHttp.post.mockResolvedValueOnce(mockAuthResponse())
      const store = useAuthStore()
      await store.login('test@test.com', 'password123')

      mockedHttp.get.mockRejectedValueOnce(new Error('unauthorized'))
      await store.fetchUser()

      expect(store.token).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })
  })

  describe('initializeAuth()', () => {
    it('有 token 時設定 Authorization header', () => {
      sessionStorage.setItem('auth_token', 'my-token')
      const store = useAuthStore()
      store.initializeAuth()

      expect(http.defaults.headers.common['Authorization']).toBe('Bearer my-token')
    })

    it('無 token 時不設定 header', () => {
      const store = useAuthStore()
      store.initializeAuth()

      expect(http.defaults.headers.common['Authorization']).toBeUndefined()
    })
  })
})
