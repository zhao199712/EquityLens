<template>
  <svg :width="width" :height="height" :viewBox="`0 0 ${width} ${height}`" class="w-full">
    <polygon v-for="(points, level) in gridPolys" :key="`grid-${level}`"
      :points="points.map(p=>p.join(',')).join(' ')"
      fill="none" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
    <line v-for="(_, i) in dimensions" :key="`axis-${i}`"
      :x1="cx" :y1="cy" :x2="axisPoints[i][0]" :y2="axisPoints[i][1]"
      stroke="#333333" stroke-width="1" />
    <polygon ref="polyRef" :points="dataPolyStr"
      :fill="fillColor" fill-opacity="0.2" :stroke="strokeColor" stroke-width="2" />
    <circle v-for="(p, i) in dataPoints" :key="`dp-${i}`" :cx="p[0]" :cy="p[1]" r="5" :fill="fillColor" />
    <text v-for="(d, i) in dimensions" :key="`lab-${i}`"
      :x="labelPoints[i][0]" :y="labelPoints[i][1]+4"
      text-anchor="middle" fill="#666666" font-size="11" font-family="Inter, sans-serif">{{ d.label }}</text>
  </svg>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import gsap from 'gsap'

const props = defineProps<{
  dimensions: { label: string; value: number; max: number }[]
  width?: number
  height?: number
  fillColor?: string
  strokeColor?: string
}>()

const width = props.width ?? 300
const height = props.height ?? 300
const cx = width / 2
const cy = height / 2
const maxR = 110
const levels = 5
const angleStep = (Math.PI * 2) / props.dimensions.length
const startAngle = -Math.PI / 2
const fillColor = props.fillColor ?? '#8B1A2B'
const strokeColor = props.strokeColor ?? '#8B1A2B'

const axisPoints = props.dimensions.map((_, i) => {
  const a = startAngle + i * angleStep
  return [cx + maxR * Math.cos(a), cy + maxR * Math.sin(a)]
})

const labelPoints = props.dimensions.map((_, i) => {
  const a = startAngle + i * angleStep
  return [cx + (maxR + 22) * Math.cos(a), cy + (maxR + 22) * Math.sin(a)]
})

const gridPolys = Array.from({ length: levels }, (_, level) => {
  const r = ((level + 1) / levels) * maxR
  return props.dimensions.map((_, i) => {
    const a = startAngle + i * angleStep
    return [cx + r * Math.cos(a), cy + r * Math.sin(a)]
  })
})

const dataPoints = props.dimensions.map((d, i) => {
  const a = startAngle + i * angleStep
  const r = (d.value / d.max) * maxR
  return [cx + r * Math.cos(a), cy + r * Math.sin(a)]
})

const dataPolyStr = dataPoints.map(p => p.join(',')).join(' ')
const polyRef = ref<SVGPolygonElement>()

onMounted(() => {
  if (!polyRef.value) return
  gsap.fromTo(polyRef.value, { opacity: 0, scale: 0.5, transformOrigin: `${cx}px ${cy}px` }, {
    opacity: 1, scale: 1, duration: 1, ease: 'power2.out',
    scrollTrigger: { trigger: polyRef.value, start: 'top 85%' }
  })
})
</script>
