<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { AxiosError } from 'axios'
import { getAgentRegistry, type NodeCapability, type WorkflowSkill } from '../../services/agentRegistryAdmin'

const skills = ref<WorkflowSkill[]>([])
const capabilities = ref<NodeCapability[]>([])
const loading = ref(false)
const error = ref('')
const query = ref('')
const sideEffect = ref('')
const loopFilter = ref<'all' | 'yes' | 'no'>('all')
const selectedSkill = ref<WorkflowSkill | null>(null)
const expanded = ref(new Set<string>())

const normalizedQuery = computed(() => query.value.trim().toLocaleLowerCase())
const sideEffects = computed(() => [...new Set(capabilities.value.map(item => item.sideEffectLevel))].sort())

function matchesQuery(...values: string[]) {
  return !normalizedQuery.value || values.some(value => value.toLocaleLowerCase().includes(normalizedQuery.value))
}

const filteredSkills = computed(() => skills.value.filter(skill =>
  matchesQuery(skill.id, skill.displayName, skill.description, skill.kind, ...(skill.supportedWorkflowTypes || []), ...skill.capabilities),
))

const filteredCapabilities = computed(() => {
  const allowed = selectedSkill.value ? new Set(selectedSkill.value.capabilities) : null
  return capabilities.value.filter(capability =>
    (!allowed || allowed.has(capability.id))
    && (!sideEffect.value || capability.sideEffectLevel === sideEffect.value)
    && (loopFilter.value === 'all' || capability.supportsLoop === (loopFilter.value === 'yes'))
    && matchesQuery(capability.id, capability.nodeType, capability.description, capability.argumentSchema),
  )
})

function loadError(value: unknown) {
  if (value instanceof AxiosError) {
    if (value.response?.status === 401) return '登入已失效，請重新登入。'
    if (value.response?.status === 403) return '此帳號沒有檢視 Registry 的權限。'
  }
  return '無法載入 Agent Registry，請稍後再試。'
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const registry = await getAgentRegistry()
    skills.value = registry.skills
    capabilities.value = registry.capabilities
    if (selectedSkill.value) {
      selectedSkill.value = registry.skills.find(item => item.id === selectedSkill.value?.id) ?? null
    }
  } catch (value) {
    error.value = loadError(value)
  } finally {
    loading.value = false
  }
}

function selectSkill(skill: WorkflowSkill) {
  selectedSkill.value = selectedSkill.value?.id === skill.id ? null : skill
}

function toggleDetails(id: string) {
  const next = new Set(expanded.value)
  next.has(id) ? next.delete(id) : next.add(id)
  expanded.value = next
}

function clearFilters() {
  query.value = ''
  sideEffect.value = ''
  loopFilter.value = 'all'
  selectedSkill.value = null
}

onMounted(load)
</script>

