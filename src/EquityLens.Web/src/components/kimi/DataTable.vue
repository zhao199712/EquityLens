<template>
  <div class="kimi-scroll" style="overflow-x: auto">
    <table :class="['kimi-table', dark ? 'kimi-table-dark' : 'kimi-table-light']">
      <thead>
        <tr>
          <th v-for="(h, i) in headers" :key="i">{{ h }}</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(row, rowIdx) in rows"
          :key="rowIdx"
          :class="{ highlight: highlightRow === rowIdx }"
          @mouseenter="$emit('rowHover', rowIdx)"
          @mouseleave="$emit('rowHover', null)"
        >
          <td
            v-for="(cell, colIdx) in row"
            :key="colIdx"
            :style="{ fontWeight: colIdx === 0 ? 600 : 400 }"
          >
            {{ cell }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  headers: string[]
  rows: (string | number)[][]
  dark?: boolean
  highlightRow?: number | null
}>()

defineEmits<{
  rowHover: [index: number | null]
}>()
</script>
