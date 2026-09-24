<template>
  <div
    class="relative w-full flex flex-col items-center justify-center font-heading"
    :style="{ backgroundColor: isDark ? '#0A0A0A' : '#F5F5F5', color: isDark ? '#FFFFFF' : '#000000', minHeight: '100vh', padding: 'clamp(20px, 4vw, 80px)' }"
  >
    <div ref="navRef" class="flex flex-col items-center gap-8 w-full max-w-4xl">
      <!-- Brand -->
      <div class="flex flex-col items-center gap-1">
        <span class="font-mono text-sm tracking-[0.3em]" :style="{ color: isDark ? '#FFFFFF' : '#000000' }">
          睿見 RISE VISION
        </span>
        <span class="text-xs tracking-[0.2em] uppercase" style="color: #666666">
          INVESTMENT ANALYTICS
        </span>
      </div>

      <!-- Navigation Options -->
      <div class="flex items-center gap-10">
        <button
          v-for="link in navLinks"
          :key="link.path"
          @click="navigateTo(link.path)"
          class="text-xs font-medium tracking-[0.15em] uppercase transition-all duration-300 hover:-translate-y-1"
          :style="{
            color: currentPath === link.path ? (isDark ? '#FFFFFF' : '#000000') : '#666666',
            fontWeight: currentPath === link.path ? 700 : 500,
          }"
        >
          {{ link.label }}
        </button>
      </div>

      <!-- Page Title -->
      <div ref="titleRef" class="flex flex-col items-center gap-0" style="filter: blur(8px); opacity: 0;">
        <h1 class="text-display font-semibold tracking-tight text-center leading-none" :style="{ color: isDark ? '#FFFFFF' : '#000000' }">
          {{ mainTitle1 }}
        </h1>
        <h1 class="text-display font-semibold tracking-tight text-center leading-none" :style="{ color: isDark ? '#FFFFFF' : '#000000' }">
          {{ mainTitle2 }}
        </h1>
      </div>

      <!-- Subtitle -->
      <p ref="subtitleRef" class="text-sm text-center max-w-md leading-relaxed" style="color: #666666; opacity: 0; transform: translateY(10px);">
        {{ subtitleText }}
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import gsap from 'gsap'

const props = defineProps<{
  isDark?: boolean
}>()

const router = useRouter()
const route = useRoute()
const titleRef = ref<HTMLDivElement>()
const subtitleRef = ref<HTMLParagraphElement>()
const navRef = ref<HTMLDivElement>()

const isDark = computed(() => props.isDark ?? false)
const currentPath = computed(() => route.path)

const navLinks = [
  { path: '/', label: '投資組合分析' },
  { path: '/financials', label: '財報分析' },
  { path: '/risk', label: '風險壓力測試' },
]

const pageConfig = computed(() => {
  switch (route.path) {
    case '/financials':
      return {
        mainTitle1: 'FINANCIAL',
        mainTitle2: 'STATEMENTS',
        subtitle: '深度解讀企業財務數據，透視營收結構與獲利能力，掌握財務健康趨勢',
      }
    case '/risk':
      return {
        mainTitle1: 'RISK',
        mainTitle2: 'ANALYSIS',
        subtitle: '全面評估投資組合風險暴露，VaR、ES量化分析與多重壓力情境測試',
      }
    default:
      return {
        mainTitle1: 'PORTFOLIO',
        mainTitle2: 'ANALYSIS',
        subtitle: '即時追蹤投資組合表現，智能分析收益與風險，數據驅動的資產配置洞察',
      }
  }
})

const mainTitle1 = computed(() => pageConfig.value.mainTitle1)
const mainTitle2 = computed(() => pageConfig.value.mainTitle2)
const subtitleText = computed(() => pageConfig.value.subtitle)

function navigateTo(path: string) {
  if (route.path === path) return
  router.push(path)
}

onMounted(() => {
  const tl = gsap.timeline()

  if (titleRef.value) {
    tl.to(titleRef.value, {
      filter: 'blur(0px)',
      opacity: 1,
      duration: 1.2,
      ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
    })
  }

  if (subtitleRef.value) {
    tl.to(subtitleRef.value, {
      opacity: 1,
      y: 0,
      duration: 0.8,
      ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
    }, '-=0.4')
  }
})
</script>
