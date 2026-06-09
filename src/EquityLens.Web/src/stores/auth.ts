import { defineStore } from 'pinia'
import { http } from '../services/http'

interface User {
  id: string
  email: string
  displayName: string
  role: string
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
    isAdmin: (state) => state.user?.role === 'Admin',
  },

  actions: {
    async login(email: string, password: string) {
      const response = await http.post<{
        user: User
        accessToken: string
        refreshToken: string
      }>('/auth/login', {
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
        const response = await http.post<{ accessToken: string; refreshToken: string }>(
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
        this.logout()
        return null
      }
    },

    async fetchUser() {
      if (!this.token) return

      try {
        http.defaults.headers.common['Authorization'] = `Bearer ${this.token}`
        const response = await http.get<User>('/auth/me')
        this.user = response.data
      } catch {
        this.logout()
      }
    },

    initializeAuth() {
      if (this.token) {
        http.defaults.headers.common['Authorization'] = `Bearer ${this.token}`
      }
    },
  },
})
