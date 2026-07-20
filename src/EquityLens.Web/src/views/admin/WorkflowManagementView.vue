<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import { AxiosError } from 'axios'
import { NButton, NDataTable, NModal, NSwitch, NSpace, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { listWorkflows, saveWorkflow, type WorkflowAdmin } from '../../services/agentWorkflowAdmin'

const rows = ref<WorkflowAdmin[]>([])
const editing = ref<WorkflowAdmin | null>(null)
const message = useMessage()

function flowSummary(workflow: WorkflowAdmin) {
  return `${workflow.initialNodes.length} nodes · ${workflow.initialEdges.length} edges\n${initialEndpoints(workflow)}`
}

function initialEndpoints(workflow: WorkflowAdmin) {
  const incoming = new Set(workflow.initialEdges.map(edge => edge.to))
  const outgoing = new Set(workflow.initialEdges.map(edge => edge.from))
  const entries = workflow.initialNodes.filter(node => !incoming.has(node.nodeKey)).map(node => node.nodeType)
  const terminals = workflow.initialNodes.filter(node => !outgoing.has(node.nodeKey)).map(node => node.nodeType)
  return `${entries.join(', ') || '—'} → ${terminals.join(', ') || '—'}`
}

function modeLabel(mode: string) {
  if (mode === 'DynamicStateful') return '動態 Stateful'
  if (mode === 'Stateful') return 'Stateful'
  return '固定 DAG'
}

function saveError(error: unknown) {
  if (error instanceof AxiosError) {
    if (error.response?.status === 401) return '登入已失效，請重新登入'
    if (error.response?.status === 403) return '此帳號沒有管理 Workflow 的權限'
    if (error.response?.status === 404) return '目前 API 尚未提供此 Workflow 設定端點'
    if (error.response?.status === 400) return '設定值不合法，請檢查名稱、描述與啟用狀態'
    return `儲存失敗（HTTP ${error.response?.status ?? '連線錯誤'}）`
  }
  return error instanceof Error ? error.message : '儲存失敗'
}

async function load() {
  try {
    rows.value = await listWorkflows()
  } catch {
    message.error('無法載入 Workflow')
  }
}

function edit(row: WorkflowAdmin) {
  editing.value = JSON.parse(JSON.stringify(row)) as WorkflowAdmin
}

async function save() {
  if (!editing.value) return
  try {
    const updated = await saveWorkflow(editing.value.workflowType, {
      isEnabled: editing.value.isEnabled,
      displayName: editing.value.displayName,
      description: editing.value.description,
    })
    rows.value = rows.value.map(row => row.workflowType === updated.workflowType ? updated : row)
    editing.value = null
    message.success('Workflow 設定已儲存')
  } catch (error) {
    message.error(saveError(error))
  }
}

const columns: DataTableColumns<WorkflowAdmin> = [
  { title: 'Workflow', key: 'displayName', render: row => h('div', { class: 'wf-name' }, [h('strong', row.displayName), h('code', row.workflowType)]) },
  { title: 'Agent', key: 'agentType' },
  { title: 'Flow', key: 'flow', render: row => h('span', { class: 'wf-flow-summary', title: flowSummary(row) }, [h('span', { class: 'wf-flow-heading' }, [h('strong', `初始 ${row.initialNodes.length} nodes · ${row.initialEdges.length} edges`), row.orchestrationMode === 'DynamicStateful' ? h(NTag, { size: 'small', type: 'info' }, () => '動態') : null]), h('span', initialEndpoints(row))]) },
  { title: '狀態', key: 'isEnabled', render: row => h(NTag, { type: row.isEnabled ? 'success' : 'error' }, () => row.isEnabled ? '啟用' : '停用') },
  { title: '管理', key: 'action', render: row => h(NButton, { size: 'small', onClick: () => edit(row) }, () => '設定') },
]

onMounted(load)
</script>

<template>
  <main class="prestige-page wf-page">
    <section class="prestige-section">
      <div class="prestige-section-head">
        <div><p class="prestige-label">AGENT WORKFLOWS</p><h1 class="prestige-section-title">Workflow 管理</h1></div>
        <button class="prestige-btn" @click="load">重新整理</button>
      </div>

      <section class="prestige-panel table-panel"><NDataTable :data="rows" :columns="columns" :bordered="false" /></section>
    </section>

    <NModal :show="!!editing" preset="card" title="Workflow 設定" style="width: min(760px, calc(100vw - 32px)); --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c" @update:show="value => !value && (editing = null)">
      <NSpace v-if="editing" vertical :size="16">
        <section class="wf-section">
          <p class="wf-section-title">有效設定</p>
          <label class="wf-field"><span class="wf-field-label">名稱</span><input v-model="editing.displayName" class="prestige-input" /></label>
          <label class="wf-field"><span class="wf-field-label">描述</span><textarea v-model="editing.description" class="prestige-input" rows="3" /></label>
          <NSwitch v-model:value="editing.isEnabled"><template #checked>啟用</template><template #unchecked>停用</template></NSwitch>
        </section>

        <details class="wf-details" open>
          <summary>流程定義（唯讀）</summary>
          <dl class="wf-definition"><dt>Workflow type</dt><dd><code>{{ editing.workflowType }}</code></dd><dt>Agent</dt><dd>{{ editing.agentType }}</dd><dt>編排模式</dt><dd>{{ modeLabel(editing.orchestrationMode) }}</dd><dt>初始流程規模</dt><dd>{{ editing.initialNodes.length }} nodes · {{ editing.initialEdges.length }} edges</dd><dt>初始入口／終點</dt><dd><code>{{ initialEndpoints(editing) }}</code></dd></dl>
        </details>

        <details class="wf-details" open>
          <summary>初始執行步驟（唯讀）</summary>
          <ol class="wf-steps"><li v-for="node in editing.initialNodes" :key="node.nodeKey"><code>{{ node.nodeType }}</code><small v-if="node.nodeKey !== node.nodeType">{{ node.nodeKey }}</small></li></ol>
        </details>

        <details class="wf-details">
          <summary>初始 DAG Edges（唯讀）</summary>
          <ul class="wf-edges"><li v-for="edge in editing.initialEdges" :key="`${edge.from}-${edge.to}`"><code>{{ edge.from }}</code> <span>→</span> <code>{{ edge.to }}</code></li></ul>
        </details>

        <details v-if="editing.orchestrationMode === 'DynamicStateful'" class="wf-details" open>
          <summary>可動態加入的 Node Types</summary>
          <ul class="wf-dynamic-nodes"><li v-for="nodeType in editing.dynamicNodeTypes" :key="nodeType"><code>{{ nodeType }}</code></li></ul>
          <p class="wf-detail-note">實際執行圖由 Planner 依執行狀態建立，請以個別 Agent Run 的持久化 Workflow Definition 為準。</p>
        </details>

        <aside class="wf-notice">停用 Workflow 會拒絕建立新的 Agent Run。Node 的 timeout、retry、side effect 與 contract 請至 Node 管理查看與設定。</aside>
        <button class="prestige-btn prestige-btn-solid" @click="save">儲存有效設定</button>
      </NSpace>
    </NModal>
  </main>
</template>

<style scoped>
.wf-page { min-height: calc(100vh - 60px); }
.table-panel { padding: 8px 16px; overflow-x: auto; }
.table-panel :deep(.n-data-table) { background: transparent; color: var(--ivory); font-size: 13px; min-width: 760px; }
.table-panel :deep(.n-data-table-thead), .table-panel :deep(.n-data-table-th) { background: transparent; }
.table-panel :deep(.n-data-table-th) { color: var(--gold); font-size: 11px; font-weight: 600; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid var(--gold-border); }
.table-panel :deep(.n-data-table-td) { background: transparent; color: var(--ivory); border-bottom: 1px solid var(--gold-border-soft); vertical-align: top; }
.table-panel :deep(.n-data-table-tr:hover .n-data-table-td) { background: rgba(201, 168, 106, .05); }
.table-panel :deep(.n-button) { color: var(--gold); background: transparent; border: 1px solid var(--gold-border); border-radius: 4px; }
.table-panel :deep(.n-tag) { background: transparent; }
.wf-name, .wf-flow-summary { display: grid; gap: 3px; }
.wf-flow-heading { display: flex; align-items: center; gap: 8px; }
.wf-name code, .wf-flow-summary span { color: var(--muted); font-size: 11px; }
.wf-section { display: grid; gap: 12px; }
.wf-section-title, .wf-field-label { margin: 0; color: var(--gold); font-size: 11px; font-weight: 600; letter-spacing: .14em; text-transform: uppercase; }
.wf-field { display: grid; gap: 6px; }
.wf-details { border: 1px solid var(--gold-border-soft); border-radius: 4px; background: rgba(11, 18, 32, .35); }
.wf-details summary { cursor: pointer; padding: 12px 14px; color: var(--gold); font-size: 12px; font-weight: 600; letter-spacing: .08em; }
.wf-definition { display: grid; grid-template-columns: 130px minmax(0, 1fr); gap: 7px 12px; padding: 0 14px 14px; margin: 0; font-size: 12px; line-height: 1.5; }
dt { color: var(--muted); } dd { margin: 0; overflow-wrap: anywhere; }
.wf-steps, .wf-edges, .wf-dynamic-nodes { display: grid; gap: 8px; margin: 0; padding: 0 14px 14px 36px; font-size: 12px; line-height: 1.5; }
.wf-steps small { display: block; color: var(--muted); }
.wf-steps li:not(:last-child)::after { content: '↓'; display: block; color: var(--gold); margin-top: 5px; }
.wf-edges { list-style: none; padding-left: 14px; }.wf-edges li { display: flex; gap: 8px; align-items: baseline; overflow-wrap: anywhere; }.wf-edges span { color: var(--gold); }
.wf-dynamic-nodes { grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); list-style: none; padding-left: 14px; }
.wf-detail-note { margin: 0; padding: 0 14px 14px; color: var(--muted); font-size: 12px; line-height: 1.6; }
.wf-notice { padding: 12px 14px; border-left: 2px solid var(--gold); background: rgba(201, 168, 106, .07); color: var(--muted); font-size: 12px; line-height: 1.6; }
</style>
