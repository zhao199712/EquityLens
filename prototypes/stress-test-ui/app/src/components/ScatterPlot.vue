<template>
  <svg :width="width" :height="height" :viewBox="`0 0 ${width} ${height}`" class="w-full">
    <line v-for="i in 6" :key="`xg-${i}`"
      :x1="pad.l + ((i-1)/5)*chartW" :y1="pad.t" :x2="pad.l + ((i-1)/5)*chartW" :y2="pad.t+chartH"
      stroke="#E0E0E0" stroke-width="1" stroke-dasharray="4 4" />
    <line v-for="i in 6" :key="`yg-${i}`"
      :x1="pad.l" :y1="pad.t + ((i-1)/5)*chartH" :x2="pad.l+chartW" :y2="pad.t + ((i-1)/5)*chartH"
      stroke="#E0E0E0" stroke-width="1" stroke-dasharray="4 4" />
    <text v-for="i in 6" :key="`xt-${i}`"
      :x="pad.l + ((i-1)/5)*chartW" :y="height-15" text-anchor="middle" fill="#666666" font-size="10">
      {{ Math.round(xMin + ((i-1)/5)*(xMax-xMin)) }}%
    </text>
    <text v-for="i in 6" :key="`yt-${i}`"
      :x="pad.l-8" :y="pad.t+chartH - ((i-1)/5)*chartH + 3" text-anchor="end" fill="#666666" font-size="10">
      {{ Math.round(yMin + ((i-1)/5)*(yMax-yMin)) }}%
    </text>
    <text :x="pad.l+chartW/2" :y="height-2" text-anchor="middle" fill="#666666" font-size="11">{{ xAxisLabel }}</text>
    <text x="12" :y="pad.t+chartH/2" text-anchor="middle" fill="#666666" font-size="11"
      :transform="`rotate(-90, 12, ${pad.t+chartH/2})`">{{ yAxisLabel }}</text>
    <path v-if="frontierCurve" :d="frontierPath" fill="none" stroke="#999999" stroke-width="1" stroke-dasharray="4 4" />
    <g v-for="(pt, i) in data" :key="`pt-${i}`" @mouseenter="hovered=i" @mouseleave="hovered=null" class="cursor-pointer">
      <circle :cx="sX(pt.x)" :cy="sY(pt.y)" :r="hovered===i?10:6" fill="#000000" class="transition-all duration-200" />
      <g v-if="hovered===i">
        <rect :x="sX(pt.x)+14" :y="sY(pt.y)-28" width="140" height="22" fill="#000000" />
        <text :x="sX(pt.x)+19" :y="sY(pt.y)-12" fill="#FFFFFF" font-size="11">{{ pt.label }}: {{ pt.x }}%, {{ pt.y }}%</text>
      </g>
    </g>
  </svg>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

const props = defineProps<{
  data: { x: number; y: number; label: string }[]
  xAxisLabel: string
  yAxisLabel: string
  xRange: [number, number]
  yRange: [number, number]
  width?: number
  height?: number
  frontierCurve?: [number, number][]
}>()

const hovered = ref<number | null>(null)
const w = props.width ?? 300
const h = props.height ?? 300
const pad = { t: 20, r: 20, b: 50, l: 55 }
const chartW = w - pad.l - pad.r
const chartH = h - pad.t - pad.b
const xMin = props.xRange[0], xMax = props.xRange[1]
const yMin = props.yRange[0], yMax = props.yRange[1]
const sX = (v: number) => pad.l + ((v - xMin) / (xMax - xMin)) * chartW
const sY = (v: number) => pad.t + chartH - ((v - yMin) / (yMax - yMin)) * chartH
const frontierPath = computed(() =>
  props.frontierCurve?.map(([x, y], i) => `${i===0?'M':'L'} ${sX(x)} ${sY(y)}`).join(' ') ?? ''
)
</script>
