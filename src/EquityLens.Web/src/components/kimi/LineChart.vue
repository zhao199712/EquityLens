<template>
  <svg
    ref="svgRef"
    width="100%"
    :height="height"
    :viewBox="`0 0 ${width} ${height}`"
    preserveAspectRatio="xMidYMid meet"
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
      :y="padding.top + chartH - (i / (yAxisLabels.length - 1)) * chartH + 4"
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
      :y="height - 10"
      text-anchor="end"
      :fill="textColor"
      font-size="10"
      font-family="Inter, sans-serif"
      :transform="`rotate(-40, ${getX(i)}, ${height - 10})`"
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
    <circle
      v-for="(v, i) in data"
      :key="'pt-' + i"
      :cx="getX(i)"
      :cy="getY(v)"
      r="4"
      :fill="lineColor"
    />
  </svg>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import gsap from 'gsap'

const props = withDefaults(defineProps<{
  data: number[]
  labels: string[]
  yAxisLabels: string[]
  width?: number
  height?: number
  lineColor?: string
  fillColor?: string
  gridColor?: string
  textColor?: string
  dark?: boolean
  showArea?: boolean
  showPoints?: boolean
  secondLine?: number[]
  secondLineColor?: string
  maxLabels?: number
}>(), {
  width: 800,
  height: 400,
  lineColor: '#000000',
  gridColor: '#E0E0E0',
  textColor: '#666666',
  dark: false,
  showArea: true,
  showPoints: true,
  secondLineColor: '#8B1A2B',
  maxLabels: 8,
})

const pathRef = ref<SVGPathElement>()
const areaRef = ref<SVGPathElement>()
const secondPathRef = ref<SVGPathElement>()

const padding = { top: 20, right: 20, bottom: 60, left: 70 }
const chartW = computed(() => props.width - padding.left - padding.right)
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

const visibleLabelIndices = computed(() => {
  const count = props.labels.length
  if (count <= props.maxLabels) {
    return props.labels.map((_, i) => i)
  }
  const step = Math.ceil(count / props.maxLabels)
  const indices = new Set<number>()
  for (let i = 0; i < count; i += step) {
    indices.add(i)
  }
  indices.add(count - 1)
  return Array.from(indices).sort((a, b) => a - b)
})

onMounted(() => {
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
})
</script>

<style scoped>
.kimi-line, .kimi-area {
  animation: fadeIn 0.6s ease-out;
}
@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}
</style>
