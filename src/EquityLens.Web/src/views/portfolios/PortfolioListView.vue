<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'

const router = useRouter()
const { t } = useI18n()
const search = ref('')

const portfolios = ref([
  { id: '1', nameKey: '科技成長型投資組合', nameEn: 'US Growth Portfolio', description: 'US Growth Portfolio', strategy: 'Growth', currency: 'USD', holdings: 6, value: 'NT$ 12,580,000', pnl: '+18.72%', updatedAt: '2026-06-03' },
  { id: '2', nameKey: '台灣核心持股組合', nameEn: 'Taiwan Core Portfolio', description: 'Taiwan Core Portfolio', strategy: 'Balanced', currency: 'TWD', holdings: 5, value: 'NT$ 8,450,000', pnl: '+12.3%', updatedAt: '2026-06-02' },
  { id: '3', nameKey: '價值型藍籌股組合', nameEn: 'Value Blue-Chip Portfolio', description: 'Value Blue-Chip Portfolio', strategy: 'Value', currency: 'TWD', holdings: 8, value: 'NT$ 6,200,000', pnl: '+8.5%', updatedAt: '2026-06-01' },
])

const filteredPortfolios = computed(() => {
  const q = search.value.toLowerCase()
  return portfolios.value.filter(
    (p) => p.nameKey.toLowerCase().includes(q) || p.description.toLowerCase().includes(q)
  )
})

const showCreate = ref(false)
const newPortfolio = ref({ name: '', description: '', strategy: 'Growth' })

function createPortfolio() {
  const p = {
    id: String(portfolios.value.length + 1),
    nameKey: newPortfolio.value.name,
    nameEn: newPortfolio.value.description,
    description: newPortfolio.value.description,
    strategy: newPortfolio.value.strategy,
    currency: 'TWD',
    holdings: 0,
    value: 'NT$ 0',
    pnl: '0%',
    updatedAt: new Date().toISOString().split('T')[0],
  }
  portfolios.value.push(p)
  showCreate.value = false
  newPortfolio.value = { name: '', description: '', strategy: 'Growth' }
}

function deletePortfolio(id: string) {
  portfolios.value = portfolios.value.filter((p) => p.id !== id)
}
</script>

<template>
  <div class="kimi-page-light" style="padding-top: 40px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0">{{ t('portfolios.title') }}</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('portfolios.titleEn') }}</span>
        </div>
        <button class="kimi-btn kimi-btn-solid" @click="showCreate = !showCreate">
          {{ showCreate ? t('portfolios.cancel') : t('portfolios.create') }}
        </button>
      </div>

      <!-- Create Form -->
      <ScrollReveal v-if="showCreate">
        <div class="kimi-section" style="margin-bottom: 40px; padding: 20px">
          <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">{{ t('portfolios.createTitle') }}</h3>
          <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 16px">
            <input v-model="newPortfolio.name" :placeholder="t('portfolios.namePlaceholder')" class="kimi-input" />
            <input v-model="newPortfolio.description" :placeholder="t('portfolios.descPlaceholder')" class="kimi-input" />
            <select v-model="newPortfolio.strategy" class="kimi-input">
              <option>Growth</option>
              <option>Value</option>
              <option>Balanced</option>
              <option>Income</option>
            </select>
          </div>
          <button class="kimi-btn kimi-btn-solid" @click="createPortfolio">{{ t('portfolios.createBtn') }}</button>
        </div>
      </ScrollReveal>

      <!-- Search -->
      <div style="margin-bottom: 24px">
        <input
          v-model="search"
          :placeholder="t('portfolios.searchPlaceholder')"
          class="kimi-input"
          style="width: 300px"
        />
      </div>

      <!-- Portfolio Grid -->
      <div class="kimi-grid-3">
        <ScrollReveal v-for="(portfolio, i) in filteredPortfolios" :key="portfolio.id" :delay="i * 0.1">
          <div
            class="kimi-panel"
            style="cursor: pointer; transition: all 0.2s ease"
            @click="router.push({ name: 'portfolio-detail', params: { id: portfolio.id } })"
          >
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
              <span class="kimi-tag">{{ portfolio.strategy }}</span>
              <button
                class="kimi-btn"
                style="padding: 2px 8px; font-size: 10px"
                @click.stop="deletePortfolio(portfolio.id)"
              >
                {{ t('portfolios.delete') }}
              </button>
            </div>

            <h3 style="margin: 0 0 4px; font-size: 18px; font-weight: 600">{{ portfolio.nameKey }}</h3>
            <p style="margin: 0 0 16px; font-size: 13px; color: var(--kimi-muted)">{{ portfolio.description }}</p>

            <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 8px">
              <span style="font-size: 12px; color: var(--kimi-muted)">{{ t('portfolios.marketValue') }}</span>
              <span style="font-size: 16px; font-weight: 600">{{ portfolio.value }}</span>
            </div>

            <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 16px">
              <span style="font-size: 12px; color: var(--kimi-muted)">{{ t('portfolios.unrealizedPnl') }}</span>
              <span style="font-size: 14px; font-weight: 500" :style="{ color: portfolio.pnl.startsWith('+') ? '#34d399' : '#f87171' }">
                {{ portfolio.pnl }}
              </span>
            </div>

            <div style="border-top: 1px solid var(--kimi-border-light); padding-top: 12px; display: flex; justify-content: space-between">
              <span style="font-size: 12px; color: var(--kimi-muted)">{{ t('portfolios.holdings', { count: portfolio.holdings }) }}</span>
              <span style="font-size: 12px; color: var(--kimi-muted)">{{ portfolio.updatedAt }}</span>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">PORTFOLIO</span>
      <span>{{ t('portfolios.footer') }}</span>
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
