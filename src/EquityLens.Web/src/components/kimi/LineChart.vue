<template>
  <div ref="containerRef" class="kimi-line-chart">
    <svg
      ref="svgRef"
      :width="svgWidth"
      :height="height"
      :viewBox="`0 0 ${svgWidth} ${height}`"
      preserveAspectRatio="none"
      @mousemove="handleMouseMove"
      @mouseleave="handleMouseLeave"
    >
      <!-- Grid lines -->
      <line
        v-for="(_, i) in yAxisLabels"
        :key="'grid-' + i"
        :x1="padding.left"
        :y1="padding.top + (i / (yAxisLabels.length - 1)) * chartH"
        :x2="padding.left + chartW"
        :y2="padding.top + (i / (yAxisLabels.length - 1)) * chartH"
        :stroke="gridColor"
        stroke-width="1"
        stroke-dasharray="4 4"
      />

      <!-- Y axis labels -->
      <text
        v-for="(_, i) in yAxisLabels"
        :key="'y-' + i"
        :x="padding.left - 10"
        :y="padding.top + (i / (yAxisLabels.length - 1)) * chartH + 4"
        text-anchor="end"
        :fill="textColor"
        font-size="11"
        font-family="Inter, sans-serif"
      >
        {{ yAxisLabels[i] }}
      </text>

      <!-- X axis labels -->
      <text
        v-for="i in visibleLabelIndices"
        :key="'x-' + i"
        :x="getX(i)"
        :y="height - 14"
        text-anchor="middle"
        :fill="textColor"
        font-size="10"
        font-family="Inter, sans-serif"
      >
        {{ labels[i] }}
      </text>

      <!-- Area fill -->
      <path
        v-if="showArea && areaPath"
        ref="areaRef"
        :d="areaPath"
        :fill="fillColor || (dark ? 'rgba(255,255,255,0.03)' : 'rgba(0,0,0,0.03)')"
        class="kimi-area"
      />

      <!-- Main line -->
      <path
        ref="pathRef"
        :d="linePath"
        fill="none"
        :stroke="lineColor"
        stroke-width="2"
        class="kimi-line"
      />

      <!-- Second line -->
      <path
        v-if="secondLine"
        ref="secondPathRef"
        :d="secondLinePath"
        fill="none"
        :stroke="secondLineColor"
        stroke-width="2"
        class="kimi-line"
      />

      <!-- Data points -->
      <g v-if="shouldShowPoints">
        <circle
          v-for="(v, i) in data"
          :key="'pt-' + i"
          :cx="getX(i)"
          :cy="getY(v)"
          r="3"
          :fill="lineColor"
        />
      </g>

      <!-- Hover guide and tooltip -->
      <g v-if="hoveredIndex !== null && tooltipBox">
        <line
          :x1="tooltipX"
          :y1="padding.top"
          :x2="tooltipX"
          :y2="padding.top + chartH"
          stroke="#666666"
          stroke-width="1"
          stroke-dasharray="4 4"
        />
        <circle :cx="tooltipX" :cy="tooltipY" r="5" :fill="lineColor" />
        <rect
          :x="tooltipBox.x"
          :y="tooltipBox.y"
          :width="tooltipBox.w"
          :height="tooltipBox.h"
          fill="rgba(0, 0, 0, 0.85)"
          stroke="#333333"
          stroke-width="1"
          rx="4"
        />
        <text
          v-for="(line, i) in tooltipLines"
          :key="'tt-' + i"
          :x="tooltipBox.x + tooltipBox.pad"
          :y="tooltipBox.y + tooltipBox.pad + (i + 1) * tooltipBox.lineHeight - 4"
          fill="#FFFFFF"
          font-size="11"
          font-family="Inter, sans-serif"
        >
          {{ line }}
        </text>
      </g>
    </svg>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import gsap from 'gsap'

const props = withDefaults(defineProps<{
  data: number[]
  labels: string[]
  yAxisLabels: string[]
  height?: number
  lineColor?: string
  fillColor?: string
  gridColor?: string
  textColor?: string
  dark?: boolean
  showArea?: boolean
  showPoints?: boolean
  pointThreshold?: number
  showTooltip?: boolean
  tooltipFormatter?: (index: number) => string[]
  secondLine?: number[]
  secondLineColor?: string
  maxLabels?: number
}>(), {
  height: 400,
  lineColor: '#000000',
  gridColor: '#E0E0E0',
  textColor: '#666666',
  dark: false,
  showArea: true,
  showPoints: true,
  pointThreshold: 25,
  showTooltip: false,
  secondLineColor: '#8B1A2B',
  maxLabels: 8,
})

const containerRef = ref<HTMLDivElement>()
const svgRef = ref<SVGSVGElement>()
const pathRef = ref<SVGPathElement>()
const areaRef = ref<SVGPathElement>()
const secondPathRef = ref<SVGPathElement>()

const svgWidth = ref(800)
const hoveredIndex = ref<number | null>(null)
const tooltipX = ref(0)
const tooltipY = ref(0)

let resizeObserver: ResizeObserver | null = null

const padding = { top: 20, right: 20, bottom: 50, left: 90 }
const chartW = computed(() => svgWidth.value - padding.left - padding.right)
const chartH = computed(() => props.height - padding.top - padding.bottom)

