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
  AddOutline,
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
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">{{ t('admin.users.titleEn') }}</p>
        <h1>{{ t('admin.users.title') }}</h1>
      </div>
      <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
        <template #icon>
          <NIcon><AddOutline /></NIcon>
        </template>
        {{ t('admin.users.create') }}
      </NButton>
    </section>

    <div class="glass-panel" style="padding: 16px 20px;">
      <NInput
        v-model:value="searchQuery"
        :placeholder="t('admin.users.searchPlaceholder')"
        clearable
        style="max-width: 400px;"
      >
        <template #prefix>
          <NIcon><SearchOutline /></NIcon>
        </template>
      </NInput>
    </div>

    <div class="glass-panel" style="margin-top: 24px; padding: 0; overflow: hidden;">
      <div class="glass-table">
        <table style="width: 100%; border-collapse: collapse;">
          <thead>
            <tr style="border-bottom: 1px solid var(--border-subtle);">
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.users.table.user') }}</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.users.table.role') }}</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.users.table.status') }}</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.users.table.lastLogin') }}</th>
              <th style="text-align: center; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('admin.users.table.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="user in filteredUsers"
              :key="user.id"
              style="border-bottom: 1px solid var(--border-subtle);"
              class="table-row-hover"
            >
              <td style="padding: 14px 20px;">
                <div style="display: flex; align-items: center; gap: 12px;">
                  <div
                    style="width: 36px; height: 36px; border-radius: 50%; background: linear-gradient(135deg, var(--accent-primary), var(--accent-secondary)); display: grid; place-items: center; color: #fff; font-weight: 600; font-size: 14px;"
                  >
                    {{ user.name.charAt(0) }}
                  </div>
                  <div>
                    <div style="color: var(--text-primary); font-weight: 500;">{{ user.name }}</div>
                    <div style="color: var(--text-tertiary); font-size: 13px;">{{ user.email }}</div>
                  </div>
                </div>
              </td>
              <td style="padding: 14px 20px;">
                <NTag :type="getRoleType(user.role)" size="small" round>
                  {{ getRoleLabel(user.role) }}
                </NTag>
              </td>
              <td style="padding: 14px 20px;">
                <NTag :type="user.status === 'active' ? 'success' : 'default'" size="small" round>
                  {{ user.status === 'active' ? t('admin.users.active') : t('admin.users.inactive') }}
                </NTag>
              </td>
              <td style="padding: 14px 20px; color: var(--text-secondary); font-size: 13px;">
                {{ user.lastLogin }}
              </td>
              <td style="padding: 14px 20px; text-align: center;">
                <NSpace justify="center">
                  <NButton text type="primary" size="small">
                    <template #icon>
                      <NIcon><CreateOutline /></NIcon>
                    </template>
                  </NButton>
                  <NPopconfirm @positive-click="handleDelete(user.id)">
                    <template #trigger>
                      <NButton text type="error" size="small">
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
      </div>
      <div style="padding: 16px 20px; border-top: 1px solid var(--border-subtle); display: flex; justify-content: flex-end;">
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
.table-row-hover:hover {
  background: rgba(255, 255, 255, 0.04);
}
</style>
