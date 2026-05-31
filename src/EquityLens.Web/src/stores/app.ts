import { defineStore } from 'pinia'

export const useAppStore = defineStore('app', {
  state: () => ({
    apiStatus: 'idle' as 'idle' | 'checking' | 'online' | 'offline',
  }),
  actions: {
    setApiStatus(status: 'idle' | 'checking' | 'online' | 'offline') {
      this.apiStatus = status
    },
  },
})
