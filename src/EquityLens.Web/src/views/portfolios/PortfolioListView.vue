<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import {
  getPortfolios,
  createPortfolio,
  deletePortfolio,
  getPortfolioValuation,
  type PortfolioListItem,
  type PortfolioValuationResponse,
} from '../../services/risk.ts'

const router = useRouter()
const search = ref('')
const portfolios = ref<PortfolioListItem[]>([])
const valuations = ref<Map<string, PortfolioValuationResponse>>(new Map())
const loading = ref(false)
const error = ref('')
const showCreate = ref(false)
const creating = ref(false)
const newPortfolio = ref({ name: '', description: '', baseCurrency: 'TWD' })

const filteredPortfolios = computed(() => {
  const q = search.value.toLowerCase()
  return portfolios.value.filter(
    (p) =>
      p.name.toLowerCase().includes(q) ||
      (p.description?.toLowerCase() ?? '').includes(q),
  )
})

async function loadValuations(items: PortfolioListItem[]) {
  const results = await Promise.allSettled(
    items.map((p) => getPortfolioValuation(p.id).then((v) => ({ id: p.id, v }))),
  )
  results.forEach((r) => {
    if (r.status === 'fulfilled') {
      valuations.value.set(r.value.id, r.value.v)
    }
  })
}

async function loadPortfolios() {
  loading.value = true
  error.value = ''
  try {
    const items = await getPortfolios()
    portfolios.value = items
    await loadValuations(items)
  } catch (e) {
    error.value = '無法載入投資組合列表,請稍後再試。'
  } finally {
    loading.value = false
  }
}

onMounted(loadPortfolios)

async function handleCreatePortfolio() {
  if (!newPortfolio.value.name.trim()) return
  creating.value = true
  error.value = ''
  try {
    const created = await createPortfolio({
      name: newPortfolio.value.name,
      description: newPortfolio.value.description || undefined,
      baseCurrency: newPortfolio.value.baseCurrency,
    })
    portfolios.value.unshift({
      id: created.id,
      name: created.name,
      description: created.description,
      baseCurrency: created.baseCurrency,
      holdingCount: created.holdings.length,
      createdAtUtc: created.createdAtUtc,
      updatedAtUtc: created.updatedAtUtc,
    })
    newPortfolio.value = { name: '', description: '', baseCurrency: 'TWD' }
    showCreate.value = false
  } catch (e) {
    error.value = '建立投資組合失敗,請稍後再試。'
  } finally {
    creating.value = false
  }
}

