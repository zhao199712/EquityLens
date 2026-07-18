<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import { AxiosError } from 'axios'
import { NButton, NDataTable, NModal, NSwitch, NSpace, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { listWorkflows, saveWorkflow, type WorkflowAdmin } from '../../services/agentWorkflowAdmin'

const rows = ref<WorkflowAdmin[]>([])
const editing = ref<WorkflowAdmin | null>(null)
const message = useMessage()

function flowSummary(workflow: WorkflowAdmin) {
  const first = workflow.nodeTypes[0] ?? '—'
  const last = workflow.nodeTypes.at(-1) ?? '—'
  return `${workflow.nodeTypes.length} nodes · ${workflow.edges.length} edges\n${first} → ${last}`
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
  { title: 'Flow', key: 'flow', render: row => h('span', { class: 'wf-flow-summary', title: flowSummary(row) }, [h('strong', `${row.nodeTypes.length} nodes · ${row.edges.length} edges`), h('span', `${row.nodeTypes[0] ?? '—'} → ${row.nodeTypes.at(-1) ?? '—'}`)]) },
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
          <dl class="wf-definition"><dt>Workflow type</dt><dd><code>{{ editing.workflowType }}</code></dd><dt>Agent</dt><dd>{{ editing.agentType }}</dd><dt>流程規模</dt><dd>{{ editing.nodeTypes.length }} nodes · {{ editing.edges.length }} edges</dd><dt>入口 Node</dt><dd><code>{{ editing.nodeTypes[0] ?? '—' }}</code></dd><dt>終點 Node</dt><dd><code>{{ editing.nodeTypes.at(-1) ?? '—' }}</code></dd></dl>
        </details>

        <details class="wf-details" open>
          <summary>執行步驟（唯讀）</summary>
          <ol class="wf-steps"><li v-for="nodeType in editing.nodeTypes" :key="nodeType"><code>{{ nodeType }}</code></li></ol>
        </details>

        <details class="wf-details">
          <summary>DAG Edges（唯讀）</summary>
          <ul class="wf-edges"><li v-for="edge in editing.edges" :key="`${edge.from}-${edge.to}`"><code>{{ edge.from }}</code> <span>→</span> <code>{{ edge.to }}</code></li></ul>
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
.wf-name code, .wf-flow-summary span { color: var(--muted); font-size: 11px; }
.wf-section { display: grid; gap: 12px; }
.wf-section-title, .wf-field-label { margin: 0; color: var(--gold); font-size: 11px; font-weight: 600; letter-spacing: .14em; text-transform: uppercase; }
.wf-field { display: grid; gap: 6px; }
.wf-details { border: 1px solid var(--gold-border-soft); border-radius: 4px; background: rgba(11, 18, 32, .35); }
.wf-details summary { cursor: pointer; padding: 12px 14px; color: var(--gold); font-size: 12px; font-weight: 600; letter-spacing: .08em; }
.wf-definition { display: grid; grid-template-columns: 130px minmax(0, 1fr); gap: 7px 12px; padding: 0 14px 14px; margin: 0; font-size: 12px; line-height: 1.5; }
dt { color: var(--muted); } dd { margin: 0; overflow-wrap: anywhere; }
.wf-steps, .wf-edges { display: grid; gap: 8px; margin: 0; padding: 0 14px 14px 36px; font-size: 12px; line-height: 1.5; }
.wf-steps li:not(:last-child)::after { content: '↓'; display: block; color: var(--gold); margin-top: 5px; }
.wf-edges { list-style: none; padding-left: 14px; }.wf-edges li { display: flex; gap: 8px; align-items: baseline; overflow-wrap: anywhere; }.wf-edges span { color: var(--gold); }
.wf-notice { padding: 12px 14px; border-left: 2px solid var(--gold); background: rgba(201, 168, 106, .07); color: var(--muted); font-size: 12px; line-height: 1.6; }
</style>
