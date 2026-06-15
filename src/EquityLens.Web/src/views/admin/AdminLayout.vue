<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
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
const { t } = useI18n()

function handleMenuSelect(key: string) {
  const item = menuOptions.value.find(m => m.key === key)
  if (item) {
    router.push(item.path)
  }
}

const menuOptions = computed(() => [
  {
    label: t('admin.layout.userMgmt'),
    key: 'users',
    icon: renderIcon(PeopleOutline),
    path: '/admin/users',
  },
  {
    label: t('admin.layout.stockMgmt'),
    key: 'stocks',
    icon: renderIcon(BusinessOutline),
    path: '/admin/stocks',
  },
  {
    label: t('admin.layout.priceMgmt'),
    key: 'prices',
    icon: renderIcon(TrendingUpOutline),
    path: '/admin/prices',
  },
  {
    label: t('admin.layout.reportMgmt'),
    key: 'reports',
    icon: renderIcon(DocumentTextOutline),
    path: '/admin/reports',
  },
  {
    label: t('admin.layout.riskModelMgmt'),
    key: 'risk-models',
    icon: renderIcon(ShieldCheckmarkOutline),
    path: '/admin/risk-models',
  },
  {
    label: t('admin.layout.aiSettings'),
    key: 'ai-settings',
    icon: renderIcon(SparklesOutline),
    path: '/admin/ai-settings',
  },
  {
    label: t('admin.layout.jobMgmt'),
    key: 'jobs',
    icon: renderIcon(TimeOutline),
    path: '/admin/jobs',
  },
])

const activeMenuKey = computed(() => {
  const path = route.path
  const item = menuOptions.value.find(m => path.startsWith(m.path))
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
          {{ t('admin.layout.title') }}
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
