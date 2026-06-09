<template>
  <svg ref="svgRef" :width="width" :height="height" :viewBox="`0 0 ${width} ${height}`">
    <g
      v-for="(path, i) in paths"
      :key="i"
      @mouseenter="handleHover(i)"
      @mouseleave="handleHover(null)"
      class="cursor-pointer"
      :style="{
        transform: `translate(${currentHover === i ? 5 * Math.cos(path.midAngle) : 0}px, ${currentHover === i ? 5 * Math.sin(path.midAngle) : 0}px)`,
        transition: 'transform 0.3s ease',
      }"
    >
      <path
        :d="path.d"
        :fill="path.seg.color"
        :stroke="path.seg.borderColor || 'none'"
        :stroke-width="path.seg.borderColor ? 1 : 0"
      />
      <line
        :x1="cx + outerR * Math.cos(path.midAngle)"
        :y1="cy + outerR * Math.sin(path.midAngle)"
        :x2="cx + (outerR + 30) * Math.cos(path.midAngle)"
        :y2="cy + (outerR + 30) * Math.sin(path.midAngle)"
        :stroke="dark ? '#333333' : '#E0E0E0'"
        stroke-width="1"
      />
      <text
        :x="cx + (outerR + 40) * Math.cos(path.midAngle)"
        :y="cy + (outerR + 40) * Math.sin(path.midAngle) + 4"
        :text-anchor="Math.cos(path.midAngle) > 0 ? 'start' : 'end'"
        fill="#666666"
        font-size="11"
        font-family="Inter, sans-serif"
      >
        {{ path.seg.label }} {{ Math.round((path.seg.value / total) * 100) }}%
      </text>
    </g>

    <!-- Center label -->
    <g v-if="centerLabel">
      <text
        :x="cx"
        :y="cy - 6"
        text-anchor="middle"
        :fill="dark ? '#FFFFFF' : '#000000'"
        font-size="20"
        font-weight="600"
        font-family="Noto Sans TC, sans-serif"
      >
        {{ centerLabel }}
      </text>
      <text
        v-if="centerSubLabel"
        :x="cx"
        :y="cy + 14"
        text-anchor="middle"
        fill="#666666"
        font-size="11"
        font-family="Inter, sans-serif"
      >
        {{ centerSubLabel }}
      </text>
    </g>
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

const props = withDefaults(defineProps<{
  segments: Segment[]
  centerLabel?: string
  centerSubLabel?: string
  width?: number
  height?: number
  activeIndex?: number | null
  dark?: boolean
}>(), {
  width: 360,
  height: 360,
  dark: false,
})

const emit = defineEmits<{
  segmentHover: [index: number | null]
}>()

const hovered = ref<number | null>(null)

const cx = computed(() => props.width / 2)
const cy = computed(() => props.height / 2)
const outerR = 140
const innerR = 90
const total = computed(() => props.segments.reduce((s, seg) => s + seg.value, 0))

const paths = computed(() => {
  let startAngle = -Math.PI / 2
  return props.segments.map((seg) => {
    const angle = (seg.value / total.value) * Math.PI * 2
    const endAngle = startAngle + angle

    const x1 = cx.value + outerR * Math.cos(startAngle)
    const y1 = cy.value + outerR * Math.sin(startAngle)
    const x2 = cx.value + outerR * Math.cos(endAngle)
    const y2 = cy.value + outerR * Math.sin(endAngle)
    const x3 = cx.value + innerR * Math.cos(endAngle)
    const y3 = cy.value + innerR * Math.sin(endAngle)
    const x4 = cx.value + innerR * Math.cos(startAngle)
    const y4 = cy.value + innerR * Math.sin(startAngle)

    const largeArc = angle > Math.PI ? 1 : 0
    const d = `M ${x1} ${y1} A ${outerR} ${outerR} 0 ${largeArc} 1 ${x2} ${y2} L ${x3} ${y3} A ${innerR} ${innerR} 0 ${largeArc} 0 ${x4} ${y4} Z`
    const midAngle = startAngle + angle / 2
    startAngle = endAngle

    return { d, midAngle, seg }
  })
})

const currentHover = computed(() => hovered.value !== null ? hovered.value : props.activeIndex)

const handleHover = (index: number | null) => {
  hovered.value = index
  emit('segmentHover', index)
}
</script>
