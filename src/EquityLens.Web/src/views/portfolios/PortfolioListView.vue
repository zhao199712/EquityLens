<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'

const router = useRouter()
const search = ref('')

const portfolios = ref([
  { id: '1', name: '科技成長型投資組合', description: 'US Growth Portfolio', strategy: 'Growth', currency: 'USD', holdings: 6, value: 'NT$ 12,580,000', pnl: '+18.72%', updatedAt: '2026-06-03' },
  { id: '2', name: '台灣核心持股組合', description: 'Taiwan Core Portfolio', strategy: 'Balanced', currency: 'TWD', holdings: 5, value: 'NT$ 8,450,000', pnl: '+12.3%', updatedAt: '2026-06-02' },
  { id: '3', name: '價值型藍籌股組合', description: 'Value Blue-Chip Portfolio', strategy: 'Value', currency: 'TWD', holdings: 8, value: 'NT$ 6,200,000', pnl: '+8.5%', updatedAt: '2026-06-01' },
])

const filteredPortfolios = ref(portfolios.value)
const showCreate = ref(false)
const newPortfolio = ref({ name: '', description: '', strategy: 'Growth' })

function filterPortfolios() {
  const q = search.value.toLowerCase()
  filteredPortfolios.value = portfolios.value.filter(
    (p) => p.name.toLowerCase().includes(q) || p.description.toLowerCase().includes(q)
  )
}

function createPortfolio() {
  const p = {
    id: String(portfolios.value.length + 1),
    name: newPortfolio.value.name,
    description: newPortfolio.value.description,
    strategy: newPortfolio.value.strategy,
    currency: 'TWD',
    holdings: 0,
    value: 'NT$ 0',
    pnl: '0%',
    updatedAt: new Date().toISOString().split('T')[0],
  }
  portfolios.value.push(p)
  filterPortfolios()
  showCreate.value = false
  newPortfolio.value = { name: '', description: '', strategy: 'Growth' }
}

function deletePortfolio(id: string) {
  portfolios.value = portfolios.value.filter((p) => p.id !== id)
  filterPortfolios()
}
</script>

<template>
  <div class="kimi-page-light" style="padding-top: 40px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0">投資組合管理</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">PORTFOLIO MANAGEMENT</span>
        </div>
        <button class="kimi-btn kimi-btn-solid" @click="showCreate = !showCreate">
          {{ showCreate ? 'CANCEL' : '+ CREATE PORTFOLIO' }}
        </button>
      </div>

      <!-- Create Form -->
      <ScrollReveal v-if="showCreate">
        <div class="kimi-section" style="margin-bottom: 40px; padding: 20px">
          <h3 style="margin: 0 0 16px; font-size: 16px; font-weight: 600">建立新投資組合</h3>
          <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 16px">
            <input v-model="newPortfolio.name" placeholder="組合名稱" class="kimi-input" />
            <input v-model="newPortfolio.description" placeholder="描述" class="kimi-input" />
            <select v-model="newPortfolio.strategy" class="kimi-input">
              <option>Growth</option>
              <option>Value</option>
              <option>Balanced</option>
              <option>Income</option>
            </select>
          </div>
          <button class="kimi-btn kimi-btn-solid" @click="createPortfolio">CREATE</button>
        </div>
      </ScrollReveal>

      <!-- Search -->
      <div style="margin-bottom: 24px">
        <input
          v-model="search"
          placeholder="搜尋投資組合..."
          class="kimi-input"
          style="width: 300px"
          @input="filterPortfolios"
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
                DEL
              </button>
            </div>

            <h3 style="margin: 0 0 4px; font-size: 18px; font-weight: 600">{{ portfolio.name }}</h3>
            <p style="margin: 0 0 16px; font-size: 13px; color: var(--kimi-muted)">{{ portfolio.description }}</p>

            <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 8px">
              <span style="font-size: 12px; color: var(--kimi-muted)">市場價值</span>
              <span style="font-size: 16px; font-weight: 600">{{ portfolio.value }}</span>
            </div>

            <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 16px">
              <span style="font-size: 12px; color: var(--kimi-muted)">未實現損益</span>
              <span style="font-size: 14px; font-weight: 500" :style="{ color: portfolio.pnl.startsWith('+') ? '#34d399' : '#f87171' }">
                {{ portfolio.pnl }}
              </span>
            </div>

            <div style="border-top: 1px solid var(--kimi-border-light); padding-top: 12px; display: flex; justify-content: space-between">
              <span style="font-size: 12px; color: var(--kimi-muted)">{{ portfolio.holdings }} Holdings</span>
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
      <span>數據僅供參考</span>
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