<template>
  <main class="prestige-page registry-page">
    <section class="prestige-section">
      <div class="prestige-section-head registry-head">
        <div>
          <p class="prestige-label">AGENT REGISTRY</p>
          <h1 class="prestige-section-title">Skill 與 Capability Registry</h1>
        </div>
        <button class="prestige-btn" :disabled="loading" @click="load">重新整理</button>
      </div>

      <div class="registry-stats">
        <div class="prestige-panel stat"><strong>{{ skills.length }}</strong><span>Skills</span></div>
        <div class="prestige-panel stat"><strong>{{ capabilities.length }}</strong><span>Capabilities</span></div>
      </div>

      <section class="prestige-panel filters" aria-label="Registry 篩選">
        <input v-model="query" class="prestige-input search" placeholder="搜尋 ID、描述、Node Type…" />
        <select v-model="sideEffect" class="prestige-input">
          <option value="">所有 Side Effects</option>
          <option v-for="value in sideEffects" :key="value" :value="value">{{ value }}</option>
        </select>
        <select v-model="loopFilter" class="prestige-input">
          <option value="all">所有 Loop 類型</option>
          <option value="yes">支援 Loop</option>
          <option value="no">不支援 Loop</option>
        </select>
        <button class="prestige-btn" @click="clearFilters">清除篩選</button>
      </section>

      <p v-if="loading" class="registry-state">正在載入 Registry…</p>
      <div v-else-if="error" class="prestige-panel registry-error" role="alert">
        <span>{{ error }}</span><button class="prestige-btn" @click="load">重試</button>
      </div>

      <template v-else>
        <section class="registry-section">
          <div class="section-title"><div><p class="prestige-label">SKILLS</p><h2>工作技能</h2></div><span>{{ filteredSkills.length }} / {{ skills.length }}</span></div>
          <div v-if="filteredSkills.length" class="skill-grid">
            <article v-for="skill in filteredSkills" :key="skill.id" class="prestige-panel skill-card" :class="{ selected: selectedSkill?.id === skill.id }">
              <button class="skill-select" @click="selectSkill(skill)">
                <div class="skill-title"><code>{{ skill.id }}</code><span class="skill-kind">{{ skill.kind }}{{ skill.routable ? ' · Routable' : '' }}</span></div>
                <strong>{{ skill.displayName }}</strong>
                <span>{{ skill.description }}</span>
              </button>
              <dl class="skill-contract">
                <dt>Workflows</dt><dd>{{ skill.supportedWorkflowTypes?.join(', ') || '—' }}</dd>
                <dt>Prompt</dt><dd>{{ skill.promptTemplateId ? `${skill.promptTemplateId} v${skill.promptVersion}` : '—' }}</dd>
                <dt>Required inputs</dt><dd>{{ skill.requiredInputs?.join(', ') || '—' }}</dd>
              </dl>
              <div class="capability-tags">
                <button v-for="capability in skill.capabilities" :key="capability" @click="selectSkill(skill)">{{ capability }}</button>
              </div>
            </article>
          </div>
          <p v-else class="prestige-panel registry-empty">沒有符合條件的 Skill。</p>
        </section>

        <section class="registry-section">
          <div class="section-title">
            <div><p class="prestige-label">CAPABILITIES</p><h2>節點能力</h2></div>
            <div class="capability-count"><span v-if="selectedSkill">Skill: <code>{{ selectedSkill.id }}</code></span><span>{{ filteredCapabilities.length }} / {{ capabilities.length }}</span></div>
          </div>
          <div v-if="filteredCapabilities.length" class="prestige-panel registry-table-wrap">
            <table class="registry-table">
              <thead><tr><th>Capability</th><th>Node Type</th><th>Side Effect</th><th>Loop</th><th>Max</th><th></th></tr></thead>
              <tbody v-for="item in filteredCapabilities" :key="item.id">
                <tr>
                  <td><code>{{ item.id }}</code><small>{{ item.description }}</small></td>
                  <td><code>{{ item.nodeType }}</code></td>
                  <td>{{ item.sideEffectLevel }}</td>
                  <td>{{ item.supportsLoop ? 'Yes' : 'No' }}</td>
                  <td>{{ item.maxOccurrences }}</td>
                  <td><button class="detail-button" @click="toggleDetails(item.id)">{{ expanded.has(item.id) ? '收合' : '詳情' }}</button></td>
                </tr>
                <tr v-if="expanded.has(item.id)" class="detail-row">
                  <td colspan="6">
                    <dl>
                      <dt>Capability Argument Contract</dt><dd><code>{{ item.argumentSchema }}</code></dd>
                      <dt>Input Mode</dt><dd>{{ item.inputMode || 'Any' }}</dd>
                      <dt>Required Keys</dt><dd>{{ item.requiredKeys.join(', ') || '—' }}</dd>
                      <dt>Produced Keys</dt><dd>{{ item.producedKeys.join(', ') || '—' }}</dd>
                      <dt>Idempotent</dt><dd>{{ item.idempotent ? 'Yes' : 'No' }}</dd>
                      <dt>Capability Parameter Schema</dt><dd><pre>{{ JSON.stringify(item.parametersSchema ?? {}, null, 2) }}</pre></dd>
                    </dl>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="prestige-panel registry-empty">沒有符合條件的 Capability。</p>
        </section>
      </template>
    </section>
  </main>
</template>

