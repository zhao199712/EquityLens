<template>
  <svg width="100%" :height="height" :viewBox="`0 0 ${width} ${height}`">
    <!-- Grid polygons -->
    <polygon
      v-for="(_, level) in levels"
      :key="'grid-' + level"
      :points="gridPolygons[level].map((p) => p.join(',')).join(' ')"
      fill="none"
      stroke="#333333"
      stroke-width="1"
      stroke-dasharray="4 4"
    />

    <!-- Axis lines -->
    <line
      v-for="(_, i) in dimensions"
      :key="'axis-' + i"
      :x1="cx"
      :y1="cy"
      :x2="cx + maxRadius * Math.cos(startAngle + i * angleStep)"
      :y2="cy + maxRadius * Math.sin(startAngle + i * angleStep)"
      stroke="#333333"
      stroke-width="1"
    />

    <!-- Data polygon -->
    <polygon
      :points="dataPolygonStr"
      :fill="fillColor"
      fill-opacity="0.2"
      :stroke="strokeColor"
      stroke-width="2"
      class="kimi-radar-poly"
    />

    <!-- Data points -->
    <circle
      v-for="(p, i) in dataPoints"
      :key="'dp-' + i"
      :cx="p[0]"
      :cy="p[1]"
      r="5"
      :fill="fillColor"
    />

    <!-- Dimension labels -->
    <text
      v-for="(d, i) in dimensions"
      :key="'label-' + i"
      :x="cx + (maxRadius + 22) * Math.cos(startAngle + i * angleStep)"
      :y="cy + (maxRadius + 22) * Math.sin(startAngle + i * angleStep) + 4"
      text-anchor="middle"
      fill="#666666"
      font-size="11"
      font-family="Inter, sans-serif"
    >
      {{ d.label }}
    </text>
  </svg>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  dimensions: { label: string; value: number; max: number }[]
  width?: number
  height?: number
  fillColor?: string
  strokeColor?: string
}>(), {
  width: 300,
  height: 300,
  fillColor: '#8B1A2B',
  strokeColor: '#8B1A2B',
})

const levels = 5
const cx = computed(() => props.width / 2)
const cy = computed(() => props.height / 2)
const maxRadius = 110
const angleStep = computed(() => (Math.PI * 2) / props.dimensions.length)
const startAngle = -Math.PI / 2

const gridPolygons = computed(() =>
  Array.from({ length: levels }).map((_, level) => {
    const r = ((level + 1) / levels) * maxRadius
    return props.dimensions.map((_, i) => {
      const angle = startAngle + i * angleStep.value
      return [cx.value + r * Math.cos(angle), cy.value + r * Math.sin(angle)]
    })
  })
)

const dataPoints = computed(() =>
  props.dimensions.map((d, i) => {
    const angle = startAngle + i * angleStep.value
    const r = (d.value / d.max) * maxRadius
    return [cx.value + r * Math.cos(angle), cy.value + r * Math.sin(angle)]
  })
)

const dataPolygonStr = computed(() => dataPoints.value.map((p) => p.join(',')).join(' '))
</script>

<style scoped>
.kimi-radar-poly {
  animation: radarReveal 1s ease-out forwards;
}
@keyframes radarReveal {
  from { opacity: 0; transform: scale(0.5); }
  to { opacity: 1; transform: scale(1); }
}
</style>
