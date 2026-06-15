<template>
  <footer :class="['kimi-footer', dark ? 'kimi-footer-dark' : '']">
    <span>RISE VISION 2026</span>
    <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">
      {{ label }}
    </span>
    <span>{{ t('dashboard.footer') }}</span>
  </footer>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

defineProps<{
  dark?: boolean
  label?: string
}>()
</script>
