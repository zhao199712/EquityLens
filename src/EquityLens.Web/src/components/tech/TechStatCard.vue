<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useTransition } from '@vueuse/core'

const props = withDefaults(defineProps<{
  label: string
  value: number
  prefix?: string
  suffix?: string
  decimals?: number
  sub?: string
  tone?: 'neutral' | 'positive' | 'negative'
}>(), {
  prefix: '',
  suffix: '',
  decimals: 0,
  sub: '',
  tone: 'neutral',
})

const el = ref<HTMLElement>()
const inView = ref(false)
let observer: IntersectionObserver | null = null

const source = ref(0)
const animated = useTransition(source, { duration: 1200 })

const displayValue = computed(() => {
  const v = inView.value ? animated.value : 0
  return v.toLocaleString('en-US', {
    minimumFractionDigits: props.decimals,
    maximumFractionDigits: props.decimals,
  })
})

onMounted(() => {
  if (!el.value) return
  observer = new IntersectionObserver(
    (entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        inView.value = true
        source.value = props.value
        observer?.disconnect()
      }
    },
    { threshold: 0.4 }
  )
  observer.observe(el.value)
})

onUnmounted(() => {
  observer?.disconnect()
})
</script>

<template>
  <div ref="el" class="tech-stat-card" :data-tone="tone">
    <span class="tech-label">{{ label }}</span>
    <span class="tech-stat-value tech-mono">{{ prefix }}{{ displayValue }}{{ suffix }}</span>
    <span v-if="sub" class="tech-stat-sub">{{ sub }}</span>
  </div>
</template>

<style scoped>
.tech-stat-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 18px 22px;
  background: var(--tech-bg-panel);
  border: 1px solid var(--tech-border);
  border-radius: 12px;
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  transition: border-color 0.3s ease, box-shadow 0.3s ease;
}

.tech-stat-card:hover {
  border-color: var(--tech-border-strong);
  box-shadow: var(--tech-glow-sm);
}

.tech-stat-value {
  font-size: clamp(22px, 2.6vw, 32px);
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--tech-text);
  text-shadow: 0 0 18px rgba(34, 211, 238, 0.25);
}

.tech-stat-card[data-tone='positive'] .tech-stat-value {
  color: var(--tech-green);
  text-shadow: 0 0 18px rgba(52, 211, 153, 0.35);
}

.tech-stat-card[data-tone='negative'] .tech-stat-value {
  color: var(--tech-red);
  text-shadow: 0 0 18px rgba(251, 113, 133, 0.35);
}

.tech-stat-sub {
  font-size: 12px;
  color: var(--tech-muted);
}
</style>
