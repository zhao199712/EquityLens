<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import { NButton, NDataTable, NModal, NInput, NSwitch, NSpace, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { listWorkflows, saveWorkflow, type WorkflowAdmin } from '../../services/agentWorkflowAdmin'
const rows=ref<WorkflowAdmin[]>([]); const editing=ref<WorkflowAdmin|null>(null); const message=useMessage()
async function load(){ try { rows.value=await listWorkflows() } catch { message.error('無法載入 Workflow') } }
async function save(){ if(!editing.value)return; try { const x=await saveWorkflow(editing.value); rows.value=rows.value.map(r=>r.workflowType===x.workflowType?x:r); editing.value=null; message.success('已儲存') } catch { message.error('儲存失敗') } }
const columns:DataTableColumns<WorkflowAdmin>=[{title:'Workflow',key:'displayName'},{title:'Agent',key:'agentType'},{title:'流程',key:'nodeTypes',render:r=>r.nodeTypes.join(' → ')},{title:'狀態',key:'isEnabled',render:r=>h(NTag,{type:r.isEnabled?'success':'error'},()=>r.isEnabled?'啟用':'停用')},{title:'管理',key:'action',render:r=>h(NButton,{size:'small',onClick:()=>editing.value=structuredClone(r)},()=> '設定')}]
onMounted(load)
</script>
<template><main class="tech-page tech-page-fill"><section class="tech-page-header"><div><p class="tech-eyebrow">AGENT WORKFLOWS</p><h1>Workflow 管理</h1></div><NButton class="tech-btn" @click="load">重新整理</NButton></section><section class="tech-panel"><NDataTable :data="rows" :columns="columns" :bordered="false"/></section><NModal :show="!!editing" preset="card" title="Workflow 設定" @update:show="v=>!v&&(editing=null)"><NSpace v-if="editing" vertical><NInput v-model:value="editing.displayName"/><NInput v-model:value="editing.description" type="textarea"/><NSwitch v-model:value="editing.isEnabled"><template #checked>啟用</template><template #unchecked>停用</template></NSwitch><div class="flow">{{editing.nodeTypes.join(' → ')}}</div><NButton type="primary" @click="save">儲存</NButton></NSpace></NModal></main></template>
<style scoped>.tech-page{padding:32px}.tech-page-header{display:flex;justify-content:space-between;align-items:center;margin-bottom:24px}.flow{padding:14px;border:1px solid var(--tech-border,#1e3a5f);border-radius:8px}</style>
