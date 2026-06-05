<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { NConfigProvider, NLayout, NLayoutHeader, NLayoutContent, NMenu, darkTheme } from 'naive-ui'
import {
  GridOutline,
  FolderOpenOutline,
  TrendingUpOutline,
  DocumentTextOutline,
  SettingsOutline,
} from '@vicons/ionicons5'
import { renderIcon } from './utils/icons'

const route = useRoute()
const router = useRouter()

const menuOptions = [
  {
    label: 'Dashboard',
    key: 'dashboard',
    icon: renderIcon(GridOutline),
  },
  {
    label: 'Portfolios',
    key: 'portfolios',
    icon: renderIcon(FolderOpenOutline),
  },
  {
    label: 'Risk Runs',
    key: 'risk-runs',
    icon: renderIcon(TrendingUpOutline),
  },
  {
    label: 'Reports',
    key: 'financial-reports',
    icon: renderIcon(DocumentTextOutline),
  },
  {
    label: 'Settings',
    key: 'settings',
    icon: renderIcon(SettingsOutline),
  },
]

const activeMenuKey = computed(() => {
  const name = String(route.name ?? 'dashboard')
  if (name.startsWith('portfolio')) return 'portfolios'
  if (name.startsWith('risk-run')) return 'risk-runs'
  if (name.startsWith('financial-report')) return 'financial-reports'
  return name
})

function handleMenuSelect(key: string) {
  if (key !== activeMenuKey.value) {
    router.push({ name: key })
  }
}

const themeOverrides = {
  common: {
    primaryColor: '#60a5fa',
    primaryColorHover: '#7cb8fb',
    primaryColorPressed: '#4a8fe8',
    primaryColorSuppl: '#60a5fa',
    bodyColor: '#0a0e1a',
    cardColor: 'rgba(255, 255, 255, 0.03)',
    modalColor: '#111827',
    tableColor: 'transparent',
    tableHeaderColor: '#1a1f35',
    tableColorHover: 'rgba(255, 255, 255, 0.04)',
    borderColor: 'rgba(255, 255, 255, 0.06)',
    textColor1: '#f0f4f8',
    textColor2: '#94a3b8',
    textColor3: '#64748b',
    placeholderColor: '#475569',
    inputColor: 'rgba(255, 255, 255, 0.06)',
    inputColorDisabled: 'rgba(255, 255, 255, 0.03)',
    buttonColor2: 'rgba(255, 255, 255, 0.06)',
    buttonColor2Hover: 'rgba(255, 255, 255, 0.10)',
    buttonColor2Pressed: 'rgba(255, 255, 255, 0.04)',
  },
  Menu: {
    itemTextColor: '#94a3b8',
    itemTextColorHover: '#f0f4f8',
    itemTextColorActive: '#60a5fa',
    itemTextColorActiveHover: '#7cb8fb',
    itemColorHover: 'rgba(255, 255, 255, 0.06)',
    itemColorActive: 'rgba(255, 255, 255, 0.06)',
    itemColorActiveHover: 'rgba(255, 255, 255, 0.10)',
    arrowColor: '#94a3b8',
    arrowColorHover: '#f0f4f8',
    arrowColorActive: '#60a5fa',
    arrowColorActiveHover: '#7cb8fb',
    groupTextColor: '#64748b',
  },
  Card: {
    color: 'rgba(255, 255, 255, 0.03)',
    colorModal: '#111827',
    borderColor: 'rgba(255, 255, 255, 0.06)',
    titleTextColor: '#f0f4f8',
  },
  Table: {
    thColor: '#1a1f35',
    thTextColor: '#94a3b8',
    tdTextColor: '#f0f4f8',
    tdColorHover: 'rgba(255, 255, 255, 0.04)',
    borderColor: 'rgba(255, 255, 255, 0.06)',
  },
  Tabs: {
    tabTextColor: '#94a3b8',
    tabTextColorActive: '#60a5fa',
    tabTextColorHover: '#f0f4f8',
    tabColor: 'transparent',
    tabColorActive: 'rgba(255, 255, 255, 0.06)',
    tabColorHover: 'rgba(255, 255, 255, 0.04)',
    barColor: '#60a5fa',
  },
  Input: {
    color: 'rgba(255, 255, 255, 0.06)',
    colorFocus: 'rgba(255, 255, 255, 0.08)',
    colorDisabled: 'rgba(255, 255, 255, 0.03)',
    borderColor: 'rgba(255, 255, 255, 0.06)',
    borderColorHover: 'rgba(255, 255, 255, 0.10)',
    borderColorFocus: '#60a5fa',
    placeholderColor: '#475569',
    textColor: '#f0f4f8',
  },
  Select: {
    color: 'rgba(255, 255, 255, 0.06)',
    colorActive: 'rgba(255, 255, 255, 0.08)',
    borderColor: 'rgba(255, 255, 255, 0.06)',
    borderColorHover: 'rgba(255, 255, 255, 0.10)',
    borderColorFocus: '#60a5fa',
    placeholderColor: '#475569',
    textColor: '#f0f4f8',
  },
  Button: {
    textColor: '#f0f4f8',
    textColorPrimary: '#ffffff',
    textColorHoverPrimary: '#ffffff',
    textColorPressedPrimary: '#ffffff',
    textColorFocusPrimary: '#ffffff',
    colorPrimary: '#60a5fa',
    colorHoverPrimary: '#7cb8fb',
    colorPressedPrimary: '#4a8fe8',
    colorFocusPrimary: '#60a5fa',
    colorDisabledPrimary: 'rgba(96, 165, 250, 0.5)',
    borderColorPrimary: 'transparent',
    borderColorHoverPrimary: 'transparent',
    borderColorPressedPrimary: 'transparent',
    borderColorFocusPrimary: 'transparent',
    rippleColorPrimary: 'rgba(255, 255, 255, 0.2)',
  },
  Tag: {
    color: 'rgba(255, 255, 255, 0.06)',
    colorBordered: 'rgba(255, 255, 255, 0.06)',
    textColor: '#94a3b8',
    textColorSuccess: '#34d399',
    textColorWarning: '#fbbf24',
    textColorError: '#f87171',
    textColorInfo: '#60a5fa',
    borderColorSuccess: 'rgba(52, 211, 153, 0.3)',
    borderColorWarning: 'rgba(251, 191, 36, 0.3)',
    borderColorError: 'rgba(248, 113, 113, 0.3)',
    borderColorInfo: 'rgba(96, 165, 250, 0.3)',
  },
  Dialog: {
    color: '#111827',
    textColor: '#f0f4f8',
    borderColor: 'rgba(255, 255, 255, 0.06)',
  },
  Modal: {
    color: '#111827',
    textColor: '#f0f4f8',
    borderColor: 'rgba(255, 255, 255, 0.06)',
  },
  Drawer: {
    color: '#111827',
    textColor: '#f0f4f8',
    borderColor: 'rgba(255, 255, 255, 0.06)',
  },
}
</script>

<template>
  <NConfigProvider :theme="darkTheme" :theme-overrides="themeOverrides">
    <NLayout class="app-shell">
      <NLayoutHeader class="app-header" bordered>
        <RouterLink class="brand" to="/">
          <span class="brand-mark">EL</span>
          <span>EquityLens</span>
        </RouterLink>
        <NMenu
          :value="activeMenuKey"
          mode="horizontal"
          :options="menuOptions"
          class="nav-menu"
          @update:value="handleMenuSelect"
        />
      </NLayoutHeader>

      <NLayoutContent class="app-content">
        <RouterView />
      </NLayoutContent>
    </NLayout>
  </NConfigProvider>
</template>
