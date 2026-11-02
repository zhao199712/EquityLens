<template>
  <svg :width="width" :height="height" :viewBox="`0 0 ${width} ${height}`" class="w-full">
    <g v-for="(seg, i) in segmentPaths" :key="`seg-${i}`">
      <path
        :d="seg.d"
        :fill="seg.data.color"
        :stroke="seg.data.borderColor || 'none'"
        :stroke-width="seg.data.borderColor ? 1 : 0"
        :transform="currentHover === i ? `translate(${5 * Math.cos(seg.midAngle)}, ${5 * Math.sin(seg.midAngle)})` : ''"
        class="cursor-pointer transition-transform duration-300"
        @mouseenter="hovered = i"
        @mouseleave="hovered = null"
      />
      <line
        :x1="cx + outerR * Math.cos(seg.midAngle)"
        :y1="cy + outerR * Math.sin(seg.midAngle)"
        :x2="cx + (outerR + 30) * Math.cos(seg.midAngle)"
        :y2="cy + (outerR + 30) * Math.sin(seg.midAngle)"
        stroke="#E0E0E0" stroke-width="1"
      />
      <text
        :x="cx + (outerR + 40) * Math.cos(seg.midAngle)"
        :y="cy + (outerR + 40) * Math.sin(seg.midAngle) + 4"
        :text-anchor="Math.cos(seg.midAngle) > 0 ? 'start' : 'end'"
        fill="#666666" font-size="11" font-family="Inter, sans-serif"
      >{{ seg.data.label }} {{ Math.round((seg.data.value / total) * 100) }}%</text>
    </g>
    <text :x="cx" :y="cy - 6" text-anchor="middle" fill="#000000" font-size="20" font-weight="600" font-family="Noto Sans TC, sans-serif">{{ centerLabel }}</text>
    <text v-if="centerSubLabel" :x="cx" :y="cy + 14" text-anchor="middle" fill="#666666" font-size="11" font-family="Inter, sans-serif">{{ centerSubLabel }}</text>
  </svg>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

interface Segment {
  label: string
  value: number
  color: string
  borderColor?: string
}

const props = defineProps<{
  segments: Segment[]
  centerLabel?: string
  centerSubLabel?: string
  width?: number
  height?: number
  activeIndex?: number | null
}>()

const emit = defineEmits<{ segmentHover: [index: number | null] }>()

const hovered = ref<number | null>(null)
const width = props.width ?? 360
const height = props.height ?? 360
const cx = width / 2
const cy = height / 2
const outerR = 120
const innerR = 75
const total = props.segments.reduce((s, seg) => s + seg.value, 0)

const currentHover = computed(() => hovered.value !== null ? hovered.value : props.activeIndex)

const segmentPaths = computed(() => {
  let startAngle = -Math.PI / 2
  return props.segments.map((seg) => {
    const angle = (seg.value / total) * Math.PI * 2
    const endAngle = startAngle + angle
    const midAngle = startAngle + angle / 2

    const x1 = cx + outerR * Math.cos(startAngle)
    const y1 = cy + outerR * Math.sin(startAngle)
    const x2 = cx + outerR * Math.cos(endAngle)
    const y2 = cy + outerR * Math.sin(endAngle)
    const x3 = cx + innerR * Math.cos(endAngle)
    const y3 = cy + innerR * Math.sin(endAngle)
    const x4 = cx + innerR * Math.cos(startAngle)
    const y4 = cy + innerR * Math.sin(startAngle)
    const largeArc = angle > Math.PI ? 1 : 0

    const d = `M ${x1} ${y1} A ${outerR} ${outerR} 0 ${largeArc} 1 ${x2} ${y2} L ${x3} ${y3} A ${innerR} ${innerR} 0 ${largeArc} 0 ${x4} ${y4} Z`
    startAngle = endAngle
    return { d, midAngle, data: seg }
  })
})
</script>
