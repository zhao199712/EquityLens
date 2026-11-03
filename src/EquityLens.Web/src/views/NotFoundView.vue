<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <div class="prestige-page not-found">
    <section class="prestige-section not-found-inner">
      <span class="prestige-label">404</span>
      <h1 class="not-found-title">{{ t('notFound.title') }}</h1>
      <p class="not-found-desc">{{ t('notFound.description') }}</p>
      <RouterLink class="prestige-btn prestige-btn-solid not-found-link" to="/">
        {{ t('notFound.backHome') }}
      </RouterLink>
    </section>
  </div>
</template>

<style scoped>
.not-found {
  min-height: calc(100vh - 60px);
}

.not-found-inner {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 16px;
  padding-top: 120px;
}

.not-found-title {
  margin: 0;
  font-family: var(--serif);
  font-size: clamp(28px, 5vw, 40px);
  font-weight: 600;
}

.not-found-desc {
  margin: 0 0 8px;
  color: var(--muted);
}

.not-found-link {
  text-decoration: none;
}
</style>