<style scoped>
.registry-page { min-height: calc(100vh - 60px); }
.registry-head { gap: 20px; }
.registry-stats { display: grid; grid-template-columns: repeat(2, minmax(0, 180px)); gap: 12px; margin-bottom: 16px; }
.stat { display: grid; gap: 3px; padding: 16px 20px; }
.stat strong { color: var(--gold); font-family: var(--serif); font-size: 28px; }
.stat span, .registry-state, .registry-empty { color: var(--muted); }
.filters { display: grid; grid-template-columns: minmax(240px, 1fr) 190px 170px auto; gap: 10px; padding: 14px; align-items: center; }
.registry-section { margin-top: 28px; }
.section-title { display: flex; justify-content: space-between; align-items: end; gap: 16px; margin-bottom: 12px; color: var(--muted); font-size: 12px; }
.section-title h2 { margin: 4px 0 0; color: var(--ivory); font-family: var(--serif); font-size: 22px; }
.skill-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 12px; }
.skill-card { padding: 16px; transition: border-color .2s, background .2s; }
.skill-card.selected { border-color: var(--gold); background: rgba(201,168,106,.08); }
.skill-select { display: grid; gap: 8px; width: 100%; padding: 0; border: 0; color: var(--ivory); background: transparent; text-align: left; cursor: pointer; }
.skill-title { display: flex; justify-content: space-between; gap: 12px; align-items: center; }
.skill-select code { color: var(--gold); font-size: 13px; }
.skill-select strong { color: var(--ivory); font-family: var(--serif); font-size: 17px; font-weight: 500; }
.skill-kind { padding: 3px 7px; border: 1px solid var(--gold-border-soft); border-radius: 999px; color: var(--gold) !important; font: 9px 'SFMono-Regular', Consolas, monospace; white-space: nowrap; }
.skill-select span, .registry-table small { color: var(--muted); font-size: 12px; line-height: 1.5; }
.skill-contract { display: grid; grid-template-columns: 92px minmax(0, 1fr); gap: 5px 10px; margin: 14px 0 0; padding-top: 12px; border-top: 1px solid var(--gold-border-soft); font-size: 11px; }
.skill-contract dt { color: var(--muted); }.skill-contract dd { margin: 0; color: var(--ivory); overflow-wrap: anywhere; }
.capability-tags { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 14px; }
.capability-tags button, .detail-button { border: 1px solid var(--gold-border-soft); border-radius: 999px; padding: 4px 8px; color: var(--gold); background: rgba(201,168,106,.04); font: 10px 'SFMono-Regular', Consolas, monospace; cursor: pointer; }
.capability-count { display: flex; flex-wrap: wrap; justify-content: end; gap: 14px; }
.registry-table-wrap { overflow-x: auto; padding: 0 14px; }
.registry-table { width: 100%; min-width: 900px; border-collapse: collapse; color: var(--ivory); font-size: 12px; }
.registry-table th { padding: 12px 10px; border-bottom: 1px solid var(--gold-border); color: var(--gold); font-size: 10px; letter-spacing: .12em; text-align: left; text-transform: uppercase; }
.registry-table td { padding: 12px 10px; border-bottom: 1px solid var(--gold-border-soft); vertical-align: top; }
.registry-table td:first-child { display: grid; gap: 4px; }
.registry-table code, .capability-count code { font-family: 'SFMono-Regular', Consolas, monospace; color: var(--gold-strong); overflow-wrap: anywhere; }
.detail-row td { background: rgba(11,18,32,.4); }
.detail-row dl { display: grid; grid-template-columns: 140px minmax(0, 1fr); gap: 7px 14px; margin: 0; }
.detail-row dt { color: var(--muted); }.detail-row dd { margin: 0; overflow-wrap: anywhere; }
.registry-state, .registry-empty { padding: 28px; text-align: center; }
.registry-error { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-top: 16px; padding: 16px; color: #e3a3a3; }
@media (max-width: 880px) { .filters { grid-template-columns: 1fr 1fr; }.search { grid-column: 1 / -1; } }
@media (max-width: 560px) { .registry-stats, .filters { grid-template-columns: 1fr; }.search { grid-column: auto; }.skill-grid { grid-template-columns: 1fr; }.section-title { align-items: start; }.detail-row dl { grid-template-columns: 1fr; gap: 3px; } }
</style>
