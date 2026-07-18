<script setup lang="ts">
import { h,onMounted,ref } from 'vue'
import { NButton,NDataTable,NModal,NSwitch,NSpace,NTag,useMessage,type DataTableColumns } from 'naive-ui'
import { listNodes,saveNode,type NodeAdmin } from '../../services/agentWorkflowAdmin'
const rows=ref<NodeAdmin[]>([]);const editing=ref<NodeAdmin|null>(null);const metadataText=ref('');const message=useMessage();async function load(){try{rows.value=await listNodes()}catch{message.error('無法載入 Node Catalog')}}function edit(row:NodeAdmin){editing.value=structuredClone(row);metadataText.value=JSON.stringify(row.metadata??{},null,2)}async function save(){if(!editing.value)return;try{const metadata=JSON.parse(metadataText.value||'{}');if(Array.isArray(metadata)||metadata===null||typeof metadata!=='object')throw new Error('Metadata 必須為 JSON 物件');editing.value.metadata=metadata;const x=await saveNode(editing.value);rows.value=rows.value.map(r=>r.nodeType===x.nodeType?x:r);editing.value=null;message.success('已儲存')}catch(error){message.error(error instanceof SyntaxError?'Metadata 必須是有效的 JSON':error instanceof Error?error.message:'儲存失敗')}}const columns:DataTableColumns<NodeAdmin>=[{title:'Node',key:'displayName'},{title:'Stage',key:'stage'},{title:'Contract',key:'contract',render:r=>`${r.contract.inputSchema} → ${r.contract.outputSchema}`},{title:'Timeout',key:'timeoutSeconds',render:r=>r.timeoutSeconds+'s'},{title:'Retries',key:'maxRetryCount'},{title:'狀態',key:'isEnabled',render:r=>h(NTag,{type:r.isEnabled?'success':'error'},()=>r.isEnabled?'啟用':'停用')},{title:'管理',key:'action',render:r=>h(NButton,{size:'small',onClick:()=>edit(r)},()=> '設定')}];onMounted(load)
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
      style="width: 520px; --n-color-modal: #101a2e; --n-text-color: #f5efe0; --n-title-text-color: #f5efe0; --n-title-font-weight: 600; --n-border-color: rgba(201,168,106,.25); --n-close-icon-color: #9a917c; --n-close-icon-color-hover: #c9a86a; --n-close-color-hover: rgba(201,168,106,.12); --gold: #c9a86a; --gold-strong: #ddc18a; --gold-border: rgba(201,168,106,.25); --gold-border-soft: rgba(201,168,106,.14); --ivory: #f5efe0; --muted: #9a917c; --up: #7fa387; --down: #b05c5c; --panel-bg: rgba(201,168,106,.04); --serif: Georgia, 'Noto Serif TC', serif; --sans: 'Inter', 'Noto Sans TC', sans-serif"
      @update:show="v=>!v&&(editing=null)"
    >
      <NSpace v-if="editing" vertical>
        <label class="nc-field">
          <span class="nc-field-label">名稱</span>
          <input v-model="editing.displayName" class="prestige-input" />
        </label>
        <label class="nc-field">
          <span class="nc-field-label">描述</span>
          <textarea v-model="editing.description" class="prestige-input" rows="3" />
        </label>
        <NSwitch v-model:value="editing.isEnabled">
          <template #checked>啟用</template>
          <template #unchecked>停用</template>
        </NSwitch>
        <div class="nc-number-grid">
          <label class="nc-field">
            <span class="nc-field-label">Timeout(秒)</span>
            <input v-model.number="editing.timeoutSeconds" type="number" min="1" max="3600" class="prestige-input" />
          </label>
          <label class="nc-field">
            <span class="nc-field-label">最大重試</span>
            <input v-model.number="editing.maxRetryCount" type="number" min="0" max="5" class="prestige-input" />
          </label>
        </div>
        <label class="nc-field">
          <span class="nc-field-label">Node Contract（唯讀）</span>
          <pre class="prestige-input nc-mono">{{ JSON.stringify(editing.contract, null, 2) }}</pre>
        </label>
        <label class="nc-field">
          <span class="nc-field-label">Admin 補充 Metadata(JSON，可選)</span>
          <textarea v-model="metadataText" class="prestige-input nc-mono" rows="8" placeholder='{"key":"value"}' />
        </label>
        <small class="nc-hint">Required: {{ editing.requiredBlackboardKeys.join(', ') }}</small>
        <small class="nc-hint">Produces: {{ editing.producedBlackboardKeys.join(', ') }}</small>
        <button class="prestige-btn prestige-btn-solid" @click="save">儲存</button>
      </NSpace>
    </NModal>
  </main>
</template>

<style scoped>
.nc-page {
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

.nc-field {
  display: grid;
  gap: 6px;
}

.nc-field-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--gold);
}

.nc-number-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.nc-mono {
  font-family: 'SFMono-Regular', Consolas, 'Courier New', monospace;
  font-size: 12px;
}

.nc-hint {
  color: var(--muted);
  font-size: 12px;
}
</style>
