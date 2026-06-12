import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { router } from '../router'

export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5034/api',
  timeout: 10000,
})

const token = sessionStorage.getItem('auth_token')
if (token) {
  http.defaults.headers.common['Authorization'] = `Bearer ${token}`
}

let isRefreshing = false
let failedQueue: Array<{
  resolve: (token: string) => void
  reject: (error: unknown) => void
}> = []

function processQueue(error: unknown, token: string | null) {
  failedQueue.forEach((prom) => {
    if (error || !token) {
      prom.reject(error)
    } else {
      prom.resolve(token)
    }
  })
  failedQueue = []
}

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean }

    if (error.response?.status === 401 && !originalRequest._retry) {
      if (isRefreshing) {
        return new Promise<string>((resolve, reject) => {
          failedQueue.push({ resolve, reject })
        }).then((newToken) => {
          originalRequest.headers['Authorization'] = `Bearer ${newToken}`
          return http(originalRequest)
        })
      }

      originalRequest._retry = true
      isRefreshing = true

      const refreshToken = sessionStorage.getItem('refresh_token')
      if (!refreshToken) {
        isRefreshing = false
        sessionStorage.removeItem('auth_token')
        sessionStorage.removeItem('refresh_token')
        router.push({ name: 'login' })
        return Promise.reject(error)
      }

      try {
        const { data } = await axios.post(
          `${http.defaults.baseURL}/auth/refresh`,
          { refreshToken },
        )

        const newToken = data.accessToken
        sessionStorage.setItem('auth_token', newToken)
        sessionStorage.setItem('refresh_token', data.refreshToken)
        http.defaults.headers.common['Authorization'] = `Bearer ${newToken}`

        processQueue(null, newToken)

        originalRequest.headers['Authorization'] = `Bearer ${newToken}`
        return http(originalRequest)
      } catch (refreshError) {
        processQueue(refreshError, null)
        sessionStorage.removeItem('auth_token')
        sessionStorage.removeItem('refresh_token')
        delete http.defaults.headers.common['Authorization']
        router.push({ name: 'login' })
        return Promise.reject(refreshError)
      } finally {
        isRefreshing = false
      }
    }

    return Promise.reject(error)
  },
)
