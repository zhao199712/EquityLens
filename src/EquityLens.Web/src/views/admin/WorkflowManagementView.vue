<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import { NButton, NDataTable, NModal, NSwitch, NSpace, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { listWorkflows, saveWorkflow, type WorkflowAdmin } from '../../services/agentWorkflowAdmin'
const rows=ref<WorkflowAdmin[]>([]); const editing=ref<WorkflowAdmin|null>(null); const message=useMessage()
async function load(){ try { rows.value=await listWorkflows() } catch { message.error('無法載入 Workflow') } }
async function save(){ if(!editing.value)return; try { const x=await saveWorkflow(editing.value); rows.value=rows.value.map(r=>r.workflowType===x.workflowType?x:r); editing.value=null; message.success('已儲存') } catch { message.error('儲存失敗') } }
const columns:DataTableColumns<WorkflowAdmin>=[{title:'Workflow',key:'displayName'},{title:'Agent',key:'agentType'},{title:'流程',key:'nodeTypes',render:r=>r.nodeTypes.join(' → ')},{title:'狀態',key:'isEnabled',render:r=>h(NTag,{type:r.isEnabled?'success':'error'},()=>r.isEnabled?'啟用':'停用')},{title:'管理',key:'action',render:r=>h(NButton,{size:'small',onClick:()=>editing.value=structuredClone(r)},()=> '設定')}]
onMounted(load)
</script>

<template>
  <main class="prestige-page wf-page">
    <section class="prestige-section">
      <div class="prestige-section-head">
        <div>
          <p class="prestige-label">AGENT WORKFLOWS</p>
          <h1 class="prestige-section-title">Workflow 管理</h1>
        </div>
        <button class="prestige-btn" @click="load">重新整理</button>
      </div>

      <section class="prestige-panel table-panel">
        <NDataTable :data="rows" :columns="columns" :bordered="false" />
      </section>
    </section>

    <NModal
      :show="!!editing"
      preset="card"
      title="Workflow 設定"
      style="width: 480px; --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c; --up: #7fa387; --down: #b05c5c; --panel-bg: rgba(201,168,106,.04); --serif: Georgia, 'Noto Serif TC', serif; --sans: 'Inter', 'Noto Sans TC', sans-serif"
      @update:show="v=>!v&&(editing=null)"
    >
      <NSpace v-if="editing" vertical>
        <label class="wf-field">
          <span class="wf-field-label">名稱</span>
          <input v-model="editing.displayName" class="prestige-input" />
        </label>
        <label class="wf-field">
          <span class="wf-field-label">描述</span>
          <textarea v-model="editing.description" class="prestige-input" rows="3" />
        </label>
        <NSwitch v-model:value="editing.isEnabled">
          <template #checked>啟用</template>
          <template #unchecked>停用</template>
        </NSwitch>
        <div class="wf-flow prestige-mono">{{ editing.nodeTypes.join(' → ') }}</div>
        <button class="prestige-btn prestige-btn-solid" @click="save">儲存</button>
      </NSpace>
    </NModal>
  </main>
</template>

<style scoped>
.wf-page {
  min-height: calc(100vh - 60px);
}

.table-panel {
  padding: 8px 16px;
}

.table-panel :deep(.n-data-table) {
  background: transparent;
  color: var(--ivory);
  font-size: 13px;
}

.table-panel :deep(.n-data-table-thead) {
  background: transparent;
}

.table-panel :deep(.n-data-table-th) {
  background: transparent;
  color: var(--gold);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  border-bottom: 1px solid var(--gold-border);
}

.table-panel :deep(.n-data-table-td) {
  background: transparent;
  color: var(--ivory);
  border-bottom: 1px solid var(--gold-border-soft);
}

.table-panel :deep(.n-data-table-tr:hover .n-data-table-td) {
  background: rgba(201, 168, 106, 0.05);
}

.table-panel :deep(.n-data-table-empty) {
  color: var(--muted);
}

.table-panel :deep(.n-button) {
  color: var(--gold);
  background: transparent;
  border: 1px solid var(--gold-border);
  border-radius: 4px;
}

.table-panel :deep(.n-button:hover) {
  color: var(--gold-strong);
  border-color: var(--gold);
  background: rgba(201, 168, 106, 0.08);
}

.table-panel :deep(.n-tag) {
  background: transparent;
}

.wf-field {
  display: grid;
  gap: 6px;
}

.wf-field-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--gold);
}

.wf-flow {
  padding: 14px;
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  background: rgba(11, 18, 32, 0.6);
  color: var(--ivory);
  font-size: 12px;
  line-height: 1.7;
}
</style>
