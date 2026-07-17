<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { TrendingUpOutline } from '@vicons/ionicons5'

const { t } = useI18n()
</script>

<template>
  <main class="prestige-section prestige-fade admin-view">
    <div class="prestige-section-head">
      <div>
        <p class="prestige-label view-eyebrow">{{ t('admin.prices.titleEn') }}</p>
        <h1 class="prestige-section-title">{{ t('admin.prices.title') }}</h1>
      </div>
      <button class="prestige-btn prestige-btn-solid">
        <NIcon :size="16"><TrendingUpOutline /></NIcon>
        {{ t('admin.prices.import') }}
      </button>
    </div>

    <div class="prestige-empty">
      <NEmpty :description="t('admin.prices.emptyTitle')">
        <template #extra>
          <NButton class="empty-btn">{{ t('admin.prices.learnMore') }}</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>

<style scoped>
.admin-view {
  padding-top: 8px;
}

.view-eyebrow {
  margin: 0 0 10px;
}

:deep(.n-empty__icon) {
  color: var(--muted) !important;
}

:deep(.n-empty__description) {
  color: var(--muted) !important;
}

.empty-btn {
  background: transparent !important;
  color: var(--gold) !important;
  border-radius: 4px;
}

.empty-btn :deep(.n-button__border) {
  border-color: var(--gold-border) !important;
}

.empty-btn :deep(.n-button__state-border) {
  border-color: var(--gold) !important;
  box-shadow: none !important;
}

.empty-btn:hover {
  background: rgba(201, 168, 106, 0.08) !important;
  color: var(--gold-strong) !important;
}
</style>
