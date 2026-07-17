<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  NButton,
  NInput,
  NTag,
  NModal,
  NForm,
  NFormItem,
  NSelect,
  NSpace,
  NIcon,
  NPopconfirm,
  NPagination,
} from 'naive-ui'
import {
  SearchOutline,
  CreateOutline,
  TrashOutline,
} from '@vicons/ionicons5'

const { t } = useI18n()

const searchQuery = ref('')
const currentPage = ref(1)
const pageSize = ref(10)

const users = ref([
  { id: '1', name: 'Admin User', email: 'admin@equitylens.com', role: 'admin', status: 'active', lastLogin: '2026-06-03 10:00:00' },
  { id: '2', name: 'Analyst A', email: 'analyst.a@equitylens.com', role: 'analyst', status: 'active', lastLogin: '2026-06-03 09:30:00' },
  { id: '3', name: 'Analyst B', email: 'analyst.b@equitylens.com', role: 'analyst', status: 'active', lastLogin: '2026-06-02 16:45:00' },
  { id: '4', name: 'Viewer C', email: 'viewer.c@equitylens.com', role: 'viewer', status: 'inactive', lastLogin: '2026-05-28 11:20:00' },
])

const filteredUsers = computed(() => {
  let result = users.value
  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase()
    result = result.filter(u =>
      u.name.toLowerCase().includes(q) ||
      u.email.toLowerCase().includes(q)
    )
  }
  return result
})

const roleOptions = computed(() => [
  { label: t('admin.users.roleAdmin'), value: 'admin' },
  { label: t('admin.users.roleAnalyst'), value: 'analyst' },
  { label: t('admin.users.roleViewer'), value: 'viewer' },
])

const showCreateModal = ref(false)
const createForm = ref({
  name: '',
  email: '',
  role: 'analyst',
})

function getRoleLabel(role: string): string {
  const map: Record<string, string> = {
    admin: t('admin.users.roleAdmin'),
    analyst: t('admin.users.roleAnalyst'),
    viewer: t('admin.users.roleViewer'),
  }
  return map[role] || role
}

function getRoleType(role: string): 'error' | 'warning' | 'default' {
  const map: Record<string, 'error' | 'warning' | 'default'> = {
    admin: 'error',
    analyst: 'warning',
    viewer: 'default',
  }
  return map[role] || 'default'
}

function handleCreate() {
  users.value.push({
    id: String(users.value.length + 1),
    name: createForm.value.name,
    email: createForm.value.email,
    role: createForm.value.role,
    status: 'active',
    lastLogin: '-',
  })
  showCreateModal.value = false
  createForm.value = { name: '', email: '', role: 'analyst' }
}

function handleDelete(id: string) {
  users.value = users.value.filter(u => u.id !== id)
}
</script>

