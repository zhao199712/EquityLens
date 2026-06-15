<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { DocumentTextOutline } from '@vicons/ionicons5'

const { t } = useI18n()
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">{{ t('admin.reports.titleEn') }}</p>
        <h1>{{ t('admin.reports.title') }}</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><DocumentTextOutline /></NIcon>
        </template>
        {{ t('admin.reports.import') }}
      </NButton>
    </section>

    <div class="empty-state">
      <NEmpty :description="t('admin.reports.emptyTitle')">
        <template #extra>
          <NButton type="primary" class="btn-primary">{{ t('admin.reports.learnMore') }}</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>
