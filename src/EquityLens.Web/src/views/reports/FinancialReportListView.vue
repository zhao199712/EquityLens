<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'

const { t } = useI18n()
const router = useRouter()
const search = ref('')

const reports = ref([
  { id: '1', name: 'TSMC 2025 Q1 財報分析', company: '台積電 (2330)', type: 'AI Memo', confidence: '高', date: '2026-06-03' },
  { id: '2', name: 'Apple FY2025 半年報', company: 'Apple (AAPL)', type: 'Financial Report', confidence: '中', date: '2026-06-02' },
  { id: '3', name: 'NVIDIA 風險評估報告', company: 'NVIDIA (NVDA)', type: 'Risk Report', confidence: '高', date: '2026-06-01' },
  { id: '4', name: 'TSMC 2024 Q4 財報分析', company: '台積電 (2330)', type: 'AI Memo', confidence: '高', date: '2026-05-15' },
])

const filteredReports = ref(reports.value)
const showCreate = ref(false)
const newReport = ref({ name: '', company: '', type: 'AI Memo' })

function filterReports() {
  const q = search.value.toLowerCase()
  filteredReports.value = reports.value.filter(
    (r) => r.name.toLowerCase().includes(q) || r.company.toLowerCase().includes(q)
  )
}

function createReport() {
  reports.value.push({
    id: String(reports.value.length + 1),
    name: newReport.value.name,
    company: newReport.value.company,
    type: newReport.value.type,
    confidence: '中',
    date: new Date().toISOString().split('T')[0],
  })
  filterReports()
  showCreate.value = false
  newReport.value = { name: '', company: '', type: 'AI Memo' }
}
</script>

<template>
  <div class="kimi-page-light" style="padding-top: 40px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0">{{ t('reports.list.title') }}</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('reports.list.titleEn') }}</span>
        </div>
        <button class="kimi-btn kimi-btn-solid" @click="showCreate = !showCreate">
          {{ showCreate ? t('reports.list.cancel') : t('reports.list.create') }}
        </button>
      </div>

      <!-- Create Form -->
      <ScrollReveal v-if="showCreate">
        <div class="kimi-section" style="margin-bottom: 40px; padding: 20px">
          <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">{{ t('reports.list.createTitle') }}</h3>
          <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 16px">
            <input v-model="newReport.name" :placeholder="t('reports.list.tickerPlaceholder')" class="kimi-input" />
            <input v-model="newReport.company" placeholder="公司 (例: 台積電 2330)" class="kimi-input" />
            <select v-model="newReport.type" class="kimi-input">
              <option>AI Memo</option>
              <option>Financial Report</option>
              <option>Risk Report</option>
            </select>
          </div>
          <button class="kimi-btn kimi-btn-solid" @click="createReport">{{ t('reports.list.createBtn') }}</button>
        </div>
      </ScrollReveal>

      <!-- Search -->
      <div style="margin-bottom: 24px">
        <input
          v-model="search"
          :placeholder="t('reports.list.searchPlaceholder')"
          class="kimi-input"
          style="width: 300px"
          @input="filterReports"
        />
      </div>

      <!-- Report Grid -->
      <div class="kimi-grid-3">
        <ScrollReveal v-for="(report, i) in filteredReports" :key="report.id" :delay="i * 0.1">
          <div
            class="kimi-panel"
            style="cursor: pointer; transition: all 0.2s ease"
            @click="router.push({ name: 'financial-report-detail', params: { id: report.id } })"
          >
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
              <span class="kimi-tag">{{ report.type }}</span>
              <span class="kimi-tag">{{ t('reports.list.confidence') }}: {{ report.confidence }}</span>
            </div>

            <h3 style="margin: 0 0 4px; font-size: 18px; font-weight: 600">{{ report.name }}</h3>
            <p style="margin: 0 0 16px; font-size: 13px; color: var(--kimi-muted)">{{ report.company }}</p>

            <div style="border-top: 1px solid var(--kimi-border-light); padding-top: 12px">
              <span style="font-size: 12px; color: var(--kimi-muted)">{{ report.date }}</span>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">REPORTS</span>
      <span>{{ t('dashboard.footer') }}</span>
    </footer>
  </div>
</template>

<style scoped>
.kimi-input {
  padding: 8px 12px;
  font-size: 14px;
  font-family: var(--kimi-font-body);
  border: 1px solid var(--kimi-border-light);
  background: transparent;
  color: var(--kimi-text-light);
  outline: none;
  transition: border-color 0.2s;
  width: 100%;
}
.kimi-input:focus {
  border-color: #000000;
}
.kimi-input::placeholder {
  color: #999999;
}
select.kimi-input {
  appearance: none;
  cursor: pointer;
}
</style>
