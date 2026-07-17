<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  NButton,
  NInput,
  NTag,
  NModal,
  NForm,
  NFormItem,
  NSpace,
  NIcon,
  NPopconfirm,
} from 'naive-ui'
import {
  SearchOutline,
  CreateOutline,
  TrashOutline,
} from '@vicons/ionicons5'

const { t } = useI18n()

const searchQuery = ref('')

const stocks = ref([
  { symbol: 'AAPL', name: 'Apple Inc.', sector: 'Technology', market: 'NASDAQ', price: 195.50, currency: 'USD' },
  { symbol: 'MSFT', name: 'Microsoft Corp.', sector: 'Technology', market: 'NASDAQ', price: 420.30, currency: 'USD' },
  { symbol: 'NVDA', name: 'NVIDIA Corp.', sector: 'Technology', market: 'NASDAQ', price: 1250.00, currency: 'USD' },
  { symbol: 'GOOGL', name: 'Alphabet Inc.', sector: 'Technology', market: 'NASDAQ', price: 175.80, currency: 'USD' },
  { symbol: 'TSM', name: 'TSMC', sector: 'Technology', market: 'NYSE', price: 158.20, currency: 'USD' },
  { symbol: '2330.TW', name: '台積電', sector: 'Technology', market: 'TWSE', price: 875.00, currency: 'TWD' },
])

const filteredStocks = computed(() => {
  if (!searchQuery.value) return stocks.value
  const q = searchQuery.value.toLowerCase()
  return stocks.value.filter(s =>
    s.symbol.toLowerCase().includes(q) ||
    s.name.toLowerCase().includes(q) ||
    s.sector.toLowerCase().includes(q)
  )
})

const showCreateModal = ref(false)
const createForm = ref({
  symbol: '',
  name: '',
  sector: '',
  market: '',
})

function handleCreate() {
  stocks.value.push({
    symbol: createForm.value.symbol,
    name: createForm.value.name,
    sector: createForm.value.sector,
    market: createForm.value.market,
    price: 0,
    currency: 'USD',
  })
  showCreateModal.value = false
  createForm.value = { symbol: '', name: '', sector: '', market: '' }
}

function handleDelete(symbol: string) {
  stocks.value = stocks.value.filter(s => s.symbol !== symbol)
}
</script>

