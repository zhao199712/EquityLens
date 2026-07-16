<template>
  <div class="equity-lines-chart">
    <div v-if="loading" class="chart-loading">載入中...</div>
    <div v-else-if="series.length === 0" class="chart-empty">選擇標的以查看走勢比較</div>
    <svg
      v-else
      width="100%"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      preserveAspectRatio="xMidYMid meet"
      class="chart-svg"
    >
      <defs>
        <filter id="pencil">
          <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="3" result="noise" />
          <feDisplacementMap in="SourceGraphic" in2="noise" scale="1.5" />
        </filter>
      </defs>

      <!-- Grid -->
      <g class="grid">
        <line
          v-for="i in gridYCount"
          :key="`gy-${i}`"
          :x1="padding.left"
          :y1="padding.top + ((i - 1) / (gridYCount - 1)) * chartH"
          :x2="padding.left + chartW"
          :y2="padding.top + ((i - 1) / (gridYCount - 1)) * chartH"
        />
        <text
          v-for="i in gridYCount"
          :key="`gyl-${i}`"
          :x="padding.left - 10"
          :y="padding.top + chartH - ((i - 1) / (gridYCount - 1)) * chartH + 4"
          text-anchor="end"
        >
          {{ yLabels[i - 1] }}
        </text>
      </g>

      <!-- Zero line -->
      <line
        v-if="hasPositiveAndNegative"
        :x1="padding.left"
        :y1="zeroY"
        :x2="padding.left + chartW"
        :y2="zeroY"
        class="zero-line"
      />

      <!-- Series paths -->
      <g filter="url(#pencil)">
        <path
          v-for="s in visibleSeries"
          :key="s.securityId"
          :d="pathFor(s)"
          :stroke="s.color"
          fill="none"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="series-line"
        />
      </g>

      <!-- Data points -->
      <g v-for="s in visibleSeries" :key="`pts-${s.securityId}`">
        <circle
          v-for="(v, i) in visiblePoints(s)"
          :key="i"
          :cx="v.x"
          :cy="v.y"
          r="3.5"
          :fill="s.color"
          class="series-point"
        />
      </g>

      <!-- X labels -->
      <text
        v-for="(l, i) in xLabels"
        :key="`xl-${i}`"
        :x="padding.left + (i / (xLabels.length - 1)) * chartW"
        :y="height - 14"
        text-anchor="middle"
        class="axis-label"
      >
        {{ l }}
      </text>
    </svg>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Series {
  securityId: string
  ticker: string
  labels: string[]
  values: number[]
  color: string
}

const props = withDefaults(defineProps<{
  series: Series[]
  loading?: boolean
  width?: number
  height?: number
}>(), {
  width: 900,
  height: 420,
})

const padding = { top: 24, right: 24, bottom: 50, left: 56 }
const chartW = computed(() => props.width - padding.left - padding.right)
const chartH = computed(() => props.height - padding.top - padding.bottom)
const gridYCount = 6

const allValues = computed(() => props.series.flatMap((s) => s.values))
const minValue = computed(() => {
  const m = Math.min(...allValues.value, 0)
  return Math.floor(m / 5) * 5
})
const maxValue = computed(() => {
  const m = Math.max(...allValues.value, 0)
  return Math.ceil(m / 5) * 5
})
const valueRange = computed(() => maxValue.value - minValue.value || 1)

const yLabels = computed(() => {
  return Array.from({ length: gridYCount }, (_, i) => {
    const v = minValue.value + (i / (gridYCount - 1)) * valueRange.value
    return `${v.toFixed(0)}%`
  })
})

const hasPositiveAndNegative = computed(() => minValue.value < 0 && maxValue.value > 0)
const zeroY = computed(() => padding.top + chartH.value - (0 - minValue.value) / valueRange.value * chartH.value)

const xLabels = computed(() => {
  if (props.series.length === 0) return []
  const labels = props.series[0].labels
  const count = labels.length
  if (count <= 6) return labels
  const step = Math.ceil(count / 6)
  const result: string[] = []
  for (let i = 0; i < count; i += step) {
    result.push(labels[i])
  }
  result.push(labels[count - 1])
  return [...new Set(result)]
})

const visibleSeries = computed(() => props.series.filter((s) => s.values.length > 0))

function getX(i: number, len: number): number {
  return padding.left + (i / (len - 1)) * chartW.value
}

function getY(v: number): number {
  return padding.top + chartH.value - ((v - minValue.value) / valueRange.value) * chartH.value
}

function pathFor(s: Series): string {
  if (s.values.length === 0) return ''
  return s.values
    .map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i, s.values.length)} ${getY(v)}`)
    .join(' ')
}

function visiblePoints(s: Series): { x: number; y: number }[] {
  if (s.values.length === 0) return []
  const len = s.values.length
  if (len <= 12) {
    return s.values.map((v, i) => ({ x: getX(i, len), y: getY(v) }))
  }
  const step = Math.ceil(len / 12)
  const result: { x: number; y: number }[] = []
  for (let i = 0; i < len; i += step) {
    result.push({ x: getX(i, len), y: getY(s.values[i]) })
  }
  result.push({ x: getX(len - 1, len), y: getY(s.values[len - 1]) })
  return result
}
</script>

<style scoped>
.equity-lines-chart {
  position: relative;
  width: 100%;
}

.chart-loading,
.chart-empty {
  display: grid;
  place-items: center;
  min-height: 360px;
  color: var(--text-muted);
  font-size: 14px;
  border: 1px dashed var(--border-medium);
}

.chart-svg {
  overflow: visible;
}

.grid line {
  stroke: var(--border-medium);
  stroke-width: 1;
  stroke-dasharray: 4 4;
}

.grid text {
  fill: var(--text-muted);
  font-size: 11px;
  font-family: var(--kimi-font-mono);
}

.zero-line {
  stroke: var(--text-muted);
  stroke-width: 1;
  stroke-dasharray: 6 4;
}

.series-line {
  transition: opacity 0.3s ease;
}

.series-point {
  transition: r 0.2s ease;
}

.axis-label {
  fill: var(--text-muted);
  font-size: 10px;
}
</style>
