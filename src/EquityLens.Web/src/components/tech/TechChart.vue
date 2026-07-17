<script setup lang="ts">
import { computed } from 'vue'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { BarChart, LineChart, PieChart, RadarChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsCoreOption } from 'echarts/core'

// 全站唯一的 echarts 模組註冊點,首頁所有圖表共用此封裝
use([LineChart, BarChart, PieChart, RadarChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer])

const props = withDefaults(defineProps<{
  option: EChartsCoreOption
  height?: string
}>(), {
  height: '320px',
})

const baseOption: EChartsCoreOption = {
  backgroundColor: 'transparent',
  textStyle: { color: '#7d8aa8', fontFamily: 'Inter, Noto Sans TC, sans-serif' },
  tooltip: {
    backgroundColor: 'rgba(10, 17, 32, 0.92)',
    borderColor: 'rgba(56, 189, 248, 0.35)',
    textStyle: { color: '#e6f1ff', fontSize: 12 },
  },
}

const mergedOption = computed<EChartsCoreOption>(() => ({
  ...baseOption,
  ...props.option,
  tooltip: { ...baseOption.tooltip as object, ...(props.option.tooltip as object | undefined) },
}))
</script>

<template>
  <VChart :option="mergedOption" :style="{ height, width: '100%' }" autoresize />
</template>
