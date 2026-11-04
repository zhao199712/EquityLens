<script setup lang="ts">
import { computed, ref } from 'vue'
import { currentWorkflowTypes, retiredWorkflowTypes } from '../../services/legacyWorkflows'
const props = defineProps<{ modelValue: string; observedTypes?: string[] }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const showHistory = ref(false)
const historyTypes = computed(() => [...new Set([...retiredWorkflowTypes, ...(props.observedTypes ?? [])])]
  .filter(type => !(currentWorkflowTypes as readonly string[]).includes(type)))
function toggleHistory(event: Event) {
  showHistory.value = (event.target as HTMLInputElement).checked
  if (!showHistory.value && historyTypes.value.includes(props.modelValue)) emit('update:modelValue', '')
}
</script>

<template>
  <div class="workflow-filter">
    <select aria-label="Workflow" class="prestige-input" :value="modelValue"
      @change="emit('update:modelValue', ($event.target as HTMLSelectElement).value)">
      <option value="">全部 Workflow（含歷史紀錄）</option>
      <optgroup label="現行 workflow">
        <option v-for="type in currentWorkflowTypes" :key="type" :value="type">{{ type }}</option>
      </optgroup>
      <optgroup v-if="showHistory" label="歷史 workflow（已退役，唯讀）">
        <option v-for="type in historyTypes" :key="type" :value="type">{{ type }}</option>
      </optgroup>
    </select>
    <label class="history-toggle">
      <input type="checkbox" :checked="showHistory" @change="toggleHistory" />
      顯示歷史 workflow（已退役，唯讀）
    </label>
  </div>
</template>

<style scoped>
.workflow-filter { display: flex; flex-direction: column; gap: 10px; }
select { width: 100%; min-width: 260px; }
.history-toggle { color: var(--muted); font-size: 12px; display: flex; align-items: center; gap: 8px; cursor: pointer; }
input { accent-color: var(--gold); }
@media (max-width: 720px) { .workflow-filter { width: 100%; } select { min-width: 0; } }
</style>
