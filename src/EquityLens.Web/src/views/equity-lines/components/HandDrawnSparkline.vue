<template>
  <div class="hand-drawn-sparkline glass-card">
    <div class="spark-header">
      <div class="spark-title">{{ props.title }}</div>
      <div class="spark-change" :style="{ color: props.change >= 0 ? '#34d399' : '#f87171' }">
        {{ props.change >= 0 ? '+' : '' }}{{ props.change.toFixed(2) }}%
      </div>
    </div>
    <div class="spark-subtitle">{{ props.subtitle }}</div>
    <div class="spark-canvas">
      <svg
        width="100%"
        height="120"
        :viewBox="`0 0 ${chartWidth} 120`"
        preserveAspectRatio="none"
      >
        <defs>
          <filter id="rough">
            <feTurbulence type="fractalNoise" baseFrequency="0.85" numOctaves="3" result="noise" />
            <feDisplacementMap in="SourceGraphic" in2="noise" scale="2" />
          </filter>
        </defs>

        <!-- Grid -->
        <line x1="0" y1="60" x2="100%" y2="60" class="spark-grid" />

        <!-- Area -->
        <polygon
          :points="areaPoints"
          :fill="props.color"
          fill-opacity="0.08"
          filter="url(#rough)"
        />

        <!-- Line -->
        <polyline
          :points="linePoints"
          :stroke="props.color"
          fill="none"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          filter="url(#rough)"
        />

        <!-- Last point -->
        <circle :cx="lastPoint.x" :cy="lastPoint.y" r="4" :fill="props.color" />
      </svg>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    title: string
    subtitle: string
    data: number[]
    change: number
    color: string
    width?: number
  }>(),
  {
    width: 240,
  }
)

const chartWidth = computed(() => props.width)

const min = computed(() => Math.min(...props.data, 0))
const max = computed(() => Math.max(...props.data, 0))
const range = computed(() => max.value - min.value || 1)

const linePoints = computed(() => {
  const h = 100
  const padY = 10
  return props.data
    .map((v: number, i: number) => {
      const x = (i / (props.data.length - 1)) * chartWidth.value
      const y = padY + h - ((v - min.value) / range.value) * h
      return `${x},${y}`
    })
    .join(' ')
})

const areaPoints = computed(() => {
  const h = 100
  const padY = 10
  const zeroY = padY + h - ((0 - min.value) / range.value) * h
  return `0,${zeroY} ${linePoints.value} ${chartWidth.value},${zeroY}`
})

const lastPoint = computed(() => {
  const h = 100
  const padY = 10
  return {
    x: chartWidth.value,
    y: padY + h - ((props.data[props.data.length - 1] - min.value) / range.value) * h,
  }
})
</script>

<style scoped>
.hand-drawn-sparkline {
  padding: 20px;
  transition: all 0.3s ease;
}

.spark-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
}

.spark-title {
  font-family: var(--kimi-font-mono);
  font-size: 16px;
  font-weight: 700;
  color: var(--text-primary);
}

.spark-change {
  font-size: 13px;
  font-weight: 700;
}

.spark-subtitle {
  font-size: 12px;
  color: var(--text-muted);
  margin-bottom: 16px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.spark-canvas {
  width: 100%;
}

.spark-grid {
  stroke: var(--border-medium);
  stroke-width: 1;
  stroke-dasharray: 3 3;
}
</style>
