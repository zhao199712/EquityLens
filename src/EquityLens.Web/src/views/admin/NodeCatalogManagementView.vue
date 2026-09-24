<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import { AxiosError } from 'axios'
import { NButton, NDataTable, NModal, NSwitch, NSpace, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { listNodes, saveNode, type NodeAdmin } from '../../services/agentWorkflowAdmin'

const rows = ref<NodeAdmin[]>([])
const editing = ref<NodeAdmin | null>(null)
const metadataText = ref('')
const message = useMessage()

function keySummary(keys: string[]) {
  if (!keys.length) return '—'
  return keys.length <= 2 ? keys.join(', ') : `${keys.slice(0, 2).join(', ')} +${keys.length - 2}`
}

function saveError(error: unknown) {
  if (error instanceof SyntaxError) return 'Metadata 必須是有效的 JSON'
  if (error instanceof AxiosError) {
    if (error.response?.status === 401) return '登入已失效，請重新登入'
    if (error.response?.status === 403) return '此帳號沒有管理 Node 的權限'
    if (error.response?.status === 404) return '目前 API 尚未提供此 Node 設定端點'
    if (error.response?.status === 400) return '設定值不合法：Timeout 為 1–3600 秒，重試次數為 0–5'
    return `儲存失敗（HTTP ${error.response?.status ?? '連線錯誤'}）`
  }
  return error instanceof Error ? error.message : '儲存失敗'
}

async function load() {
  try {
    rows.value = await listNodes()
  } catch {
    message.error('無法載入 Node Catalog')
  }
}

function edit(row: NodeAdmin) {
  editing.value = JSON.parse(JSON.stringify(row)) as NodeAdmin
  metadataText.value = JSON.stringify(row.metadata ?? {}, null, 2)
}

async function save() {
  if (!editing.value) return

  try {
    const metadata = JSON.parse(metadataText.value || '{}')
    if (Array.isArray(metadata) || metadata === null || typeof metadata !== 'object') {
      throw new Error('Metadata 必須為 JSON 物件')
    }

    const updated = await saveNode(editing.value.nodeType, {
      isEnabled: editing.value.isEnabled,
      displayName: editing.value.displayName,
      description: editing.value.description,
      timeoutSeconds: editing.value.timeoutSeconds,
      maxRetryCount: editing.value.maxRetryCount,
      requiresHumanApprovalOverride: editing.value.requiresHumanApprovalOverride,
      metadata,
    })
    rows.value = rows.value.map((row) => row.nodeType === updated.nodeType ? updated : row)
    editing.value = null
    message.success('Node 設定已儲存')
  } catch (error) {
    message.error(saveError(error))
  }
}

const columns: DataTableColumns<NodeAdmin> = [
  {
    title: 'Node',
    key: 'displayName',
    render: (row) => h('div', { class: 'nc-node' }, [
      h('strong', row.displayName),
      h('code', row.nodeType),
    ]),
  },
  { title: 'Stage', key: 'stage' },
  {
    title: 'Input / Output',
    key: 'contract',
    render: (row) => h('span', { title: `${row.contract.inputSchema} → ${row.contract.outputSchema}` }, `${row.contract.inputSchema} → ${row.contract.outputSchema}`),
  },
  {
    title: 'Blackboard',
    key: 'blackboard',
    render: (row) => h('div', { class: 'nc-blackboard', title: `Required: ${row.requiredBlackboardKeys.join(', ') || '—'}\nProduces: ${row.producedBlackboardKeys.join(', ') || '—'}` }, [
      h('span', `In: ${keySummary(row.requiredBlackboardKeys)}`),
      h('span', `Out: ${keySummary(row.producedBlackboardKeys)}`),
    ]),
  },
  { title: 'Side effect', key: 'sideEffectLevel' },
  { title: 'Approval', key: 'requiresHumanApproval', render: (row) => h(NTag, { type: row.requiresHumanApproval ? 'warning' : 'default' }, () => row.requiresHumanApproval ? `需要 · ${row.approvalPolicySource}` : `不需要 · ${row.approvalPolicySource}`) },
  { title: 'Policy', key: 'policy', render: (row) => `${row.timeoutSeconds}s · ${row.maxRetryCount} retries` },
  { title: '狀態', key: 'isEnabled', render: (row) => h(NTag, { type: row.isEnabled ? 'success' : 'error' }, () => row.isEnabled ? '啟用' : '停用') },
  { title: '管理', key: 'action', render: (row) => h(NButton, { size: 'small', onClick: () => edit(row) }, () => '設定') },
]

onMounted(load)
</script>

<template>
  <main class="prestige-page nc-page">
    <section class="prestige-section">
      <div class="prestige-section-head">
        <div>
          <p class="prestige-label">NODE CATALOG</p>
          <h1 class="prestige-section-title">Node 管理</h1>
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
      title="Node 設定"
      style="width: min(760px, calc(100vw - 32px)); --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c; --up: #7fa387; --down: #b05c5c; --panel-bg: rgba(201,168,106,.04); --serif: Georgia, 'Noto Serif TC', serif; --sans: 'Inter', 'Noto Sans TC', sans-serif"
      @update:show="value => !value && (editing = null)"
    >
      <NSpace v-if="editing" vertical :size="16">
        <section class="nc-section">
          <p class="nc-section-title">有效設定</p>
          <div class="nc-form-grid">
            <label class="nc-field nc-field-wide">
              <span class="nc-field-label">名稱</span>
              <input v-model="editing.displayName" class="prestige-input" />
            </label>
            <label class="nc-field nc-field-wide">
              <span class="nc-field-label">描述</span>
              <textarea v-model="editing.description" class="prestige-input" rows="3" />
            </label>
            <label class="nc-field">
              <span class="nc-field-label">Timeout（秒）</span>
              <input v-model.number="editing.timeoutSeconds" type="number" min="1" max="3600" class="prestige-input" />
            </label>
            <label class="nc-field">
              <span class="nc-field-label">最大重試</span>
              <input v-model.number="editing.maxRetryCount" type="number" min="0" max="5" class="prestige-input" />
            </label>
            <label class="nc-field nc-field-wide">
              <span class="nc-field-label">Human Approval Gate</span>
              <select v-model="editing.requiresHumanApprovalOverride" class="prestige-input">
                <option :value="null">繼承 Catalog（{{ editing.contract.requiresHumanInput ? '需要批准' : '不需批准' }}）</option>
                <option :value="true">強制需要批准</option>
                <option :value="false">明確不需批准</option>
              </select>
              <span class="nc-hint">有效值：{{ editing.requiresHumanApproval ? '需要批准' : '不需批准' }}；儲存後套用於新建立的 run。</span>
            </label>
          </div>
          <NSwitch v-model:value="editing.isEnabled">
            <template #checked>啟用</template>
            <template #unchecked>停用</template>
          </NSwitch>
        </section>

        <details class="nc-details" open>
          <summary>Node Contract（唯讀）</summary>
          <div class="nc-contract-grid">
            <section>
              <span class="nc-field-label">Identity</span>
              <dl><dt>Node type</dt><dd><code>{{ editing.contract.nodeType }}</code></dd><dt>Version</dt><dd>{{ editing.contract.version }}</dd><dt>Stage</dt><dd>{{ editing.contract.stage }}</dd><dt>Side effect</dt><dd>{{ editing.contract.sideEffectLevel }}</dd></dl>
            </section>
            <section>
              <span class="nc-field-label">Input / Output</span>
              <dl><dt>Input</dt><dd><code>{{ editing.contract.inputSchema }}</code></dd><dt>Output</dt><dd><code>{{ editing.contract.outputSchema }}</code></dd><dt>Default policy</dt><dd>{{ editing.contract.defaultPolicy.timeoutSeconds }}s · {{ editing.contract.defaultPolicy.maxRetryCount }} retries</dd></dl>
            </section>
            <section class="nc-contract-wide">
              <span class="nc-field-label">Blackboard keys</span>
              <dl><dt>Required</dt><dd>{{ editing.contract.requiredBlackboardKeys.join(', ') || '—' }}</dd><dt>Optional</dt><dd>{{ editing.contract.optionalBlackboardKeys.join(', ') || '—' }}</dd><dt>Produces</dt><dd>{{ editing.contract.producedBlackboardKeys.join(', ') || '—' }}</dd></dl>
            </section>
            <section>
              <span class="nc-field-label">Flow</span>
              <dl><dt>Allowed previous</dt><dd>{{ editing.contract.allowedPreviousNodeTypes.join(', ') || '—' }}</dd><dt>Allowed next</dt><dd>{{ editing.contract.allowedNextNodeTypes.join(', ') || '—' }}</dd></dl>
            </section>
            <section>
              <span class="nc-field-label">Execution flags</span>
              <dl><dt>Idempotent</dt><dd>{{ editing.contract.isIdempotent ? 'Yes' : 'No' }}</dd><dt>Supports loop</dt><dd>{{ editing.contract.supportsLoop ? 'Yes' : 'No' }}</dd><dt>Human input</dt><dd>{{ editing.contract.requiresHumanInput ? 'Required' : 'Not required' }}</dd></dl>
            </section>
          </div>
        </details>

        <details class="nc-details">
          <summary>原始 Contract JSON</summary>
          <pre class="prestige-input nc-mono">{{ JSON.stringify(editing.contract, null, 2) }}</pre>
        </details>

        <details class="nc-details">
          <summary>進階 Metadata（JSON，可選）</summary>
          <label class="nc-field nc-details-body">
            <span class="nc-hint">僅供 Admin 補充，不會改變 Contract 或 workflow 執行行為。</span>
            <textarea v-model="metadataText" class="prestige-input nc-mono" rows="8" placeholder='{"key":"value"}' />
          </label>
        </details>

        <button class="prestige-btn prestige-btn-solid" @click="save">儲存有效設定</button>
      </NSpace>
    </NModal>
  </main>
</template>

<style scoped>
.nc-page { min-height: calc(100vh - 60px); }
.table-panel { padding: 8px 16px; overflow-x: auto; }
.table-panel :deep(.n-data-table) { background: transparent; color: var(--ivory); font-size: 13px; min-width: 1060px; }
.table-panel :deep(.n-data-table-thead), .table-panel :deep(.n-data-table-th) { background: transparent; }
.table-panel :deep(.n-data-table-th) { color: var(--gold); font-size: 11px; font-weight: 600; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid var(--gold-border); }
.table-panel :deep(.n-data-table-td) { background: transparent; color: var(--ivory); border-bottom: 1px solid var(--gold-border-soft); vertical-align: top; }
.table-panel :deep(.n-data-table-tr:hover .n-data-table-td) { background: rgba(201, 168, 106, .05); }
.table-panel :deep(.n-data-table-empty) { color: var(--muted); }
.table-panel :deep(.n-button) { color: var(--gold); background: transparent; border: 1px solid var(--gold-border); border-radius: 4px; }
.table-panel :deep(.n-button:hover) { color: var(--gold-strong); border-color: var(--gold); background: rgba(201, 168, 106, .08); }
.table-panel :deep(.n-tag) { background: transparent; }
.nc-node, .nc-blackboard { display: grid; gap: 3px; }
.nc-node code, .nc-blackboard { font-size: 11px; color: var(--muted); }
.nc-section { display: grid; gap: 12px; }
.nc-section-title { margin: 0; color: var(--gold); font-size: 11px; font-weight: 600; letter-spacing: .14em; text-transform: uppercase; }
.nc-form-grid, .nc-contract-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.nc-field { display: grid; gap: 6px; }
.nc-field-wide, .nc-contract-wide { grid-column: 1 / -1; }
.nc-field-label { color: var(--gold); font-size: 11px; font-weight: 600; letter-spacing: .14em; text-transform: uppercase; }
.nc-details { border: 1px solid var(--gold-border-soft); border-radius: 4px; background: rgba(11, 18, 32, .35); }
.nc-details summary { cursor: pointer; padding: 12px 14px; color: var(--gold); font-size: 12px; font-weight: 600; letter-spacing: .08em; }
.nc-contract-grid, .nc-details-body { padding: 0 14px 14px; }
.nc-contract-grid section { min-width: 0; }
dl { display: grid; grid-template-columns: 120px minmax(0, 1fr); gap: 6px 12px; margin: 8px 0 0; font-size: 12px; line-height: 1.5; }
dt { color: var(--muted); }
dd { margin: 0; overflow-wrap: anywhere; }
code, .nc-mono { font-family: 'SFMono-Regular', Consolas, 'Courier New', monospace; }
.nc-mono { font-size: 12px; overflow: auto; white-space: pre-wrap; }
.nc-hint { color: var(--muted); font-size: 12px; }
@media (max-width: 640px) { .nc-form-grid, .nc-contract-grid { grid-template-columns: 1fr; } .nc-field-wide, .nc-contract-wide { grid-column: auto; } dl { grid-template-columns: 1fr; gap: 2px; } }
</style>
