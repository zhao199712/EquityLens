<template>
  <svg width="100%" :height="height" :viewBox="`0 0 ${width} ${height}`">
    <!-- Grid lines -->
    <line
      v-for="(_, i) in yAxisLabels"
      :key="'grid-' + i"
      :x1="padding.left"
      :y1="padding.top + (i / (yAxisLabels.length - 1)) * chartH"
      :x2="width - padding.right"
      :y2="padding.top + (i / (yAxisLabels.length - 1)) * chartH"
      stroke="#333333"
      stroke-width="1"
      stroke-dasharray="4 4"
    />

    <!-- Zero line -->
    <line
      :x1="padding.left"
      :y1="zeroY"
      :x2="width - padding.right"
      :y2="zeroY"
      stroke="#666666"
      stroke-width="2"
    />

    <!-- Y axis labels -->
    <text
      v-for="(label, i) in yAxisLabels"
      :key="'y-' + i"
      :x="padding.left - 10"
      :y="padding.top + chartH - (i / (yAxisLabels.length - 1)) * chartH + 4"
      text-anchor="end"
      fill="#666666"
      font-size="11"
    >
      {{ label }}
    </text>

    <!-- Bars -->
    <g v-for="(group, gi) in data" :key="'group-' + gi">
      <rect
        v-for="(v, vi) in group.values"
        :key="'bar-' + gi + '-' + vi"
        :x="padding.left + gi * barGroupWidth + barGroupWidth / 2 - barWidth / 2"
        :y="getBarY(group.values, vi)"
        :width="barWidth"
        :height="getBarH(v)"
        :fill="colors[vi]"
        class="kimi-stack-bar"
      />
      <text
        :x="padding.left + gi * barGroupWidth + barGroupWidth / 2"
        :y="height - 15"
        text-anchor="middle"
        fill="#666666"
        font-size="11"
      >
        {{ group.label }}
      </text>
    </g>

    <!-- Line overlay -->
    <g v-if="lineData">
      <path
        :d="lineData.map((v, i) => `${i === 0 ? 'M' : 'L'} ${padding.left + i * barGroupWidth + barGroupWidth / 2} ${getLineY(v)}`).join(' ')"
        fill="none"
        :stroke="lineColor"
        stroke-width="2"
      />
      <circle
        v-for="(v, i) in lineData"
        :key="'lp-' + i"
        :cx="padding.left + i * barGroupWidth + barGroupWidth / 2"
        :cy="getLineY(v)"
        r="4"
        :fill="lineColor"
      />
    </g>

    <!-- Legend -->
    <g :transform="`translate(${padding.left}, ${height - 5})`">
      <g v-for="(label, i) in legendLabels" :key="'legend-' + i" :transform="`translate(${i * 120}, 0)`">
        <rect x="0" y="-8" width="10" height="10" :fill="colors[i]" />
        <text x="16" y="0" fill="#666666" font-size="10">{{ label }}</text>
      </g>
    </g>
  </svg>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  data: { label: string; values: number[] }[]
  colors: string[]
  legendLabels: string[]
  yAxisLabels: string[]
  width?: number
  height?: number
  lineData?: number[]
  lineColor?: string
}>(), {
  width: 800,
  height: 380,
  lineColor: '#8B1A2B',
})

const padding = { top: 20, right: 60, bottom: 50, left: 60 }
const chartW = computed(() => props.width - padding.left - padding.right)
const chartH = computed(() => props.height - padding.top - padding.bottom)

const maxVal = computed(() =>
  Math.max(
    ...props.data.map((d) => d.values.reduce((s, v) => s + Math.max(0, v), 0)),
    ...(props.lineData || [0])
  )
)
const minVal = computed(() =>
  Math.min(
    ...props.data.map((d) => d.values.reduce((s, v) => s + Math.min(0, v), 0)),
    ...(props.lineData || [0])
  )
)
const range = computed(() => maxVal.value - minVal.value || 1)

const zeroY = computed(() => padding.top + chartH.value - ((0 - minVal.value) / range.value) * chartH.value)
const barGroupWidth = computed(() => chartW.value / props.data.length)
const barWidth = computed(() => Math.min(barGroupWidth.value * 0.5, 30))

const getBarH = (v: number) => Math.abs((v / range.value) * chartH.value)
const getLineY = (v: number) => padding.top + chartH.value - ((v - minVal.value) / range.value) * chartH.value

const getBarY = (values: number[], vi: number) => {
  let y = zeroY.value
  for (let j = 0; j < vi; j++) {
    if (values[j] >= 0) y -= getBarH(values[j])
    else y += getBarH(values[j])
  }
  return values[vi] >= 0 ? y - getBarH(values[vi]) : y
}
</script>

<style scoped>
.kimi-stack-bar {
  animation: stackGrow 0.8s ease-out forwards;
  transform-origin: bottom;
}
@keyframes stackGrow {
  from { transform: scaleY(0); }
  to { transform: scaleY(1); }
}
</style>
