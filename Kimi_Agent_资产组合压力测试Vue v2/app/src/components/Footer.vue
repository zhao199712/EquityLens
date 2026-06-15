<template>
  <footer
    class="w-full flex items-center justify-between px-5"
    :style="{
      backgroundColor: isDark ? '#0A0A0A' : '#F5F5F5',
      borderTop: `1px solid ${isDark ? '#333333' : '#E0E0E0'}`,
      height: '60px',
    }"
  >
    <span class="text-xs" style="color: #666666">RISE VISION 2026</span>
    <span class="text-xs font-mono tracking-wider uppercase" style="color: #666666">{{ label }}</span>
    <span class="text-xs" style="color: #666666">數據僅供參考</span>
  </footer>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const isDark = computed(() => route.path === '/financials' || route.path === '/risk')
const label = computed(() => {
  if (route.path === '/risk') return 'RISK'
  if (route.path === '/financials') return 'FINANCIALS'
  return 'PORTFOLIO'
})
</script>
