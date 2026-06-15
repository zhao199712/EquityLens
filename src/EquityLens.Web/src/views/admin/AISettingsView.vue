<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { SparklesOutline } from '@vicons/ionicons5'

const { t } = useI18n()
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">{{ t('admin.aiSettings.titleEn') }}</p>
        <h1>{{ t('admin.aiSettings.title') }}</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><SparklesOutline /></NIcon>
        </template>
        {{ t('admin.aiSettings.save') }}
      </NButton>
    </section>

    <div class="empty-state">
      <NEmpty :description="t('admin.aiSettings.emptyTitle')">
        <template #extra>
          <NButton type="primary" class="btn-primary">{{ t('admin.aiSettings.learnMore') }}</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>
