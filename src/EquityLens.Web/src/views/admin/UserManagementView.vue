<script setup lang="ts">
import { ref, computed } from 'vue'
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

const searchQuery = ref('')
const currentPage = ref(1)
const pageSize = ref(10)

// Mock users
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

const roleOptions = [
  { label: '管理員', value: 'admin' },
  { label: '分析師', value: 'analyst' },
  { label: '檢視者', value: 'viewer' },
]

const showCreateModal = ref(false)
const createForm = ref({
  name: '',
  email: '',
  role: 'analyst',
})

function getRoleLabel(role: string): string {
  const map: Record<string, string> = {
    admin: '管理員',
    analyst: '分析師',
    viewer: '檢視者',
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
        <p class="eyebrow">User Management</p>
        <h1>使用者管理</h1>
      </div>
      <NButton type="primary" class="btn-primary" @click="showCreateModal = true">
        <template #icon>
          <NIcon><AddOutline /></NIcon>
        </template>
        新增使用者
      </NButton>
    </section>

    <div class="glass-panel" style="padding: 16px 20px;">
      <NInput
        v-model:value="searchQuery"
        placeholder="搜尋使用者..."
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
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">使用者</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">角色</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">狀態</th>
              <th style="text-align: left; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">最後登入</th>
              <th style="text-align: center; padding: 14px 20px; color: var(--text-secondary); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">操作</th>
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
                  {{ user.status === 'active' ? '啟用' : '停用' }}
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
                    確定要刪除此使用者嗎？
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
      title="新增使用者"
      preset="card"
      style="width: 420px;"
      :bordered="false"
    >
      <NForm :model="createForm" label-placement="top">
        <NFormItem label="名稱" required>
          <NInput v-model:value="createForm.name" placeholder="輸入使用者名稱" />
        </NFormItem>
        <NFormItem label="Email" required>
          <NInput v-model:value="createForm.email" placeholder="輸入 Email" />
        </NFormItem>
        <NFormItem label="角色">
          <NSelect v-model:value="createForm.role" :options="roleOptions" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showCreateModal = false">取消</NButton>
          <NButton type="primary" class="btn-primary" @click="handleCreate">新增</NButton>
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
