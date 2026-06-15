<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'

const router = useRouter()
const { t } = useI18n()
const search = ref('')

const riskRuns = ref([
  { id: '1', name: '科技成長型投資組合', model: 'Historical VaR', date: '2026-06-03', var95: '-2.3%', var99: '-3.8%', status: 'completed' },
  { id: '2', name: '價值型藍籌股組合', model: 'Parametric VaR', date: '2026-06-02', var95: '-1.5%', var99: '-2.7%', status: 'completed' },
  { id: '3', name: '全球平衡型組合', model: 'Monte Carlo', date: '2026-06-01', var95: '-1.8%', var99: '-3.1%', status: 'completed' },
  { id: '4', name: '台灣核心持股組合', model: 'Historical VaR', date: '2026-05-30', var95: '-2.1%', var99: '-3.5%', status: 'completed' },
])

const filteredRuns = computed(() => {
  const q = search.value.toLowerCase()
  return riskRuns.value.filter(
    (r) => r.name.toLowerCase().includes(q) || r.model.toLowerCase().includes(q)
  )
})

const showCreate = ref(false)
const newRun = ref({ name: '', model: 'Historical VaR' })

const modelOptions = computed(() => [
  { value: 'Historical VaR', label: t('risk.runs.modelVaR') },
  { value: 'Parametric VaR', label: t('risk.runs.modelES') },
  { value: 'Monte Carlo', label: t('risk.runs.modelStress') },
])

function createRun() {
  riskRuns.value.push({
    id: String(riskRuns.value.length + 1),
    name: newRun.value.name,
    model: newRun.value.model,
    date: new Date().toISOString().split('T')[0],
    var95: '-2.0%',
    var99: '-3.2%',
    status: 'completed',
  })
  showCreate.value = false
  newRun.value = { name: '', model: 'Historical VaR' }
}
</script>

<template>
  <div class="kimi-page-light" style="padding-top: 40px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0">{{ t('risk.runs.title') }}</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('risk.runs.titleEn') }}</span>
        </div>
        <button class="kimi-btn kimi-btn-solid" @click="showCreate = !showCreate">
          {{ showCreate ? t('risk.runs.cancel') : t('risk.runs.create') }}
        </button>
      </div>

      <!-- Create Form -->
      <ScrollReveal v-if="showCreate">
        <div class="kimi-section" style="margin-bottom: 40px; padding: 20px">
          <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">{{ t('risk.runs.createTitle') }}</h3>
          <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 16px; margin-bottom: 16px">
            <input v-model="newRun.name" :placeholder="t('risk.runs.portfolioPlaceholder')" class="kimi-input" />
            <select v-model="newRun.model" class="kimi-input">
              <option v-for="opt in modelOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
            </select>
          </div>
          <button class="kimi-btn kimi-btn-solid" @click="createRun">{{ t('risk.runs.createBtn') }}</button>
        </div>
      </ScrollReveal>

      <!-- Search -->
      <div style="margin-bottom: 24px">
        <input
          v-model="search"
          :placeholder="t('risk.runs.searchPlaceholder')"
          class="kimi-input"
          style="width: 300px"
        />
      </div>

      <!-- Risk Run Grid -->
      <div class="kimi-grid-3">
        <ScrollReveal v-for="(run, i) in filteredRuns" :key="run.id" :delay="i * 0.1">
          <div
            class="kimi-panel"
            style="cursor: pointer; transition: all 0.2s ease"
            @click="router.push({ name: 'risk-run-detail', params: { id: run.id } })"
          >
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
              <span class="kimi-tag">{{ run.model }}</span>
              <span class="kimi-tag" style="border-color: #34d399; color: #34d399">{{ t('common.status.completed') }}</span>
            </div>

            <h3 style="margin: 0 0 4px; font-size: 18px; font-weight: 600">{{ run.name }}</h3>
            <p style="margin: 0 0 16px; font-size: 13px; color: var(--kimi-muted)">{{ run.date }}</p>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
              <div>
                <span style="font-size: 11px; color: var(--kimi-muted); display: block; margin-bottom: 4px">VaR 95%</span>
                <span style="font-size: 16px; font-weight: 600; color: #f87171">{{ run.var95 }}</span>
              </div>
              <div>
                <span style="font-size: 11px; color: var(--kimi-muted); display: block; margin-bottom: 4px">VaR 99%</span>
                <span style="font-size: 16px; font-weight: 600; color: #f87171">{{ run.var99 }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">RISK</span>
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
