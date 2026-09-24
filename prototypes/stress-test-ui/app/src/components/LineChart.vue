<template>
  <svg ref="svgRef" :viewBox="`0 0 ${width} ${height}`" class="w-full" preserveAspectRatio="xMidYMid meet">
    <!-- Grid lines -->
    <line
      v-for="(_, i) in yAxisLabels" :key="`grid-${i}`"
      :x1="padding.left" :y1="padding.top + (i / (yAxisLabels.length - 1)) * chartH"
      :x2="width - padding.right" :y2="padding.top + (i / (yAxisLabels.length - 1)) * chartH"
      :stroke="gridColor" stroke-width="1" stroke-dasharray="4 4"
    />
    <!-- Y axis labels -->
    <text
      v-for="(label, i) in yAxisLabels" :key="`y-${i}`"
      :x="padding.left - 10"
      :y="padding.top + chartH - (i / (yAxisLabels.length - 1)) * chartH + 4"
      text-anchor="end" :fill="textColor" font-size="11" font-family="Inter, sans-serif"
    >{{ label }}</text>
    <!-- X axis labels -->
    <text
      v-for="(label, i) in labels" :key="`x-${i}`"
      :x="getX(i)" :y="height - 10"
      text-anchor="middle" :fill="textColor" font-size="11" font-family="Inter, sans-serif"
    >{{ label }}</text>
    <!-- Area fill -->
    <path v-if="showArea" ref="areaRef" :d="areaPath" :fill="fillColor || (darkMode ? 'rgba(255,255,255,0.03)' : 'rgba(0,0,0,0.03)')" opacity="0" />
    <!-- Main line -->
    <path ref="pathRef" :d="linePath" fill="none" :stroke="lineColor" stroke-width="2" />
    <!-- Second line -->
    <path v-if="secondLine" ref="secondPathRef" :d="secondLinePath" fill="none" :stroke="secondLineColor" stroke-width="2" />
    <!-- Data points -->
    <circle
      v-for="(v, i) in data" :key="`pt-${i}`"
      :cx="getX(i)" :cy="getY(v)" r="4"
      :fill="lineColor" class="cursor-pointer hover:r-6 transition-all"
    />
  </svg>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

const props = defineProps<{
  data: number[]
  labels: string[]
  yAxisLabels: string[]
  width?: number
  height?: number
  lineColor?: string
  fillColor?: string
  gridColor?: string
  textColor?: string
  darkMode?: boolean
  showArea?: boolean
  secondLine?: number[]
  secondLineColor?: string
}>()

const svgRef = ref<SVGSVGElement>()
const pathRef = ref<SVGPathElement>()
const areaRef = ref<SVGPathElement>()
const secondPathRef = ref<SVGPathElement>()

const width = props.width ?? 800
const height = props.height ?? 400
const padding = { top: 20, right: 20, bottom: 40, left: 70 }
const chartW = width - padding.left - padding.right
const chartH = height - padding.top - padding.bottom
const lineColor = props.lineColor ?? '#000000'
const gridColor = props.gridColor ?? '#E0E0E0'
const textColor = props.textColor ?? '#666666'
const secondLineColor = props.secondLineColor ?? '#8B1A2B'

const allVals = [...props.data, ...(props.secondLine ?? [])]
const maxVal = Math.max(...allVals)
const minVal = Math.min(...allVals)
const range = maxVal - minVal || 1

const getX = (i: number) => padding.left + (i / (props.data.length - 1)) * chartW
const getY = (v: number) => padding.top + chartH - ((v - minVal) / range) * chartH

const linePath = props.data.map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`).join(' ')
const areaPath = `${linePath} L ${getX(props.data.length - 1)} ${padding.top + chartH} L ${getX(0)} ${padding.top + chartH} Z`
const secondLinePath = props.secondLine?.map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`).join(' ') ?? ''

onMounted(() => {
  if (!svgRef.value) return
  const tl = gsap.timeline({
    scrollTrigger: { trigger: svgRef.value, start: 'top 80%', toggleActions: 'play none none none' }
  })

  if (pathRef.value) {
    const len = pathRef.value.getTotalLength()
    gsap.set(pathRef.value, { strokeDasharray: len, strokeDashoffset: len })
    tl.to(pathRef.value, { strokeDashoffset: 0, duration: 2, ease: 'power2.out' })
  }

  if (secondPathRef.value && props.secondLine) {
    const len = secondPathRef.value.getTotalLength()
    gsap.set(secondPathRef.value, { strokeDasharray: len, strokeDashoffset: len })
    tl.to(secondPathRef.value, { strokeDashoffset: 0, duration: 1.5, ease: 'power2.out' }, '-=1')
  }

  if (areaRef.value && props.showArea) {
    gsap.set(areaRef.value, { opacity: 0 })
    tl.to(areaRef.value, { opacity: 1, duration: 1 }, '-=1')
  }
})
</script>
