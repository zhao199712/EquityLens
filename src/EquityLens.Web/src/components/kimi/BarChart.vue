<template>
  <svg ref="svgRef" width="100%" :height="chartH" :viewBox="`0 0 ${width} ${chartH}`">
    <g v-for="(item, i) in data" :key="'bar-' + i">
      <text
        x="0"
        :y="10 + i * rowHeight + barHeight / 2 + 4"
        :fill="dark ? '#FFFFFF' : '#000000'"
        font-size="12"
        font-family="Inter, sans-serif"
      >
        {{ item.label }}
      </text>
      <rect
        :x="labelWidth"
        :y="10 + i * rowHeight"
        :width="(Math.abs(item.value) / maxVal) * barMaxWidth"
        :height="barHeight"
        :fill="item.color || (dark ? '#FFFFFF' : '#000000')"
        class="kimi-bar"
      />
      <text
        :x="labelWidth + (Math.abs(item.value) / maxVal) * barMaxWidth + 8"
        :y="10 + i * rowHeight + barHeight / 2 + 4"
        :fill="textColor"
        font-size="12"
        font-family="Inter, sans-serif"
      >
        {{ item.value > 0 ? '+' : '' }}{{ item.value }}%
      </text>
    </g>
  </svg>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  data: { label: string; value: number; color?: string }[]
  width?: number
  barHeight?: number
  textColor?: string
  dark?: boolean
}>(), {
  width: 400,
  barHeight: 24,
  textColor: '#000000',
  dark: false,
})

const maxVal = computed(() => Math.max(...props.data.map((d) => Math.abs(d.value))))
const rowHeight = computed(() => props.barHeight + 16)
const chartH = computed(() => props.data.length * rowHeight.value + 20)
const labelWidth = 100
const barMaxWidth = computed(() => props.width - labelWidth - 80)
</script>

<style scoped>
.kimi-bar {
  animation: barGrow 0.8s ease-out forwards;
  transform-origin: left;
}
@keyframes barGrow {
  from { width: 0; }
}
</style>
