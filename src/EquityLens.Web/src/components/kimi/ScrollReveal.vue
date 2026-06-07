<template>
  <div ref="el" :class="['kimi-reveal', direction === 'left' ? 'kimi-reveal-left' : '']" :style="{ transitionDelay: `${delay}s` }">
    <slot />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'

const props = withDefaults(defineProps<{
  delay?: number
  direction?: 'up' | 'left'
}>(), {
  delay: 0,
  direction: 'up',
})

const el = ref<HTMLElement>()
let observer: IntersectionObserver | null = null

onMounted(() => {
  if (!el.value) return
  observer = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add('visible')
        }
      })
    },
    { threshold: 0.15 }
  )
  observer.observe(el.value)
})

onUnmounted(() => {
  observer?.disconnect()
})
</script>
