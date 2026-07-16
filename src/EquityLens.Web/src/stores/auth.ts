import { defineStore } from 'pinia'
import { http } from '../services/http'

interface User {
  id: string
  email: string
  displayName: string
  role: string
}

interface AuthResponse {
  accessToken: string
  refreshToken: string
  tokenType: string
  expiresIn: number
  user: User
}

interface AuthState {
  user: User | null
  token: string | null
  refreshTokenValue: string | null
}

export const useAuthStore = defineStore('auth', {
  state: (): AuthState => ({
    user: null,
    token: sessionStorage.getItem('auth_token'),
    refreshTokenValue: sessionStorage.getItem('refresh_token'),
  }),

  getters: {
    isAuthenticated: (state) => !!state.token,
  },

  actions: {
    async register(email: string, password: string, displayName?: string) {
      const response = await http.post<AuthResponse>('/auth/register', {
        email,
        password,
        displayName,
      })

      const { user, accessToken, refreshToken } = response.data
      this.user = user
      this.token = accessToken
      this.refreshTokenValue = refreshToken
      sessionStorage.setItem('auth_token', accessToken)
      sessionStorage.setItem('refresh_token', refreshToken)
      http.defaults.headers.common['Authorization'] = `Bearer ${accessToken}`
    },

    async login(email: string, password: string) {
      const response = await http.post<AuthResponse>('/auth/login', {
        email,
        password,
      })

      const { user, accessToken, refreshToken } = response.data
      this.user = user
      this.token = accessToken
      this.refreshTokenValue = refreshToken
      sessionStorage.setItem('auth_token', accessToken)
      sessionStorage.setItem('refresh_token', refreshToken)
      http.defaults.headers.common['Authorization'] = `Bearer ${accessToken}`
    },

    async logout() {
      try {
        if (this.token) {
          await http.post('/auth/logout')
        }
      } catch {
        // ignore - still clear local state
      }

      this.user = null
      this.token = null
      this.refreshTokenValue = null
      sessionStorage.removeItem('auth_token')
      sessionStorage.removeItem('refresh_token')
      delete http.defaults.headers.common['Authorization']
    },

    async refreshAccessToken(): Promise<string | null> {
      if (!this.refreshTokenValue) return null

      try {
        const response = await http.post<AuthResponse>(
          '/auth/refresh',
          { refreshToken: this.refreshTokenValue },
        )

        const { accessToken, refreshToken } = response.data
        this.token = accessToken
        this.refreshTokenValue = refreshToken
        sessionStorage.setItem('auth_token', accessToken)
        sessionStorage.setItem('refresh_token', refreshToken)
        http.defaults.headers.common['Authorization'] = `Bearer ${accessToken}`
        return accessToken
      } catch {
        this.clearAuth()
        return null
      }
    },

    async fetchUser() {
      if (!this.token) return

      try {
        http.defaults.headers.common['Authorization'] = `Bearer ${this.token}`
        const response = await http.get<{ user: User }>('/auth/me')
        this.user = response.data.user
      } catch {
        this.clearAuth()
      }
    },

    clearAuth() {
      this.user = null
      this.token = null
      this.refreshTokenValue = null
      sessionStorage.removeItem('auth_token')
      sessionStorage.removeItem('refresh_token')
      delete http.defaults.headers.common['Authorization']
    },

    initializeAuth() {
      if (this.token) {
        http.defaults.headers.common['Authorization'] = `Bearer ${this.token}`
      }
    },
  },
})
