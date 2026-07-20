<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import { getPortfolios, type PortfolioListItem } from '../../services/risk.ts'

const router = useRouter()
const search = ref('')
const portfolios = ref<PortfolioListItem[]>([])
const loading = ref(false)
const error = ref('')

const filteredPortfolios = computed(() => {
  const q = search.value.toLowerCase()
  return portfolios.value.filter(
    (r) =>
      r.name.toLowerCase().includes(q) ||
      (r.description?.toLowerCase() ?? '').includes(q),
  )
})

onMounted(async () => {
  loading.value = true
  try {
    portfolios.value = await getPortfolios()
  } catch (e) {
    error.value = '無法載入投資組合列表,請稍後再試。'
  } finally {
    loading.value = false
  }
})

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('zh-TW')
}
</script>

<template>
  <div class="prestige-page">
    <div class="prestige-section">
      <!-- Header -->
      <div class="prestige-section-head">
        <div>
          <span class="prestige-label">Risk Analysis Runs</span>
          <h2 class="prestige-section-title">風險分析</h2>
        </div>
      </div>

      <!-- Search -->
      <div class="search-row">
        <input v-model="search" placeholder="搜尋投資組合..." class="prestige-input search-input" />
      </div>

      <!-- Error -->
      <div v-if="error" class="prestige-error" style="margin-bottom: 20px">{{ error }}</div>

      <!-- Loading skeleton -->
      <div v-if="loading" class="run-grid">
        <div v-for="i in 3" :key="i" class="prestige-skeleton" />
      </div>

      <!-- Portfolio Grid -->
      <div v-else class="run-grid">
        <ScrollReveal v-for="(run, i) in filteredPortfolios" :key="run.id" :delay="i * 0.06">
          <div
            class="prestige-panel prestige-panel-pad run-card"
            @click="router.push({ name: 'risk-run-detail', params: { id: run.id } })"
          >
            <div class="card-top">
              <span class="prestige-tag prestige-mono">{{ run.baseCurrency }}</span>
              <span class="prestige-tag available">
                <span class="status-dot" />
                AVAILABLE
              </span>
            </div>

            <h3 class="card-title">{{ run.name }}</h3>
            <p class="card-desc">{{ run.description || '無描述' }}</p>

            <div class="stat-grid">
              <div>
                <span class="stat-label">持股數</span>
                <span class="stat-value prestige-mono">{{ run.holdingCount }}</span>
              </div>
              <div>
                <span class="stat-label">更新於</span>
                <span class="stat-value prestige-mono">{{ formatDate(run.updatedAtUtc) }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div v-if="!loading && !error && filteredPortfolios.length === 0" class="prestige-empty">
        沒有符合條件的投資組合
      </div>
    </div>
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.search-row {
  margin-bottom: 24px;
}

.search-input {
  max-width: 320px;
}

.run-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 16px;
}

.run-card {
  cursor: pointer;
  transition: transform 0.25s ease, border-color 0.3s ease;
}

.run-card:hover {
  transform: translateY(-2px);
  border-color: rgba(201, 168, 106, 0.45);
}

.card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--up);
}

.card-title {
  margin: 0 0 4px;
  font-family: var(--serif);
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.card-desc {
  margin: 0 0 16px;
  font-size: 13px;
  color: var(--muted);
  min-height: 18px;
}

.stat-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--gold-border-soft);
}

.stat-label {
  display: block;
  font-size: 11px;
  letter-spacing: 0.14em;
  color: var(--muted);
  margin-bottom: 4px;
}

.stat-value {
  font-size: 15px;
  font-weight: 600;
  color: var(--ivory);
}
</style>
