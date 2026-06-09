<script setup lang="ts">
import { ref } from 'vue'
import { defineComponent } from 'vue'
import {
  NButton,
  NIcon,
  NTabs,
  NTabPane,
} from 'naive-ui'
import {
  RefreshOutline,
  CheckmarkCircleOutline,
  CloseCircleOutline,
  TimeOutline,
} from '@vicons/ionicons5'

const activeTab = ref('import')

const importJobs = ref([
  { id: '1', name: '匯入股價資料 2026-06-03', type: 'price', status: 'completed', progress: 100, startedAt: '2026-06-03 06:00:00', duration: '2分30秒' },
  { id: '2', name: '匯入股價資料 2026-06-02', type: 'price', status: 'completed', progress: 100, startedAt: '2026-06-02 06:00:00', duration: '2分15秒' },
  { id: '3', name: '匯入財報資料 Q1 2025', type: 'report', status: 'failed', progress: 45, startedAt: '2026-06-01 14:00:00', duration: '5分20秒' },
])

const riskJobs = ref([
  { id: '4', name: '科技成長型投資組合 VaR 計算', type: 'var', status: 'completed', progress: 100, startedAt: '2026-06-03 14:30:00', duration: '2分15秒' },
  { id: '5', name: '價值型藍籌股組合 VaR 計算', type: 'var', status: 'running', progress: 65, startedAt: '2026-06-03 15:00:00', duration: '1分30秒' },
])

const aiJobs = ref([
  { id: '6', name: 'TSMC 2025 Q1 財報分析', type: 'ai-report', status: 'completed', progress: 100, startedAt: '2026-06-03 10:00:00', duration: '3分45秒' },
  { id: '7', name: 'Apple FY2025 半年報分析', type: 'ai-report', status: 'running', progress: 30, startedAt: '2026-06-03 16:00:00', duration: '45秒' },
])

const JobList = defineComponent({
  props: ['jobs'],
  setup() {
    function getStatusType(status: string): 'success' | 'info' | 'error' | 'warning' | 'default' {
      const map: Record<string, 'success' | 'info' | 'error' | 'warning' | 'default'> = {
        completed: 'success',
        running: 'info',
        failed: 'error',
        pending: 'warning',
      }
      return map[status] || 'default'
    }

    function getStatusLabel(status: string): string {
      const map: Record<string, string> = {
        completed: '完成',
        running: '執行中',
        failed: '失敗',
        pending: '等待中',
      }
      return map[status] || status
    }

    return {
      getStatusType,
      getStatusLabel,
      CheckmarkCircleOutline,
      CloseCircleOutline,
      TimeOutline,
    }
  },
  template: `
    <div style="padding: 0 24px 24px;">
      <div style="display: grid; gap: 12px;">
        <div
          v-for="job in jobs"
          :key="job.id"
          class="glass-card"
          style="padding: 20px;"
        >
          <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px;">
            <div style="display: flex; align-items: center; gap: 12px;">
              <NIcon :size="20" :color="job.status === 'completed' ? '#34d399' : job.status === 'failed' ? '#f87171' : '#60a5fa'">
                <component :is="job.status === 'completed' ? CheckmarkCircleOutline : job.status === 'failed' ? CloseCircleOutline : TimeOutline" />
              </NIcon>
              <span style="color: var(--text-primary); font-weight: 600;">{{ job.name }}</span>
            </div>
            <NTag :type="getStatusType(job.status)" size="small" round>
              {{ getStatusLabel(job.status) }}
            </NTag>
          </div>
          <NProgress
            :percentage="job.progress"
            :status="job.status === 'failed' ? 'error' : job.status === 'completed' ? 'success' : 'default'"
            :show-indicator="true"
            :height="8"
            style="margin-bottom: 12px;"
          />
          <div style="display: flex; justify-content: space-between; color: var(--text-tertiary); font-size: 12px;">
            <span>開始: {{ job.startedAt }}</span>
            <span>耗時: {{ job.duration }}</span>
          </div>
        </div>
      </div>
    </div>
  `,
})
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">Job Management</p>
        <h1>Job 狀態管理</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><RefreshOutline /></NIcon>
        </template>
        重新整理
      </NButton>
    </section>

    <div class="glass-panel" style="padding: 0;">
      <NTabs type="line" class="glass-tabs" style="padding: 20px 24px 0;" v-model:value="activeTab">
        <NTabPane name="import" tab="匯入作業">
          <JobList :jobs="importJobs" />
        </NTabPane>
        <NTabPane name="risk" tab="風險計算作業">
          <JobList :jobs="riskJobs" />
        </NTabPane>
        <NTabPane name="ai" tab="AI 報告作業">
          <JobList :jobs="aiJobs" />
        </NTabPane>
      </NTabs>
    </div>
  </main>
</template>

<style scoped>
:deep(.n-tabs-tab) {
  font-weight: 500;
}

:deep(.n-tabs-tab--active) {
  font-weight: 600;
}

:deep(.n-progress.n-progress--default .n-progress-icon) {
  color: var(--accent-primary);
}

:deep(.n-progress.n-progress--success .n-progress-icon) {
  color: var(--success);
}

:deep(.n-progress.n-progress--error .n-progress-icon) {
  color: var(--danger);
}
</style>
