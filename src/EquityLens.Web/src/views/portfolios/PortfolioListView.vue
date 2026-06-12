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
  try {
    const items = await getPortfolios()
    portfolios.value = items
    await loadValuations(items)
  } catch (e) {
    error.value = '無法載入投資組合列表，請稍後再試。'
  } finally {
    loading.value = false
  }
}

onMounted(loadPortfolios)

async function handleCreatePortfolio() {
  if (!newPortfolio.value.name.trim()) return
  creating.value = true
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
    error.value = '建立投資組合失敗，請稍後再試。'
  } finally {
    creating.value = false
  }
}

async function handleDeletePortfolio(id: string) {
  if (!confirm('確定要刪除此投資組合嗎？')) return
  try {
    await deletePortfolio(id)
    portfolios.value = portfolios.value.filter((p) => p.id !== id)
  } catch (e) {
    error.value = '刪除投資組合失敗，請稍後再試。'
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

function getPnlColor(n: number | null) {
  if (n === null || n === undefined) return '#666666'
  return n >= 0 ? '#34d399' : '#f87171'
}
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px; padding-bottom: 80px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">投資組合管理</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">PORTFOLIO MANAGEMENT</span>
        </div>
        <button class="kimi-btn kimi-btn-solid-dark" @click="showCreate = !showCreate">
          {{ showCreate ? 'CANCEL' : '+ CREATE PORTFOLIO' }}
        </button>
      </div>

      <!-- Create Form -->
      <ScrollReveal v-if="showCreate">
        <div class="kimi-section-dark" style="margin-bottom: 40px; padding: 20px">
          <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600; color: #FFFFFF">建立新投資組合</h3>
          <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 16px">
            <input v-model="newPortfolio.name" placeholder="組合名稱" class="kimi-input-dark" />
            <input v-model="newPortfolio.description" placeholder="描述" class="kimi-input-dark" />
            <select v-model="newPortfolio.baseCurrency" class="kimi-input-dark">
              <option value="TWD">TWD</option>
              <option value="USD">USD</option>
              <option value="HKD">HKD</option>
              <option value="JPY">JPY</option>
            </select>
          </div>
          <button
            class="kimi-btn kimi-btn-solid-dark"
            :disabled="creating"
            @click="handleCreatePortfolio"
          >
            {{ creating ? 'CREATING...' : 'CREATE' }}
          </button>
        </div>
      </ScrollReveal>

      <!-- Search -->
      <div style="margin-bottom: 24px">
        <input v-model="search" placeholder="搜尋投資組合..." class="kimi-input-dark" style="width: 300px" />
      </div>

      <!-- Loading / Error -->
      <div v-if="loading" style="color: #666666; font-size: 14px">載入中...</div>
      <div v-if="error" style="color: #f87171; font-size: 14px; margin-bottom: 24px">{{ error }}</div>

      <!-- Portfolio Grid -->
      <div v-if="!loading" class="kimi-grid-3">
        <ScrollReveal v-for="(portfolio, i) in filteredPortfolios" :key="portfolio.id" :delay="i * 0.1">
          <div
            class="kimi-panel-dark"
            style="cursor: pointer; transition: all 0.2s ease"
            @click="router.push({ name: 'portfolio-detail', params: { id: portfolio.id } })"
          >
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
              <span class="kimi-tag-dark">{{ portfolio.baseCurrency }}</span>
              <button
                class="kimi-btn kimi-btn-dark"
                style="padding: 2px 8px; font-size: 10px"
                @click.stop="handleDeletePortfolio(portfolio.id)"
              >
                DEL
              </button>
            </div>

            <h3 style="margin: 0 0 4px; font-size: 18px; font-weight: 600; color: #FFFFFF">{{ portfolio.name }}</h3>
            <p style="margin: 0 0 16px; font-size: 13px; color: #666666">{{ portfolio.description || '無描述' }}</p>

            <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 8px">
              <span style="font-size: 12px; color: #666666">市場價值</span>
              <span style="font-size: 16px; font-weight: 600; color: #FFFFFF">
                {{ valuations.get(portfolio.id) ? formatMoney(valuations.get(portfolio.id)!.totalMarketValue, portfolio.baseCurrency) : '—' }}
              </span>
            </div>

            <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 16px">
              <span style="font-size: 12px; color: #666666">未實現損益</span>
              <span
                style="font-size: 14px; font-weight: 500"
                :style="{ color: getPnlColor(valuations.get(portfolio.id)?.totalUnrealizedPnlPercent ?? null) }"
              >
                {{ formatPercent(valuations.get(portfolio.id)?.totalUnrealizedPnlPercent ?? null) }}
              </span>
            </div>

            <div style="border-top: 1px solid #333333; padding-top: 12px; display: flex; justify-content: space-between">
              <span style="font-size: 12px; color: #666666">{{ portfolio.holdingCount }} Holdings</span>
              <span style="font-size: 12px; color: #666666">{{ formatDate(portfolio.updatedAtUtc) }}</span>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div v-if="!loading && filteredPortfolios.length === 0" style="color: #666666; margin-top: 24px">
        沒有符合條件的投資組合。
      </div>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer kimi-footer-dark">
      <span style="color: #666666">EQUITYLENS 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px; color: #666666">PORTFOLIO</span>
      <span style="color: #666666">數據僅供參考</span>
    </footer>
  </div>
</template>

<style scoped>
.kimi-input-dark {
  padding: 8px 12px;
  font-size: 14px;
  font-family: var(--kimi-font-body);
  border: 1px solid var(--kimi-border-dark);
  background: transparent;
  color: var(--kimi-text-dark);
  outline: none;
  transition: border-color 0.2s;
  width: 100%;
}
.kimi-input-dark:focus {
  border-color: #ffffff;
}
.kimi-input-dark::placeholder {
  color: #666666;
}
select.kimi-input-dark {
  appearance: none;
  cursor: pointer;
}
</style>
