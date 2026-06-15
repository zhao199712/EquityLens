<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { ShieldCheckmarkOutline } from '@vicons/ionicons5'

const { t } = useI18n()
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">{{ t('admin.riskModels.titleEn') }}</p>
        <h1>{{ t('admin.riskModels.title') }}</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><ShieldCheckmarkOutline /></NIcon>
        </template>
        {{ t('admin.riskModels.create') }}
      </NButton>
    </section>

    <div class="empty-state">
      <NEmpty :description="t('admin.riskModels.emptyTitle')">
        <template #extra>
          <NButton type="primary" class="btn-primary">{{ t('admin.riskModels.learnMore') }}</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>
