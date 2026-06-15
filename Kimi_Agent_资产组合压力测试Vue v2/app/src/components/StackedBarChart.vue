<template>
  <svg ref="svgRef" :width="width" :height="height" :viewBox="`0 0 ${width} ${height}`" class="w-full">
    <line v-for="i in yLabels.length" :key="`g-${i}`"
      :x1="pad.l" :y1="pad.t + ((i-1)/(yLabels.length-1))*chartH"
      :x2="width-pad.r" :y2="pad.t + ((i-1)/(yLabels.length-1))*chartH"
      stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
    <text v-for="(l, i) in yLabels" :key="`y-${i}`"
      :x="pad.l-10" :y="pad.t+chartH-((i)/(yLabels.length-1))*chartH+4"
      text-anchor="end" fill="#666666" font-size="11">{{ l }}</text>
    <line :x1="pad.l" :y1="zeroY" :x2="width-pad.r" :y2="zeroY" stroke="#666666" stroke-width="2" />
    <g v-for="(g, gi) in data" :key="`grp-${gi}`">
      <rect v-for="(v, vi) in g.values" :key="`b-${gi}-${vi}`"
        class="stack-bar"
        :x="pad.l + gi*barGroupW + barGroupW/2 - barW/2"
        :y="getBarY(v, vi, g.values)"
        :width="barW"
        :height="Math.abs((v/yRange)*chartH)"
        :fill="colors[vi]" />
      <text :x="pad.l + gi*barGroupW + barGroupW/2" :y="height-15" text-anchor="middle" fill="#666666" font-size="10">{{ g.label }}</text>
    </g>
    <g v-if="lineData">
      <path ref="lineRef"
        :d="lineData.map((v,i) => `${i===0?'M':'L'} ${pad.l + i*barGroupW + barGroupW/2} ${getLineY(v)}`).join(' ')"
        fill="none" stroke="#8B1A2B" stroke-width="2" />
      <circle v-for="(v, i) in lineData" :key="`lp-${i}`"
        :cx="pad.l + i*barGroupW + barGroupW/2" :cy="getLineY(v)" r="4" fill="#8B1A2B" />
    </g>
    <g :transform="`translate(${pad.l}, ${height-5})`">
      <g v-for="(l, i) in labels" :key="`leg-${i}`" :transform="`translate(${i*120}, 0)`">
        <rect :x="0" :y="-8" width="10" height="10" :fill="colors[i]" />
        <text :x="16" :y="0" fill="#666666" font-size="10">{{ l }}</text>
      </g>
    </g>
  </svg>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import gsap from 'gsap'

const props = defineProps<{
  data: { label: string; values: number[] }[]
  colors: string[]
  labels: string[]
  yAxisLabels: string[]
  width?: number
  height?: number
  lineData?: number[]
}>()

const width = props.width ?? 800
const height = props.height ?? 380
const pad = { t: 20, r: 60, b: 50, l: 60 }
const chartW = width - pad.l - pad.r
const chartH = height - pad.t - pad.b
const yLabels = props.yAxisLabels

const maxV = Math.max(...props.data.map(d => d.values.reduce((s, v) => s + Math.max(0, v), 0)), ...(props.lineData ?? [0]))
const minV = Math.min(...props.data.map(d => d.values.reduce((s, v) => s + Math.min(0, v), 0)), ...(props.lineData ?? [0]))
const yRange = maxV - minV || 1
const zeroY = pad.t + chartH - ((0 - minV) / yRange) * chartH
const barGroupW = chartW / props.data.length
const barW = Math.min(barGroupW * 0.5, 30)

function getBarY(v: number, vi: number, allVals: number[]) {
  let offset = 0
  for (let i = 0; i < vi; i++) {
    offset += Math.abs((allVals[i] / yRange) * chartH)
  }
  return v >= 0 ? zeroY - offset - Math.abs((v / yRange) * chartH) : zeroY + offset
}
const getLineY = (v: number) => pad.t + chartH - ((v - minV) / yRange) * chartH

const svgRef = ref<SVGSVGElement>()
const lineRef = ref<SVGPathElement>()

onMounted(() => {
  if (!svgRef.value) return
  const bars = svgRef.value.querySelectorAll('.stack-bar')
  bars.forEach((bar, i) => {
    gsap.fromTo(bar, { scaleY: 0, transformOrigin: 'bottom' }, {
      scaleY: 1, duration: 0.8, delay: i * 0.05, ease: 'power2.out',
      scrollTrigger: { trigger: svgRef.value, start: 'top 80%' }
    })
  })
  if (lineRef.value && props.lineData) {
    const len = lineRef.value.getTotalLength()
    gsap.set(lineRef.value, { strokeDasharray: len, strokeDashoffset: len })
    gsap.to(lineRef.value, { strokeDashoffset: 0, duration: 1.5, ease: 'power2.out',
      scrollTrigger: { trigger: svgRef.value, start: 'top 80%' }
    })
  }
})
</script>
