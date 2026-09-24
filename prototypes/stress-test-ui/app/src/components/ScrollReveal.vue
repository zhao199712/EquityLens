<template>
  <div ref="el" class="scroll-reveal" :class="$props.class" :style="style">
    <slot />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

const props = defineProps<{
  class?: string
  delay?: number
  direction?: 'up' | 'left'
  duration?: number
  style?: Record<string, string>
}>()

const el = ref<HTMLDivElement>()

const style = computed(() => ({
  opacity: '0',
  transform: props.direction === 'left' ? 'translateX(-10px)' : 'translateY(30px)',
  ...props.style,
}))

onMounted(() => {
  if (!el.value) return
  gsap.to(el.value, {
    opacity: 1,
    x: 0,
    y: 0,
    duration: props.duration ?? 0.8,
    delay: props.delay ?? 0,
    ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
    scrollTrigger: {
      trigger: el.value,
      start: 'top 85%',
      toggleActions: 'play none none none',
    },
  })
})
</script>