<template>
  <main class="prestige-section prestige-fade admin-view">
    <div class="prestige-section-head">
      <div>
        <p class="prestige-label view-eyebrow">{{ t('admin.users.titleEn') }}</p>
        <h1 class="prestige-section-title">{{ t('admin.users.title') }}</h1>
      </div>
      <button class="prestige-btn prestige-btn-solid" @click="showCreateModal = true">
        {{ t('admin.users.create') }}
      </button>
    </div>

    <div class="prestige-panel search-panel">
      <NInput
        v-model:value="searchQuery"
        :placeholder="t('admin.users.searchPlaceholder')"
        clearable
        class="search-input"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
    </div>

    <div class="prestige-panel table-panel">
      <table class="prestige-table">
        <thead>
          <tr>
            <th>{{ t('admin.users.table.user') }}</th>
            <th>{{ t('admin.users.table.role') }}</th>
            <th>{{ t('admin.users.table.status') }}</th>
            <th>{{ t('admin.users.table.lastLogin') }}</th>
            <th class="col-actions">{{ t('admin.users.table.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="user in filteredUsers" :key="user.id">
            <td>
              <div class="user-cell">
                <div class="user-avatar">{{ user.name.charAt(0) }}</div>
                <div>
                  <div class="user-name">{{ user.name }}</div>
                  <div class="user-email">{{ user.email }}</div>
                </div>
              </div>
            </td>
            <td>
              <NTag :type="getRoleType(user.role)" size="small" round>
                {{ getRoleLabel(user.role) }}
              </NTag>
            </td>
            <td>
              <NTag :type="user.status === 'active' ? 'success' : 'default'" size="small" round>
                {{ user.status === 'active' ? t('admin.users.active') : t('admin.users.inactive') }}
              </NTag>
            </td>
            <td class="prestige-mono cell-muted">{{ user.lastLogin }}</td>
            <td class="col-actions">
              <NSpace justify="center">
                <NButton text size="small" class="icon-btn">
                  <template #icon>
                    <NIcon><CreateOutline /></NIcon>
                  </template>
                </NButton>
                <NPopconfirm @positive-click="handleDelete(user.id)">
                  <template #trigger>
                    <NButton text size="small" class="icon-btn icon-btn--danger">
                      <template #icon>
                        <NIcon><TrashOutline /></NIcon>
                      </template>
                    </NButton>
                  </template>
                  {{ t('admin.users.delete') }}?
                </NPopconfirm>
              </NSpace>
            </td>
          </tr>
        </tbody>
      </table>
      <div class="table-footer">
        <NPagination v-model:page="currentPage" :page-size="pageSize" :item-count="filteredUsers.length" />
      </div>
    </div>

    <!-- Create Modal -->
    <NModal
      v-model:show="showCreateModal"
      :title="t('admin.users.createTitle')"
      preset="card"
      style="width: 420px;"
      :bordered="false"
    >
      <NForm :model="createForm" label-placement="top">
        <NFormItem :label="t('admin.users.name')" required>
          <NInput v-model:value="createForm.name" :placeholder="t('admin.users.namePlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('admin.users.email')" required>
          <NInput v-model:value="createForm.email" :placeholder="t('admin.users.emailPlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('admin.users.role')">
          <NSelect v-model:value="createForm.role" :options="roleOptions" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showCreateModal = false">{{ t('admin.users.cancel') }}</NButton>
          <NButton type="primary" class="btn-primary" @click="handleCreate">{{ t('admin.users.createBtn') }}</NButton>
        </NSpace>
      </template>
    </NModal>
  </main>
</template>

<style scoped>
.admin-view {
  padding-top: 8px;
}

.view-eyebrow {
  margin: 0 0 10px;
}

.search-panel {
  padding: 16px 20px;
  margin-bottom: 20px;
}

.search-input {
  max-width: 400px;
}

.table-panel {
  overflow: hidden;
}

.col-actions {
  text-align: center;
}

.cell-muted {
  color: var(--muted);
  font-size: 13px;
}

.user-cell {
  display: flex;
  align-items: center;
  gap: 12px;
}

.user-avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  border: 1px solid var(--gold-border);
  background: rgba(201, 168, 106, 0.08);
  color: var(--gold);
  font-family: var(--serif);
  font-size: 15px;
  display: grid;
  place-items: center;
  flex-shrink: 0;
}

.user-name {
  color: var(--ivory);
  font-weight: 500;
}

.user-email {
  color: var(--muted);
  font-size: 13px;
}

.table-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--gold-border-soft);
  display: flex;
  justify-content: flex-end;
}

/* ---- naive-ui → Prestige ---- */
.search-input {
  background: rgba(11, 18, 32, 0.6) !important;
  border-radius: 4px !important;
}

.search-input :deep(.n-input__border) {
  border-color: var(--gold-border-soft) !important;
}

.search-input :deep(.n-input__state-border) {
  box-shadow: none !important;
}

.search-input:hover :deep(.n-input__state-border),
.search-input.n-input--focus :deep(.n-input__state-border) {
  border-color: var(--gold) !important;
}

.search-input :deep(.n-input__input-el) {
  color: var(--ivory) !important;
  caret-color: var(--gold);
}

.search-input :deep(.n-input__input-el::placeholder) {
  color: var(--muted);
}

.search-input :deep(.n-input__prefix) {
  color: var(--gold) !important;
}

.search-input :deep(.n-input__suffix) {
  color: var(--muted) !important;
}

:deep(.n-tag) {
  background: transparent !important;
  font-weight: 600;
  letter-spacing: 0.06em;
}

:deep(.n-tag.n-tag--success-type) {
  border-color: rgba(127, 163, 135, 0.5) !important;
  color: var(--up) !important;
}

:deep(.n-tag.n-tag--warning-type) {
  border-color: rgba(212, 162, 78, 0.5) !important;
  color: #d4a24e !important;
}

:deep(.n-tag.n-tag--error-type) {
  border-color: rgba(176, 92, 92, 0.5) !important;
  color: var(--down) !important;
}

:deep(.n-tag.n-tag--default-type) {
  border-color: var(--gold-border) !important;
  color: var(--muted) !important;
}

.icon-btn {
  color: var(--gold) !important;
  border-radius: 4px;
}

.icon-btn:hover {
  color: var(--gold-strong) !important;
  background: rgba(201, 168, 106, 0.08) !important;
}

.icon-btn--danger {
  color: var(--down) !important;
}

.icon-btn--danger:hover {
  color: #c97a7a !important;
  background: rgba(176, 92, 92, 0.1) !important;
}

.table-footer :deep(.n-pagination-item) {
  background: transparent !important;
  border-color: var(--gold-border-soft) !important;
  color: var(--muted) !important;
}

.table-footer :deep(.n-pagination-item:hover) {
  border-color: var(--gold) !important;
  color: var(--gold) !important;
}

.table-footer :deep(.n-pagination-item--active),
.table-footer :deep(.n-pagination-item--active:hover) {
  background: rgba(201, 168, 106, 0.08) !important;
  border-color: var(--gold) !important;
  color: var(--gold) !important;
}
</style>
