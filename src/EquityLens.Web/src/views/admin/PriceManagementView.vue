<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { TrendingUpOutline } from '@vicons/ionicons5'

const { t } = useI18n()
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">{{ t('admin.prices.titleEn') }}</p>
        <h1>{{ t('admin.prices.title') }}</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><TrendingUpOutline /></NIcon>
        </template>
        {{ t('admin.prices.import') }}
      </NButton>
    </section>

    <div class="empty-state">
      <NEmpty :description="t('admin.prices.emptyTitle')">
        <template #extra>
          <NButton type="primary" class="btn-primary">{{ t('admin.prices.learnMore') }}</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>
