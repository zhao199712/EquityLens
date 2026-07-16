<template>
  <div class="correlation-matrix">
    <div v-if="series.length < 2" class="matrix-empty">至少需要兩檔標的才能計算相關性</div>
    <div v-else class="matrix-table">
      <div class="matrix-row header">
        <div class="matrix-cell corner" />
        <div v-for="s in series" :key="`h-${s.securityId}`" class="matrix-cell header-cell">
          {{ s.ticker }}
        </div>
      </div>
      <div v-for="(row, i) in series" :key="`r-${row.securityId}`" class="matrix-row">
        <div class="matrix-cell header-cell">{{ row.ticker }}</div>
        <div
          v-for="(col, j) in series"
          :key="`c-${col.securityId}`"
          class="matrix-cell value-cell"
          :style="cellStyle(correlation(i, j))"
        >
          <span v-if="i === j" class="diagonal">—</span>
          <span v-else>{{ correlation(i, j).toFixed(2) }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Series {
  securityId: string
  ticker: string
  values: number[]
}

const props = defineProps<{
  series: Series[]
}>()

function returns(values: number[]): number[] {
  return values.slice(1).map((v, i) => v - values[i])
}

function correlation(i: number, j: number): number {
  if (i === j) return 1
  const a = returns(props.series[i].values)
  const b = returns(props.series[j].values)
  const n = Math.min(a.length, b.length)
  const aa = a.slice(0, n)
  const bb = b.slice(0, n)
  const meanA = aa.reduce((s, v) => s + v, 0) / n
  const meanB = bb.reduce((s, v) => s + v, 0) / n
  const num = aa.reduce((s, v, idx) => s + (v - meanA) * (bb[idx] - meanB), 0)
  const denA = Math.sqrt(aa.reduce((s, v) => s + Math.pow(v - meanA, 2), 0))
  const denB = Math.sqrt(bb.reduce((s, v) => s + Math.pow(v - meanB, 2), 0))
  if (denA === 0 || denB === 0) return 0
  return num / (denA * denB)
}

const cellStyle = computed(() => (value: number) => {
  const intensity = Math.abs(value)
  const alpha = Math.max(0.08, intensity * 0.35)
  const base = value >= 0 ? '96, 165, 250' : '248, 113, 113'
  return {
    backgroundColor: `rgba(${base}, ${alpha})`,
    color: intensity > 0.6 ? '#ffffff' : 'var(--text-primary)',
  }
})
</script>

<style scoped>
.correlation-matrix {
  width: 100%;
  overflow-x: auto;
}

.matrix-empty {
  display: grid;
  place-items: center;
  min-height: 120px;
  color: var(--text-muted);
  font-size: 13px;
  border: 1px dashed var(--border-medium);
}

.matrix-table {
  display: inline-flex;
  flex-direction: column;
  min-width: 100%;
}

.matrix-row {
  display: flex;
}

.matrix-row.header {
  border-bottom: 1px solid var(--border-medium);
}

.matrix-cell {
  flex: 1;
  min-width: 80px;
  padding: 14px 8px;
  text-align: center;
  font-size: 13px;
  font-weight: 600;
  border-right: 1px solid var(--border-subtle);
  border-bottom: 1px solid var(--border-subtle);
}

.matrix-cell:last-child {
  border-right: none;
}

.matrix-cell.corner {
  background: transparent;
  border-right: 1px solid var(--border-medium);
}

.header-cell {
  font-family: var(--kimi-font-mono);
  font-size: 12px;
  color: var(--text-secondary);
  background: var(--bg-tertiary);
}

.value-cell {
  transition: background-color 0.3s ease;
}

.diagonal {
  color: var(--text-muted);
}
</style>
