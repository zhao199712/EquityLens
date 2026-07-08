import { ref, onUnmounted } from 'vue'
import { getAgentRun, type AgentRunDetail } from '../services/agentRuns'

const POLLING_INTERVAL = 2000
const TERMINAL_STATUSES = new Set(['Succeeded', 'Failed', 'Cancelled'])

export function useAgentRunPolling(runId: string) {
  const agentRun = ref<AgentRunDetail | null>(null)
  const isLoading = ref(true)
  const error = ref('')
  let timer: ReturnType<typeof setInterval> | null = null

  async function fetchRun() {
    try {
      agentRun.value = await getAgentRun(runId)
      error.value = ''
      if (TERMINAL_STATUSES.has(agentRun.value.run.status)) {
        stopPolling()
      }
    } catch {
      error.value = '無法載入 Agent Run。'
    } finally {
      isLoading.value = false
    }
  }

  function startPolling() {
    stopPolling()
    fetchRun()
    timer = setInterval(fetchRun, POLLING_INTERVAL)
  }

  function stopPolling() {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  onUnmounted(stopPolling)

  return { agentRun, isLoading, error, startPolling, stopPolling, refresh: fetchRun }
}
