import { ref, onUnmounted, computed, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { getAgentRun, type AgentRunDetail } from '../services/agentRuns'

const POLLING_INTERVAL = 2000
const TERMINAL_STATUSES = new Set(['Succeeded', 'Failed', 'Cancelled'])

export function useAgentRunPolling(runId: MaybeRefOrGetter<string>) {
  const agentRun = ref<AgentRunDetail | null>(null)
  const isLoading = ref(true)
  const isPolling = ref(false)
  const error = ref('')
  let timer: ReturnType<typeof setInterval> | null = null

  const isTerminalStatus = computed(() => {
    if (!agentRun.value) return false
    return TERMINAL_STATUSES.has(agentRun.value.run.status)
  })

  async function fetchRun() {
    try {
      agentRun.value = await getAgentRun(toValue(runId))
      error.value = ''
      if (isTerminalStatus.value) {
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
    isPolling.value = true
    fetchRun()
    timer = setInterval(fetchRun, POLLING_INTERVAL)
  }

  function stopPolling() {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
    isPolling.value = false
  }

  function refresh() {
    startPolling()
  }

  onUnmounted(stopPolling)
  watch(() => toValue(runId), () => {
    agentRun.value = null
    isLoading.value = true
    startPolling()
  })

  return {
    agentRun,
    isLoading,
    isPolling,
    isTerminalStatus,
    error,
    startPolling,
    stopPolling,
    refresh,
    fetchRun,
  }
}
