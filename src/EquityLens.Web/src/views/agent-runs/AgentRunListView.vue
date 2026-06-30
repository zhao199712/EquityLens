<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import { listAgentRuns, type AgentRunListItem } from '../../services/agentRuns'

const router = useRouter()
const search = ref('')
const runs = ref<AgentRunListItem[]>([])
const loading = ref(false)
const error = ref('')
const filterWorkflow = ref('')
const filterStatus = ref('')

const filteredRuns = computed(() => {
  const q = search.value.toLowerCase()
  return runs.value.filter(
    (r) =>
      (r.workflowType.toLowerCase().includes(q) ||
        r.agentType.toLowerCase().includes(q) ||
        r.id.toLowerCase().includes(q)) &&
      (!filterWorkflow.value || r.workflowType === filterWorkflow.value) &&
      (!filterStatus.value || r.status === filterStatus.value),
  )
})

onMounted(async () => {
  loading.value = true
  try {
    runs.value = await listAgentRuns({ limit: 50 })
  } catch (e) {
    error.value = '無法載入 Agent Run 列表，請稍後再試。'
  } finally {
    loading.value = false
  }
})

function formatDate(iso: string | null) {
  if (!iso) return '-'
  return new Date(iso).toLocaleString('zh-TW')
}

function statusColor(status: string) {
  switch (status) {
    case 'Succeeded': return '#34d399'
    case 'Failed': return '#f87171'
    case 'Running': return '#60a5fa'
    case 'Cancelled': return '#999999'
    case 'Pending': return '#fbbf24'
    case 'WaitingForFeedback': return '#c084fc'
    default: return '#666666'
  }
}
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px; padding-bottom: 80px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">Agent Runs</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">WORKFLOW EXECUTION HISTORY</span>
        </div>
      </div>

      <!-- Filters -->
      <div style="display: flex; gap: 12px; margin-bottom: 24px; flex-wrap: wrap">
        <input
          v-model="search"
          placeholder="搜尋 workflow / agent / ID..."
          class="kimi-input-dark"
          style="width: 300px"
        />
        <select v-model="filterWorkflow" class="kimi-input-dark" style="width: 180px">
          <option value="">全部 Workflow</option>
          <option value="CriticReview">CriticReview</option>
        </select>
        <select v-model="filterStatus" class="kimi-input-dark" style="width: 160px">
          <option value="">全部狀態</option>
          <option value="Pending">Pending</option>
          <option value="Running">Running</option>
          <option value="Succeeded">Succeeded</option>
          <option value="Failed">Failed</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </div>

      <!-- Loading / Error -->
      <div v-if="loading" style="color: #666666; font-size: 14px">載入中...</div>
      <div v-else-if="error" style="color: #f87171; font-size: 14px; margin-bottom: 24px">{{ error }}</div>

      <!-- Run List -->
      <div v-else style="display: flex; flex-direction: column; gap: 12px">
        <ScrollReveal v-for="(run, i) in filteredRuns" :key="run.id" :delay="i * 0.05">
          <div
            class="kimi-panel-dark"
            style="cursor: pointer; transition: all 0.2s ease; padding: 20px"
            @click="router.push({ name: 'agent-run-detail', params: { id: run.id } })"
          >
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px">
              <div style="display: flex; align-items: center; gap: 8px">
                <span class="kimi-tag-dark" style="font-family: monospace; font-size: 11px">{{ run.workflowType }}</span>
                <span class="kimi-tag-dark">{{ run.agentType }}</span>
              </div>
              <span
                class="kimi-tag-dark"
                :style="{ borderColor: statusColor(run.status), color: statusColor(run.status) }"
              >
                {{ run.status }}
              </span>
            </div>

            <div style="font-family: monospace; font-size: 12px; color: #999999; margin-bottom: 12px">
              {{ run.id }}
            </div>

            <div v-if="run.errorMessage" style="color: #f87171; font-size: 13px; margin-bottom: 12px">
              {{ run.errorMessage }}
            </div>

            <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px">
              <div>
                <div style="font-size: 11px; color: #666666; margin-bottom: 2px">Nodes</div>
                <div style="font-size: 14px; color: #FFFFFF; font-weight: 600">{{ run.nodeCount }}</div>
              </div>
              <div>
                <div style="font-size: 11px; color: #666666; margin-bottom: 2px">Events</div>
                <div style="font-size: 14px; color: #FFFFFF; font-weight: 600">{{ run.eventCount }}</div>
              </div>
              <div>
                <div style="font-size: 11px; color: #666666; margin-bottom: 2px">Tool Calls</div>
                <div style="font-size: 14px; color: #FFFFFF; font-weight: 600">{{ run.toolCallCount }}</div>
              </div>
              <div>
                <div style="font-size: 11px; color: #666666; margin-bottom: 2px">Created</div>
                <div style="font-size: 13px; color: #FFFFFF">{{ formatDate(run.createdAtUtc) }}</div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Empty state -->
        <div v-if="!loading && filteredRuns.length === 0" style="color: #666666; margin-top: 24px; text-align: center; padding: 40px">
          沒有符合條件的 Agent Run。
        </div>
      </div>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer kimi-footer-dark">
      <span style="color: #666666">EQUITYLENS 2026</span>
      <span class="kimi-font-mono" style="color: #333333">AGENT RUNS</span>
      <span style="color: #666666">數據僅供參考</span>
    </footer>
  </div>
</template>