const maxVal = computed(() => Math.max(...props.data, ...(props.secondLine || [])))
const minVal = computed(() => Math.min(...props.data, ...(props.secondLine || [])))
const range = computed(() => maxVal.value - minVal.value || 1)

const getX = (i: number) => padding.left + (i / (props.data.length - 1)) * chartW.value
const getY = (v: number) => padding.top + chartH.value - ((v - minVal.value) / range.value) * chartH.value

const linePath = computed(() =>
  props.data.map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`).join(' ')
)

const areaPath = computed(() => {
  if (!props.showArea) return ''
  return `${linePath.value} L ${getX(props.data.length - 1)} ${padding.top + chartH.value} L ${getX(0)} ${padding.top + chartH.value} Z`
})

const secondLinePath = computed(() => {
  if (!props.secondLine) return ''
  return props.secondLine.map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`).join(' ')
})

const adaptiveMaxLabels = computed(() => {
  const base = Math.max(4, Math.floor(svgWidth.value / 70))
  return Math.min(props.maxLabels, base)
})

const visibleLabelIndices = computed(() => {
  const count = props.labels.length
  const max = adaptiveMaxLabels.value
  if (count <= max) {
    return props.labels.map((_, i) => i)
  }
  const step = Math.ceil(count / max)
  const indices = new Set<number>()
  for (let i = 0; i < count; i += step) {
    indices.add(i)
  }
  indices.add(count - 1)
  return Array.from(indices).sort((a, b) => a - b)
})

const shouldShowPoints = computed(() => props.showPoints && props.data.length <= props.pointThreshold)

function updateWidth() {
  if (!containerRef.value) return
  const rect = containerRef.value.getBoundingClientRect()
  const width = Math.max(320, Math.floor(rect.width))
  if (width !== svgWidth.value) {
    svgWidth.value = width
  }
}

function scheduleAnimate() {
  nextTick(() => {
    requestAnimationFrame(() => {
      requestAnimationFrame(animateLines)
    })
  })
}

onMounted(() => {
  nextTick(() => {
    requestAnimationFrame(() => {
      updateWidth()
      scheduleAnimate()
    })
  })

  if ('ResizeObserver' in window && containerRef.value) {
    resizeObserver = new ResizeObserver(() => {
      const previousWidth = svgWidth.value
      updateWidth()
      if (svgWidth.value !== previousWidth) {
        scheduleAnimate()
      }
    })
    resizeObserver.observe(containerRef.value)
  } else {
    window.addEventListener('resize', updateWidth)
  }
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  window.removeEventListener('resize', updateWidth)
})

watch(() => props.data, scheduleAnimate, { deep: true })

function animateLines() {
  if (pathRef.value) {
    const length = pathRef.value.getTotalLength()
    gsap.set(pathRef.value, { strokeDasharray: length, strokeDashoffset: length })
    gsap.to(pathRef.value, { strokeDashoffset: 0, duration: 2, ease: 'power2.out' })
  }
  if (areaRef.value) {
    gsap.fromTo(areaRef.value, { opacity: 0 }, { opacity: 1, duration: 1, delay: 0.5 })
  }
  if (secondPathRef.value) {
    const length = secondPathRef.value.getTotalLength()
    gsap.set(secondPathRef.value, { strokeDasharray: length, strokeDashoffset: length })
    gsap.to(secondPathRef.value, { strokeDashoffset: 0, duration: 1.5, ease: 'power2.out', delay: 0.5 })
  }
}

function handleMouseMove(e: MouseEvent) {
  if (!props.showTooltip || !props.tooltipFormatter || props.data.length === 0 || !svgRef.value) return

  const rect = svgRef.value.getBoundingClientRect()
  const mouseX = e.clientX - rect.left
  // Convert screen coordinate to SVG user-space coordinate, accounting for CSS scaling.
  const svgX = (mouseX / rect.width) * svgWidth.value
  const fraction = (svgX - padding.left) / chartW.value
  const index = Math.round(Math.max(0, Math.min(1, fraction)) * (props.data.length - 1))
  const safeIndex = Math.max(0, Math.min(props.data.length - 1, index))

  hoveredIndex.value = safeIndex
  tooltipX.value = getX(safeIndex)
  tooltipY.value = getY(props.data[safeIndex])
}

function handleMouseLeave() {
  hoveredIndex.value = null
}

const tooltipLines = computed(() => {
  if (hoveredIndex.value === null || !props.tooltipFormatter) return []
  return props.tooltipFormatter(hoveredIndex.value)
})

const tooltipBox = computed(() => {
  if (hoveredIndex.value === null || tooltipLines.value.length === 0) return null
  const lineHeight = 16
  const pad = 8
  const boxW = 150
  const boxH = tooltipLines.value.length * lineHeight + pad * 2
  let x = tooltipX.value + 12
  if (x + boxW > svgWidth.value) x = tooltipX.value - boxW - 12
  let y = tooltipY.value - boxH - 12
  if (y < 0) y = tooltipY.value + 12
  return { x, y, w: boxW, h: boxH, lineHeight, pad }
})
</script>

<style scoped>
.kimi-line-chart {
  width: 100%;
  height: v-bind('`${height}px`');
}

.kimi-line-chart svg {
  display: block;
  width: 100%;
  height: 100%;
}

.kimi-line, .kimi-area {
  animation: fadeIn 0.6s ease-out;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}
</style>
