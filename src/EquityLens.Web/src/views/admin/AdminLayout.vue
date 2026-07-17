<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { NLayout, NLayoutSider, NLayoutContent, NMenu, NMessageProvider } from 'naive-ui'
import {
  PeopleOutline,
  BusinessOutline,
  TrendingUpOutline,
  DocumentTextOutline,
  ShieldCheckmarkOutline,
  SparklesOutline,
  TimeOutline,
  TerminalOutline,
  FlaskOutline,
  GitNetworkOutline,
  CubeOutline,
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
  { label: 'Workflows', key: 'workflows', icon: renderIcon(GitNetworkOutline), path: '/admin/workflows' },
  { label: 'Node Catalog', key: 'nodes', icon: renderIcon(CubeOutline), path: '/admin/nodes' },
  {
    label: 'Agent Runs',
    key: 'agent-runs',
    icon: renderIcon(TerminalOutline),
    path: '/admin/agent-runs',
  },
  {
    label: 'Research Runs',
    key: 'research-runs',
    icon: renderIcon(FlaskOutline),
    path: '/admin/research-runs',
  },
  {
    label: 'Import Jobs',
    key: 'jobs',
    icon: renderIcon(TimeOutline),
    path: '/admin/jobs',
  },
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
])

const activeMenuKey = computed(() => {
  const path = route.path
  const item = menuOptions.value.find(m => path.startsWith(m.path))
  return item?.key || 'users'
})
</script>

<template>
  <NLayout class="admin-layout prestige-page" has-sider style="min-height: calc(100vh - 68px);">
    <NLayoutSider
      class="admin-sider"
      bordered
      collapse-mode="width"
      :collapsed-width="64"
      :width="240"
      show-trigger
      style="background: transparent;"
    >
      <div class="admin-brand">
        <div class="admin-brand-mark">EquityLens</div>
        <div class="prestige-label admin-brand-label">{{ t('admin.layout.title') }}</div>
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

    <NLayoutContent class="admin-content">
      <NMessageProvider>
        <RouterView />
      </NMessageProvider>
    </NLayoutContent>
  </NLayout>
</template>

<style scoped>
.admin-layout {
  background: linear-gradient(180deg, #0b1220 0%, #101a2e 100%) !important;
  border-top: 1px solid var(--gold-border-soft);
}

.admin-sider {
  border-color: var(--gold-border-soft) !important;
}

.admin-brand {
  padding: 20px 16px 16px;
  margin-bottom: 8px;
  border-bottom: 1px solid var(--gold-border-soft);
  white-space: nowrap;
  overflow: hidden;
}

.admin-brand-mark {
  margin-bottom: 6px;
  font-family: var(--serif);
  font-size: 18px;
  letter-spacing: 0.06em;
  color: var(--ivory);
}

.admin-brand-label {
  font-size: 10px;
}

.admin-content {
  padding: 32px;
  background: transparent !important;
}

:deep(.n-layout-sider__border) {
  background-color: var(--gold-border-soft) !important;
}

/* 尚未轉換的管理頁暫用 Prestige 配色,避免深色畫布上出現淺色標題 */
:deep(.page-heading h1) {
  color: var(--ivory) !important;
  font-family: var(--serif);
}

:deep(.page-heading .eyebrow) {
  color: var(--gold) !important;
}

:deep(.n-layout-sider-trigger) {
  background: #0d1526 !important;
  color: var(--gold) !important;
  border-color: var(--gold-border-soft) !important;
}

:deep(.n-menu-item) {
  margin: 2px 10px !important;
}

:deep(.n-menu-item-content) {
  border-radius: 4px !important;
  transition: box-shadow 0.2s ease;
}

:deep(.n-menu-item-content .n-menu-item-content-header) {
  color: var(--muted) !important;
  transition: color 0.2s ease;
}

:deep(.n-menu-item-content .n-menu-item-content__icon) {
  color: var(--muted) !important;
  transition: color 0.2s ease;
}

:deep(.n-menu-item-content:hover::before) {
  background-color: rgba(201, 168, 106, 0.06) !important;
}

:deep(.n-menu-item-content:hover .n-menu-item-content-header) {
  color: var(--ivory) !important;
}

:deep(.n-menu-item-content:hover .n-menu-item-content__icon) {
  color: var(--gold) !important;
}

:deep(.n-menu-item-content--selected) {
  box-shadow: inset 3px 0 0 var(--gold);
}

:deep(.n-menu-item-content--selected::before) {
  background-color: rgba(201, 168, 106, 0.08) !important;
}

:deep(.n-menu-item-content--selected .n-menu-item-content-header),
:deep(.n-menu-item-content--selected:hover .n-menu-item-content-header) {
  color: var(--gold) !important;
}

:deep(.n-menu-item-content--selected .n-menu-item-content__icon),
:deep(.n-menu-item-content--selected:hover .n-menu-item-content__icon) {
  color: var(--gold) !important;
}
</style>