async function handleDeletePortfolio(id: string) {
  if (!confirm('確定要刪除此投資組合嗎?')) return
  error.value = ''
  try {
    await deletePortfolio(id)
    portfolios.value = portfolios.value.filter((p) => p.id !== id)
  } catch (e) {
    error.value = '刪除投資組合失敗,請稍後再試。'
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('zh-TW')
}

function formatMoney(n: number, currency = '') {
  if (n === 0) return currency ? `0 ${currency}` : '0'
  const abs = Math.abs(n)
  const sign = n < 0 ? '-' : ''
  const suffix = currency ? ` ${currency}` : ''
  if (abs >= 1_000_000_000) return `${sign}${(abs / 1_000_000_000).toFixed(2)}B${suffix}`
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(2)}M${suffix}`
  if (abs >= 1_000) return `${sign}${(abs / 1_000).toFixed(2)}K${suffix}`
  return `${sign}${abs.toFixed(2)}${suffix}`
}

function formatPercent(n: number | null) {
  if (n === null || n === undefined) return '—'
  return `${n >= 0 ? '+' : ''}${(n * 100).toFixed(2)}%`
}

function pnlClass(n: number | null | undefined) {
  if (n === null || n === undefined) return 'neutral'
  return n >= 0 ? 'up' : 'down'
}
</script>

<template>
  <div class="prestige-page">
    <div class="prestige-section">
      <!-- Header -->
      <div class="prestige-section-head">
        <div>
          <span class="prestige-label">Portfolio Management</span>
          <h2 class="prestige-section-title">投資組合</h2>
        </div>
        <button class="prestige-btn prestige-btn-solid" @click="showCreate = !showCreate">
          {{ showCreate ? '取消' : '+ 建立組合' }}
        </button>
      </div>

      <!-- Create Form -->
      <ScrollReveal v-if="showCreate">
        <div class="prestige-panel prestige-panel-pad create-panel">
          <span class="prestige-label">New Portfolio</span>
          <div class="create-grid">
            <input v-model="newPortfolio.name" placeholder="組合名稱" class="prestige-input" />
            <input v-model="newPortfolio.description" placeholder="描述(選填)" class="prestige-input" />
            <select v-model="newPortfolio.baseCurrency" class="prestige-input">
              <option value="TWD">TWD</option>
              <option value="USD">USD</option>
              <option value="HKD">HKD</option>
              <option value="JPY">JPY</option>
            </select>
          </div>
          <button class="prestige-btn" :disabled="creating" @click="handleCreatePortfolio">
            {{ creating ? '建立中...' : '建立' }}
          </button>
        </div>
      </ScrollReveal>

      <!-- Search -->
      <div class="search-row">
        <input v-model="search" placeholder="搜尋投資組合..." class="prestige-input search-input" />
      </div>

      <!-- Error -->
      <div v-if="error" class="prestige-error" style="margin-bottom: 20px">{{ error }}</div>

      <!-- Loading skeleton -->
      <div v-if="loading" class="portfolio-grid">
        <div v-for="i in 3" :key="i" class="prestige-skeleton" />
      </div>

      <!-- Portfolio Grid -->
      <div v-else class="portfolio-grid">
        <ScrollReveal v-for="(portfolio, i) in filteredPortfolios" :key="portfolio.id" :delay="i * 0.06">
          <div
            class="prestige-panel prestige-panel-pad portfolio-card"
            @click="router.push({ name: 'portfolio-detail', params: { id: portfolio.id } })"
          >
            <div class="card-top">
              <span class="prestige-tag prestige-mono">{{ portfolio.baseCurrency }}</span>
              <button
                class="del-btn"
                title="刪除組合"
                @click.stop="handleDeletePortfolio(portfolio.id)"
              >
                ✕
              </button>
            </div>

            <h3 class="card-title">{{ portfolio.name }}</h3>
            <p class="card-desc">{{ portfolio.description || '無描述' }}</p>

            <div class="metric-row">
              <span class="metric-label">市場價值</span>
              <span class="metric-value prestige-mono">
                {{ valuations.get(portfolio.id) ? formatMoney(valuations.get(portfolio.id)!.totalMarketValue, portfolio.baseCurrency) : '—' }}
              </span>
            </div>

            <div class="metric-row">
              <span class="metric-label">未實現損益</span>
              <span :class="['metric-value prestige-mono', pnlClass(valuations.get(portfolio.id)?.totalUnrealizedPnlPercent)]">
                {{ formatPercent(valuations.get(portfolio.id)?.totalUnrealizedPnlPercent ?? null) }}
              </span>
            </div>

            <div class="card-foot prestige-mono">
              <span>{{ portfolio.holdingCount }} HOLDINGS</span>
              <span>{{ formatDate(portfolio.updatedAtUtc) }}</span>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div v-if="!loading && filteredPortfolios.length === 0" class="prestige-empty">
        沒有符合條件的投資組合
      </div>
    </div>
  </div>
</template>

<style scoped>
.prestige-page {
  min-height: calc(100vh - 60px);
}

.create-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-bottom: 24px;
}

.create-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
}

.create-panel .prestige-btn {
  align-self: flex-start;
}

.search-row {
  margin-bottom: 24px;
}

.search-input {
  max-width: 320px;
}

.portfolio-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 16px;
}

.portfolio-card {
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 8px;
  transition: transform 0.25s ease, border-color 0.3s ease;
}

.portfolio-card:hover {
  transform: translateY(-2px);
  border-color: rgba(201, 168, 106, 0.45);
}

.card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.del-btn {
  background: none;
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  color: var(--muted);
  cursor: pointer;
  padding: 4px 10px;
  font-size: 12px;
  transition: all 0.2s ease;
}

.del-btn:hover {
  color: var(--down);
  border-color: rgba(176, 92, 92, 0.5);
  background: rgba(176, 92, 92, 0.08);
}

.card-title {
  margin: 4px 0 0;
  font-family: var(--serif);
  font-size: 18px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.card-desc {
  margin: 0 0 8px;
  font-size: 13px;
  color: var(--muted);
  min-height: 18px;
}

.metric-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}

.metric-label {
  font-size: 12px;
  color: var(--muted);
}

.metric-value {
  font-size: 16px;
  font-weight: 600;
}

.metric-value.up {
  color: var(--up);
}

.metric-value.down {
  color: var(--down);
}

.metric-value.neutral {
  color: var(--muted);
}

.card-foot {
  margin-top: 8px;
  padding-top: 12px;
  border-top: 1px solid var(--gold-border-soft);
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  color: var(--muted);
  letter-spacing: 0.06em;
}

@media (max-width: 720px) {
  .create-grid {
    grid-template-columns: 1fr;
  }
}
</style>
