<template>
  <svg width="100%" :height="height" :viewBox="`0 0 ${width} ${height}`">
    <!-- Grid -->
    <line
      v-for="(_, i) in xTicks"
      :key="'xgrid-' + i"
      :x1="padding.left + (i / (xTicks - 1)) * chartW"
      :y1="padding.top"
      :x2="padding.left + (i / (xTicks - 1)) * chartW"
      :y2="padding.top + chartH"
      stroke="#E0E0E0"
      stroke-width="1"
      stroke-dasharray="4 4"
    />
    <line
      v-for="(_, i) in yTicksArr"
      :key="'ygrid-' + i"
      :x1="padding.left"
      :y1="padding.top + (i / (yTicks - 1)) * chartH"
      :x2="padding.left + chartW"
      :y2="padding.top + (i / (yTicks - 1)) * chartH"
      stroke="#E0E0E0"
      stroke-width="1"
      stroke-dasharray="4 4"
    />

    <!-- X axis ticks -->
    <text
      v-for="(_, i) in xTicks"
      :key="'xtick-' + i"
      :x="padding.left + (i / (xTicks - 1)) * chartW"
      :y="height - 15"
      text-anchor="middle"
      fill="#666666"
      font-size="10"
    >
      {{ Math.round(xRange[0] + (i / (xTicks - 1)) * (xRange[1] - xRange[0])) }}%
    </text>

    <!-- Y axis ticks -->
    <text
      v-for="(_, i) in yTicksArr"
      :key="'ytick-' + i"
      :x="padding.left - 8"
      :y="padding.top + chartH - (i / (yTicks - 1)) * chartH + 3"
      text-anchor="end"
      fill="#666666"
      font-size="10"
    >
      {{ Math.round(yRange[0] + (i / (yTicks - 1)) * (yRange[1] - yRange[0])) }}%
    </text>

    <!-- Axis labels -->
    <text :x="padding.left + chartW / 2" :y="height - 2" text-anchor="middle" fill="#666666" font-size="11">
      {{ xAxisLabel }}
    </text>
    <text
      x="12"
      :y="padding.top + chartH / 2"
      text-anchor="middle"
      fill="#666666"
      font-size="11"
      :transform="`rotate(-90, 12, ${padding.top + chartH / 2})`"
    >
      {{ yAxisLabel }}
    </text>

    <!-- Frontier curve -->
    <path
      v-if="frontierCurve"
      :d="frontierCurve.map(([x, y], i) => `${i === 0 ? 'M' : 'L'} ${getX(x)} ${getY(y)}`).join(' ')"
      fill="none"
      stroke="#999999"
      stroke-width="1"
      stroke-dasharray="4 4"
    />

    <!-- Data points -->
    <g
      v-for="(point, i) in data"
      :key="'pt-' + i"
      @mouseenter="hoveredPoint = i"
      @mouseleave="hoveredPoint = null"
      class="cursor-pointer"
    >
      <circle
        :cx="getX(point.x)"
        :cy="getY(point.y)"
        :r="hoveredPoint === i ? 10 : 6"
        fill="#000000"
        style="transition: r 0.2s ease"
      />
      <g v-if="hoveredPoint === i">
        <rect
          :x="getX(point.x) + 12"
          :y="getY(point.y) - 25"
          width="120"
          height="22"
          fill="#000000"
        />
        <text
          :x="getX(point.x) + 17"
          :y="getY(point.y) - 10"
          fill="#FFFFFF"
          font-size="11"
        >
          {{ point.label }}: {{ point.x }}%, {{ point.y }}%
        </text>
      </g>
    </g>
  </svg>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

interface DataPoint {
  x: number
  y: number
  label: string
}

const props = withDefaults(defineProps<{
  data: DataPoint[]
  xAxisLabel: string
  yAxisLabel: string
  xRange: [number, number]
  yRange: [number, number]
  width?: number
  height?: number
  frontierCurve?: [number, number][]
}>(), {
  width: 300,
  height: 300,
})

const hoveredPoint = ref<number | null>(null)

const padding = { top: 20, right: 20, bottom: 50, left: 55 }
const chartW = computed(() => props.width - padding.left - padding.right)
const chartH = computed(() => props.height - padding.top - padding.bottom)
const xTicks = 6
const yTicks = 6
const yTicksArr = computed(() => Array.from({ length: yTicks }))

const getX = (v: number) => padding.left + ((v - props.xRange[0]) / (props.xRange[1] - props.xRange[0])) * chartW.value
const getY = (v: number) => padding.top + chartH.value - ((v - props.yRange[0]) / (props.yRange[1] - props.yRange[0])) * chartH.value
</script>
