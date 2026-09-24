<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { AxiosError } from 'axios'
import { createDraft, createPromptTemplate, getPromptContent, listPromptBindings, listPromptTemplates, publishPrompt, type PromptBinding, type PromptTemplate, type PromptVersion } from '../../services/promptManagement'

const templates = ref<PromptTemplate[]>([])
const bindings = ref<PromptBinding[]>([])
const selected = ref<PromptTemplate | null>(null)
const content = ref('')
const loading = ref(false)
const error = ref('')
const creating = ref(false)
const form = ref({ key: '', name: '', systemPrompt: '', requiredVariablesJson: '[]' })
const selectedBindings = computed(() => selected.value ? bindings.value.filter(x => x.promptTemplateId === selected.value?.id).map(x => x.usageKey) : [])

function message(value: unknown) { return value instanceof AxiosError ? (value.response?.data as { message?: string })?.message || '請求失敗，請重新載入。' : '請求失敗，請重新載入。' }
async function load() { loading.value = true; error.value = ''; try { [templates.value, bindings.value] = await Promise.all([listPromptTemplates(), listPromptBindings()]); if (selected.value) selected.value = templates.value.find(x => x.id === selected.value?.id) ?? null } catch (e) { error.value = message(e) } finally { loading.value = false } }
async function choose(template: PromptTemplate) { selected.value = template; const version = template.versions[0]; content.value = version ? (await getPromptContent(version.id)).systemPrompt : '' }
async function addTemplate() { try { await createPromptTemplate(form.value); creating.value = false; form.value = { key: '', name: '', systemPrompt: '', requiredVariablesJson: '[]' }; await load() } catch (e) { error.value = message(e) } }
async function newDraft() { if (!selected.value) return; try { await createDraft(selected.value.id, { systemPrompt: content.value, requiredVariablesJson: '[]', changeSummary: 'Admin draft' }); await load() } catch (e) { error.value = message(e) } }
async function publish(version: PromptVersion) { try { await publishPrompt(version.id, version.concurrencyToken, selectedBindings.value); await load() } catch (e) { error.value = message(e) } }
onMounted(load)
</script>

<template>
  <main class="prestige-page prompt-page">
    <section class="prestige-section">
      <div class="prestige-section-head"><div><p class="prestige-label">PROMPT MANAGEMENT</p><h1 class="prestige-section-title">Agent Prompt 管理</h1><p>版本、綁定與執行期快照皆可追溯；發佈只會切換明確選定的 usage。</p></div><button class="prestige-btn" :disabled="loading" @click="load">重新整理</button></div>
      <p v-if="error" class="prestige-panel error" role="alert">{{ error }}</p>
      <div class="layout">
        <section class="prestige-panel"><div class="list-head"><h2>Templates</h2><button class="prestige-btn" @click="creating = !creating">新增</button></div>
          <form v-if="creating" class="form" @submit.prevent="addTemplate"><input v-model="form.key" class="prestige-input" placeholder="stable-key" required><input v-model="form.name" class="prestige-input" placeholder="名稱" required><textarea v-model="form.systemPrompt" class="prestige-input" placeholder="System prompt" required></textarea><input v-model="form.requiredVariablesJson" class="prestige-input" aria-label="Variables JSON"><button class="prestige-btn">建立草稿</button></form>
          <button v-for="item in templates" :key="item.id" class="template" :class="{ active: selected?.id === item.id }" @click="choose(item)"><strong>{{ item.name }}</strong><small>{{ item.key }} · {{ item.status }}</small></button>
        </section>
        <section class="prestige-panel detail"><template v-if="selected"><div class="list-head"><div><p class="prestige-label">{{ selected.key }}</p><h2>{{ selected.name }}</h2></div><button class="prestige-btn" @click="newDraft">由目前內容建草稿</button></div><textarea v-model="content" class="prestige-input prompt" aria-label="Prompt content" readonly></textarea><h3>Versions</h3><div v-for="version in selected.versions" :key="version.id" class="version"><span>v{{ version.versionNumber }} · {{ version.status }}</span><code>{{ version.contentHash.slice(0, 12) }}</code><button v-if="version.status === 'Draft' || version.status === 'Retired'" class="prestige-btn" @click="publish(version)">發布並切換綁定</button></div><h3>Active bindings</h3><p v-for="key in selectedBindings" :key="key" class="binding">{{ key }}</p></template><p v-else>選擇一個 template 以檢視版本與綁定。</p></section>
      </div>
    </section>
  </main>
</template>

<style scoped>
.layout { display:grid; grid-template-columns:minmax(260px,.8fr) minmax(0,2fr); gap:18px }.list-head { display:flex; justify-content:space-between; align-items:center; gap:12px }.template { display:flex; width:100%; padding:12px 0; text-align:left; color:inherit; background:none; border:0; border-bottom:1px solid var(--gold-border-soft); flex-direction:column; gap:4px; cursor:pointer }.template.active { color:var(--gold-primary) }.form { display:grid; gap:8px; margin-bottom:12px }.form textarea { min-height:100px }.detail .prompt { width:100%; min-height:240px; margin:12px 0; font-family:monospace }.version { display:flex; align-items:center; gap:12px; padding:9px 0; border-bottom:1px solid var(--gold-border-soft) }.version code { margin-left:auto }.binding { font-family:monospace; font-size:.85rem }.error { color:#ffb4ab; padding:12px } @media (max-width:800px){.layout{grid-template-columns:1fr}}
</style>
