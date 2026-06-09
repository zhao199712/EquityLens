<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import { NLayout, NLayoutSider, NLayoutContent, NMenu } from 'naive-ui'
import {
  PeopleOutline,
  BusinessOutline,
  TrendingUpOutline,
  DocumentTextOutline,
  ShieldCheckmarkOutline,
  SparklesOutline,
  TimeOutline,
} from '@vicons/ionicons5'
import { renderIcon } from '../../utils/icons'

const route = useRoute()
const router = useRouter()

function handleMenuSelect(key: string) {
  const item = menuOptions.find(m => m.key === key)
  if (item) {
    router.push(item.path)
  }
}

const menuOptions = [
  {
    label: '使用者管理',
    key: 'users',
    icon: renderIcon(PeopleOutline),
    path: '/admin/users',
  },
  {
    label: '股票標的管理',
    key: 'stocks',
    icon: renderIcon(BusinessOutline),
    path: '/admin/stocks',
  },
  {
    label: '市場價格管理',
    key: 'prices',
    icon: renderIcon(TrendingUpOutline),
    path: '/admin/prices',
  },
  {
    label: '財報資料管理',
    key: 'reports',
    icon: renderIcon(DocumentTextOutline),
    path: '/admin/reports',
  },
  {
    label: 'Risk Model 管理',
    key: 'risk-models',
    icon: renderIcon(ShieldCheckmarkOutline),
    path: '/admin/risk-models',
  },
  {
    label: 'AI Report 設定',
    key: 'ai-settings',
    icon: renderIcon(SparklesOutline),
    path: '/admin/ai-settings',
  },
  {
    label: 'Job 狀態管理',
    key: 'jobs',
    icon: renderIcon(TimeOutline),
    path: '/admin/jobs',
  },
]

const activeMenuKey = computed(() => {
  const path = route.path
  const item = menuOptions.find(m => path.startsWith(m.path))
  return item?.key || 'users'
})
</script>

<template>
  <NLayout has-sider style="min-height: calc(100vh - 68px);">
    <NLayoutSider
      bordered
      collapse-mode="width"
      :collapsed-width="64"
      :width="240"
      show-trigger
      style="background: var(--bg-secondary); border-color: var(--border-subtle);"
    >
      <div style="padding: 20px 16px 12px;">
        <div style="color: var(--text-tertiary); font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.1em;">
          管理後台
        </div>
      </div>
      <NMenu
        :value="activeMenuKey"
        :options="menuOptions"
        :collapsed-width="64"
        :collapsed-icon-size="22"
        :indent="24"
        style="background: transparent;"
        @update:value="handleMenuSelect"
      />
    </NLayoutSider>

    <NLayoutContent style="padding: 32px; background: transparent;">
      <RouterView />
    </NLayoutContent>
  </NLayout>
</template>

<style scoped>
:deep(.n-layout-sider-trigger) {
  background: var(--bg-tertiary) !important;
  color: var(--text-secondary) !important;
  border-color: var(--border-subtle) !important;
}

:deep(.n-menu-item) {
  margin: 4px 8px !important;
  border-radius: var(--radius-sm) !important;
}

:deep(.n-menu-item-content) {
  color: var(--text-secondary) !important;
}

:deep(.n-menu-item-content:hover) {
  color: var(--text-primary) !important;
  background: var(--bg-glass) !important;
}

:deep(.n-menu-item-content--selected) {
  color: var(--accent-primary) !important;
  background: var(--bg-glass) !important;
}

:deep(.n-menu-item-content--selected::before) {
  background: var(--accent-primary) !important;
}
</style>