<template>
  <main class="prestige-section prestige-fade admin-view">
    <div class="prestige-section-head">
      <div>
        <p class="prestige-label view-eyebrow">{{ t('admin.stocks.titleEn') }}</p>
        <h1 class="prestige-section-title">{{ t('admin.stocks.title') }}</h1>
      </div>
      <button class="prestige-btn prestige-btn-solid" @click="showCreateModal = true">
        {{ t('admin.stocks.create') }}
      </button>
    </div>

    <div class="prestige-panel search-panel">
      <NInput
        v-model:value="searchQuery"
        :placeholder="t('admin.stocks.searchPlaceholder')"
        clearable
        class="search-input"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
    </div>

    <div class="prestige-panel table-panel">
      <table class="prestige-table">
        <thead>
          <tr>
            <th>{{ t('admin.stocks.table.ticker') }}</th>
            <th>{{ t('admin.stocks.table.name') }}</th>
            <th>{{ t('admin.stocks.table.sector') }}</th>
            <th>{{ t('admin.stocks.table.exchange') }}</th>
            <th class="col-price">{{ t('admin.stocks.table.marketPrice') }}</th>
            <th class="col-actions">{{ t('admin.stocks.table.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="stock in filteredStocks" :key="stock.symbol">
            <td class="prestige-mono ticker-cell">{{ stock.symbol }}</td>
            <td class="name-cell">{{ stock.name }}</td>
            <td>
              <NTag size="small" round>{{ stock.sector }}</NTag>
            </td>
            <td class="cell-muted">{{ stock.market }}</td>
            <td class="prestige-mono price-cell">
              {{ stock.currency === 'USD' ? '$' : 'NT$' }}{{ stock.price }}
            </td>
            <td class="col-actions">
              <NSpace justify="center">
                <NButton text size="small" class="icon-btn">
                  <template #icon>
                    <NIcon><CreateOutline /></NIcon>
                  </template>
                </NButton>
                <NPopconfirm @positive-click="handleDelete(stock.symbol)">
                  <template #trigger>
                    <NButton text size="small" class="icon-btn icon-btn--danger">
                      <template #icon>
                        <NIcon><TrashOutline /></NIcon>
                      </template>
                    </NButton>
                  </template>
                  {{ t('admin.stocks.delete') }}?
                </NPopconfirm>
              </NSpace>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <NModal v-model:show="showCreateModal" :title="t('admin.stocks.createTitle')" preset="card" style="width: 420px;" :bordered="false">
      <NForm :model="createForm" label-placement="top">
        <NFormItem :label="t('admin.stocks.ticker')" required>
          <NInput v-model:value="createForm.symbol" :placeholder="t('admin.stocks.tickerPlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('admin.stocks.name')" required>
          <NInput v-model:value="createForm.name" :placeholder="t('admin.stocks.namePlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('admin.stocks.table.sector')">
          <NInput v-model:value="createForm.sector" placeholder="e.g. Technology" />
        </NFormItem>
        <NFormItem :label="t('admin.stocks.exchange')">
          <NInput v-model:value="createForm.market" placeholder="e.g. NASDAQ" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showCreateModal = false">{{ t('admin.stocks.cancel') }}</NButton>
          <NButton type="primary" class="btn-primary" @click="handleCreate">{{ t('admin.stocks.createBtn') }}</NButton>
        </NSpace>
      </template>
    </NModal>
  </main>
</template>

<style scoped>
.admin-view {
  padding-top: 8px;
}

.view-eyebrow {
  margin: 0 0 10px;
}

.search-panel {
  padding: 16px 20px;
  margin-bottom: 20px;
}

.search-input {
  max-width: 400px;
}

.table-panel {
  overflow: hidden;
}

.col-actions {
  text-align: center;
}

.col-price {
  text-align: right;
}

.cell-muted {
  color: var(--muted);
}

.ticker-cell {
  color: var(--gold);
  font-weight: 600;
}

.name-cell {
  color: var(--ivory);
  font-weight: 500;
}

.price-cell {
  text-align: right;
  color: var(--ivory);
  font-weight: 600;
}

/* ---- naive-ui → Prestige ---- */
.search-input {
  background: rgba(11, 18, 32, 0.6) !important;
  border-radius: 4px !important;
}

.search-input :deep(.n-input__border) {
  border-color: var(--gold-border-soft) !important;
}

.search-input :deep(.n-input__state-border) {
  box-shadow: none !important;
}

.search-input:hover :deep(.n-input__state-border),
.search-input.n-input--focus :deep(.n-input__state-border) {
  border-color: var(--gold) !important;
}

.search-input :deep(.n-input__input-el) {
  color: var(--ivory) !important;
  caret-color: var(--gold);
}

.search-input :deep(.n-input__input-el::placeholder) {
  color: var(--muted);
}

.search-input :deep(.n-input__prefix) {
  color: var(--gold) !important;
}

.search-input :deep(.n-input__suffix) {
  color: var(--muted) !important;
}

:deep(.n-tag) {
  background: transparent !important;
  border-color: var(--gold-border) !important;
  color: var(--muted) !important;
  font-weight: 600;
  letter-spacing: 0.06em;
}

.icon-btn {
  color: var(--gold) !important;
  border-radius: 4px;
}

.icon-btn:hover {
  color: var(--gold-strong) !important;
  background: rgba(201, 168, 106, 0.08) !important;
}

.icon-btn--danger {
  color: var(--down) !important;
}

.icon-btn--danger:hover {
  color: #c97a7a !important;
  background: rgba(176, 92, 92, 0.1) !important;
}
</style>
