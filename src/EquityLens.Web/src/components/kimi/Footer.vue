<template>
  <footer :class="['kimi-footer', dark ? 'kimi-footer-dark' : '']">
    <span>RISE VISION 2026</span>
    <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">
      {{ label }}
    </span>
    <span>數據僅供參考</span>
  </footer>
</template>

<script setup lang="ts">
defineProps<{
  dark?: boolean
  label?: string
}>()
</script>
