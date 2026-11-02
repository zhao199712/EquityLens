<template>
  <div ref="tableRef" class="w-full overflow-x-auto">
    <table class="w-full" style="border-collapse: collapse">
      <thead>
        <tr :style="{ backgroundColor: darkMode ? '#333333' : '#000000' }">
          <th
            v-for="(h, i) in headers" :key="`h-${i}`"
            class="text-caption uppercase text-left px-4"
            :style="{ color: '#FFFFFF', height: compact ? 40 : 48, borderBottom: `1px solid ${darkMode ? '#333333' : '#E0E0E0'}`, whiteSpace: 'nowrap', fontSize: '11px', letterSpacing: '0.1em' }"
          >{{ h }}</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(row, rowIdx) in rows" :key="`r-${rowIdx}`"
          class="data-row transition-colors duration-200"
          :style="{
            backgroundColor: highlightRow === rowIdx ? (darkMode ? '#1A1A1A' : '#000000')
              : (rowIdx % 2 === 0 ? (darkMode ? '#0A0A0A' : '#FFFFFF') : (darkMode ? '#111111' : '#F5F5F5')),
            height: compact ? 40 : 48,
            cursor: onRowHover ? 'pointer' : 'default',
          }"
          @mouseenter="onRowHover?.(rowIdx)"
          @mouseleave="onRowHover?.(null)"
        >
          <td
            v-for="(cell, colIdx) in row" :key="`c-${rowIdx}-${colIdx}`"
            class="px-4 text-sm whitespace-nowrap"
            :style="{
              color: getCellColor(cell, colIdx, rowIdx, row.length),
              fontWeight: colIdx === 0 ? 600 : 400,
              borderBottom: `1px solid ${darkMode ? '#333333' : '#E0E0E0'}`,
            }"
          >
            <slot :name="`cell-${colIdx}`" :cell="cell" :row="row" :rowIdx="rowIdx" :colIdx="colIdx">
              {{ typeof cell === 'number' ? cell.toLocaleString() : cell }}
            </slot>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

const props = defineProps<{
  headers: string[]
  rows: any[][]
  darkMode?: boolean
  highlightRow?: number | null
  onRowHover?: (idx: number | null) => void
  compact?: boolean
}>()

const tableRef = ref<HTMLDivElement>()

function getCellColor(cell: any, colIdx: number, rowIdx: number, rowLen: number): string {
  if (props.highlightRow === rowIdx) return '#FFFFFF'
  if (colIdx === rowLen - 1 && typeof cell === 'string' && cell.startsWith('+')) return props.darkMode ? '#FFFFFF' : '#000000'
  if (colIdx === rowLen - 1 && typeof cell === 'string' && cell.startsWith('-')) return '#666666'
  return props.darkMode ? '#FFFFFF' : '#000000'
}

onMounted(() => {
  if (!tableRef.value) return
  const rowEls = tableRef.value.querySelectorAll('.data-row')
  rowEls.forEach((row, i) => {
    gsap.fromTo(row, { opacity: 0, x: -10 }, {
      opacity: 1, x: 0, duration: 0.5, delay: i * 0.05, ease: 'power2.out',
      scrollTrigger: { trigger: tableRef.value, start: 'top 85%' }
    })
  })
})
</script>
