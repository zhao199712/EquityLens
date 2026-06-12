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
    error.value = '無法載入投資組合列表，請稍後再試。'
  } finally {
    loading.value = false
  }
})

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('zh-TW')
}
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px; padding-bottom: 80px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">風險分析</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">RISK ANALYSIS RUNS</span>
        </div>
      </div>

      <!-- Search -->
      <div style="margin-bottom: 24px">
        <input
          v-model="search"
          placeholder="搜尋投資組合..."
          class="kimi-input-dark"
          style="width: 300px"
        />
      </div>

      <!-- Loading / Error -->
      <div v-if="loading" style="color: #666666; font-size: 14px">載入中...</div>
      <div v-else-if="error" style="color: #f87171; font-size: 14px; margin-bottom: 24px">{{ error }}</div>

      <!-- Portfolio Grid -->
      <div v-else class="kimi-grid-3">
        <ScrollReveal v-for="(run, i) in filteredPortfolios" :key="run.id" :delay="i * 0.1">
          <div
            class="kimi-panel-dark"
            style="cursor: pointer; transition: all 0.2s ease"
            @click="router.push({ name: 'risk-run-detail', params: { id: run.id } })"
          >
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
              <span class="kimi-tag-dark">{{ run.baseCurrency }}</span>
              <span class="kimi-tag-dark" style="border-color: #34d399; color: #34d399">AVAILABLE</span>
            </div>

            <h3 style="margin: 0 0 4px; font-size: 18px; font-weight: 600; color: #FFFFFF">{{ run.name }}</h3>
            <p style="margin: 0 0 16px; font-size: 13px; color: #666666">{{ run.description || '無描述' }}</p>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
              <div>
                <span style="font-size: 11px; color: #666666; display: block; margin-bottom: 4px">持股數</span>
                <span style="font-size: 16px; font-weight: 600; color: #FFFFFF">{{ run.holdingCount }}</span>
              </div>
              <div>
                <span style="font-size: 11px; color: #666666; display: block; margin-bottom: 4px">更新於</span>
                <span style="font-size: 16px; font-weight: 600; color: #FFFFFF">{{ formatDate(run.updatedAtUtc) }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>
      </div>

      <div v-if="!loading && !error && filteredPortfolios.length === 0" style="color: #666666; margin-top: 24px">
        沒有符合條件的投資組合。
      </div>

      <div style="height: 80px" />
    </div>

    <footer class="kimi-footer kimi-footer-dark">
      <span style="color: #666666">EQUITYLENS 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px; color: #666666">RISK</span>
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
  border-color: #FFFFFF;
}
.kimi-input-dark::placeholder {
  color: #666666;
}
</style>
