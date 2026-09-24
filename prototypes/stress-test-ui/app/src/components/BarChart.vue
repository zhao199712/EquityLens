<template>
  <svg ref="svgRef" :width="width" :height="chartH" :viewBox="`0 0 ${width} ${chartH}`">
    <g v-for="(item, i) in data" :key="`bar-${i}`">
      <text :x="0" :y="10 + i * rowH + barH/2 + 4"
        :fill="darkMode ? '#FFFFFF' : '#000000'" font-size="12" font-family="Inter, sans-serif">{{ item.label }}</text>
      <rect ref="bars" class="bar-rect"
        :x="labelW" :y="10 + i * rowH"
        :width="(Math.abs(item.value) / maxVal) * barMaxW"
        :height="barH"
        :fill="item.color || (darkMode ? '#FFFFFF' : '#000000')" />
      <text :x="labelW + ((Math.abs(item.value) / maxVal) * barMaxW) + 8"
        :y="10 + i * rowH + barH/2 + 4"
        :fill="textColor" font-size="12" font-family="Inter, sans-serif">
        {{ item.value > 0 ? '+' : '' }}{{ item.value }}%
      </text>
    </g>
  </svg>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import gsap from 'gsap'

const props = defineProps<{
  data: { label: string; value: number; color?: string }[]
  width?: number
  barHeight?: number
  textColor?: string
  darkMode?: boolean
}>()

const width = props.width ?? 400
const barH = props.barHeight ?? 24
const rowH = barH + 16
const chartH = 10 + props.data.length * rowH + 10
const labelW = 100
const barMaxW = width - labelW - 80
const maxVal = Math.max(...props.data.map(d => Math.abs(d.value)))
const textColor = props.textColor ?? '#000000'
const darkMode = props.darkMode ?? false
const bars = ref<SVGRectElement[]>([])
const svgRef = ref<SVGSVGElement>()

onMounted(() => {
  bars.value.forEach((bar, i) => {
    if (!bar) return
    const targetW = bar.getAttribute('width') || '0'
    gsap.set(bar, { attr: { width: 0 } })
    gsap.to(bar, { attr: { width: Number(targetW) }, duration: 0.8, delay: i * 0.1, ease: 'power2.out',
      scrollTrigger: { trigger: svgRef.value, start: 'top 85%' }
    })
  })
})
</script>
