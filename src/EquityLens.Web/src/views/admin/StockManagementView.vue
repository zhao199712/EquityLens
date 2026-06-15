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
  AddOutline,
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
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">{{ t('admin.stocks.titleEn') }}</p>
        <h1>{{ t('admin.stocks.title') }}</h1>
      </div>
      <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
        <template #icon>
          <NIcon><AddOutline /></NIcon>
        </template>
        {{ t('admin.stocks.create') }}
      </NButton>
    </section>

    <div class="glass-panel" style="padding: 16px 20px;">
      <NInput
        v-model:value="searchQuery"
        :placeholder="t('admin.stocks.searchPlaceholder')"
        clearable
        style="max-width: 400px;"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
    </div>

    <div class="glass-panel" style="margin-top: 24px; padding: 0; overflow: hidden;">
      <div class="glass-table">
        <table style="width: 100%; border-collapse: collapse;">
          <thead>
            <tr style="border-bottom: 1px solid var(--border-subtle);">
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.stocks.table.ticker') }}</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.stocks.table.name') }}</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.stocks.table.sector') }}</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.stocks.table.exchange') }}</th>
              <th style="text-align: right; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.stocks.table.marketPrice') }}</th>
              <th style="text-align: center; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.stocks.table.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="stock in filteredStocks"
              :key="stock.symbol"
              style="border-bottom: 1px solid var(--border-subtle);"
              class="table-row-hover"
            >
              <td style="padding: 14px 20px; color: var(--accent-primary); font-weight: 600;">{{ stock.symbol }}</td>
              <td style="padding: 14px 20px; color: var(--text-primary); font-weight: 500;">{{ stock.name }}</td>
              <td style="padding: 14px 20px;">
                <NTag size="small" round>{{ stock.sector }}</NTag>
              </td>
              <td style="padding: 14px 20px; color: var(--text-secondary);">{{ stock.market }}</td>
              <td style="padding: 14px 20px; text-align: right; color: var(--text-primary); font-weight: 600;">
                {{ stock.currency === 'USD' ? '$' : 'NT$' }}{{ stock.price }}
              </td>
              <td style="padding: 14px 20px; text-align: center;">
                <NSpace justify="center">
                  <NButton text type="primary" size="small">
                    <template #icon>
                      <NIcon><CreateOutline /></NIcon>
                    </template>
                  </NButton>
                  <NPopconfirm @positive-click="handleDelete(stock.symbol)">
                    <template #trigger>
                      <NButton text type="error" size="small">
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
.table-row-hover:hover {
  background: rgba(255, 255, 255, 0.04);
}
</style>
