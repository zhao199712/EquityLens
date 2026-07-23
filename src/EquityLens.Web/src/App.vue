<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { NConfigProvider, darkTheme } from 'naive-ui'
import { enUS, zhTW } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from './stores/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const { t, locale } = useI18n()

const isAuthPage = computed(() => route.name === 'login' || route.name === 'register')
const isAdmin = computed(() => authStore.user?.role === 'Admin')

const naiveLocale = computed(() => locale.value === 'zh-TW' ? zhTW : enUS)

async function handleLogout() {
  await authStore.logout()
  router.push({ name: 'login' })
}

const themeOverrides = {
  common: {
    primaryColor: '#60a5fa',
    bodyColor: '#F5F5F5',
    cardColor: 'transparent',
    modalColor: '#FFFFFF',
    tableColor: 'transparent',
    tableHeaderColor: '#F5F5F5',
    borderColor: 'rgba(0,0,0,0.08)',
    textColor1: '#000000',
    textColor2: '#666666',
    textColor3: '#999999',
    placeholderColor: '#999999',
    inputColor: 'transparent',
    buttonColor2: 'transparent',
  },
  Menu: {
    itemTextColor: '#666666',
    itemTextColorHover: '#000000',
    itemTextColorActive: '#000000',
    itemColorHover: 'transparent',
    itemColorActive: 'transparent',
  },
  Card: {
    color: 'transparent',
    borderColor: 'rgba(0,0,0,0.08)',
  },
  Table: {
    thColor: '#F5F5F5',
    thTextColor: '#666666',
    tdTextColor: '#000000',
    borderColor: 'rgba(0,0,0,0.08)',
  },
  Tabs: {
    tabTextColor: '#666666',
    tabTextColorActive: '#000000',
    tabTextColorHover: '#000000',
    tabColor: 'transparent',
    tabColorActive: 'transparent',
    barColor: '#000000',
  },
  Input: {
    color: 'transparent',
    borderColor: 'rgba(0,0,0,0.15)',
    borderColorHover: '#000000',
    borderColorFocus: '#000000',
    placeholderColor: '#999999',
    textColor: '#000000',
  },
  Button: {
    textColor: '#000000',
    textColorPrimary: '#FFFFFF',
    colorPrimary: '#000000',
    colorHoverPrimary: '#333333',
    colorPressedPrimary: '#000000',
    borderColorPrimary: '#000000',
    borderColorHoverPrimary: '#000000',
  },
  Tag: {
    color: 'rgba(0,0,0,0.05)',
    textColor: '#666666',
    textColorSuccess: '#34d399',
    textColorWarning: '#fbbf24',
    textColorError: '#f87171',
  },
  Dialog: { color: '#FFFFFF', textColor: '#000000' },
  Modal: { color: '#FFFFFF', textColor: '#000000' },
}

const activeMenuKey = computed(() => {
  const name = String(route.name ?? 'dashboard')
  if (name.startsWith('portfolio')) return 'portfolios'
  if (name.startsWith('risk-run')) return 'risk-runs'
  if (name.startsWith('financial-report')) return 'financial-reports'
  if (name.startsWith('admin-')) return 'admin-agent-runs'
  if (name.startsWith('research')) return 'research'
  if (name.startsWith('agent-run')) return 'agent-runs'
  if (name === 'ask-agent') return 'ask-agent'
  return name
})

function handleMenuSelect(key: string) {
  if (key !== activeMenuKey.value) {
    router.push({ name: key })
  }
}
</script>

<template>
  <NConfigProvider :theme="darkTheme" :theme-overrides="themeOverrides" :locale="naiveLocale">
    <div class="kimi-app-shell">
      <!-- Header - hidden on login page -->
      <header v-if="!isAuthPage" class="kimi-app-header">
        <RouterLink class="kimi-brand" to="/">
          <span class="kimi-brand-mark">EL</span>
          <span class="kimi-brand-text">EQUITYLENS</span>
        </RouterLink>

        <nav class="kimi-main-nav">
          <button
            :class="['kimi-nav-item', activeMenuKey === 'home' && 'active']"
            @click="handleMenuSelect('home')"
          >
            {{ t('nav.home') }}
          </button>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'dashboard' && 'active']"
            @click="handleMenuSelect('dashboard')"
          >
            {{ t('nav.dashboard') }}
          </button>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'portfolios' && 'active']"
            @click="handleMenuSelect('portfolios')"
          >
            {{ t('nav.portfolios') }}
          </button>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'risk-runs' && 'active']"
            @click="handleMenuSelect('risk-runs')"
          >
            {{ t('nav.riskRuns') }}
          </button>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'financial-reports' && 'active']"
            @click="handleMenuSelect('financial-reports')"
          >
            {{ t('nav.reports') }}
          </button>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'research' && 'active']"
            @click="handleMenuSelect('research')"
          >
            Research
          </button>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'ask-agent' && 'active']"
            @click="handleMenuSelect('ask-agent')"
          >
            Ask Agent
          </button>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'agent-runs' && 'active']"
            @click="handleMenuSelect('agent-runs')"
          >
            Agent Runs
          </button>
          <button
            v-if="isAdmin"
            :class="['kimi-nav-item', activeMenuKey === 'admin-agent-runs' && 'active']"
            @click="handleMenuSelect('admin-agent-runs')"
          >
            Admin
          </button>
        </nav>

        <div class="kimi-header-right">
          <RouterLink class="kimi-admin-link" to="/settings">{{ t('nav.settings') }}</RouterLink>
          <button class="kimi-logout-btn" @click="handleLogout">{{ t('nav.logout') }}</button>
        </div>
      </header>

      <!-- Content -->
      <main class="kimi-app-content">
        <RouterView />
      </main>
    </div>
  </NConfigProvider>
</template>

<style scoped>
.kimi-app-shell {
  min-height: 100vh;
  background: #0b1220;
}

.kimi-app-header {
  position: sticky;
  top: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  gap: 32px;
  height: 60px;
  padding: 0 clamp(20px, 4vw, 80px);
  background: rgba(11, 18, 32, 0.88);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-bottom: 1px solid rgba(201, 168, 106, 0.18);
}

.kimi-brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  text-decoration: none;
  color: #f5efe0;
}

.kimi-brand-mark {
  display: inline-grid;
  width: 32px;
  height: 32px;
  place-items: center;
  border: 1px solid rgba(201, 168, 106, 0.35);
  border-radius: 4px;
  color: #c9a86a;
  font-family: Georgia, 'Noto Serif TC', serif;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.03em;
}

.kimi-brand-text {
  font-family: Georgia, 'Noto Serif TC', serif;
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 0.12em;
}

.kimi-main-nav {
  display: flex;
  align-items: center;
  gap: 22px;
}

.kimi-nav-item {
  position: relative;
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  background: none;
  border: none;
  cursor: pointer;
  padding: 6px 2px;
  color: #9a917c;
  transition: color 0.2s ease;
  font-family: 'Inter', 'Noto Sans TC', sans-serif;
}

.kimi-nav-item::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: -2px;
  height: 1px;
  background: #c9a86a;
  transform: scaleX(0);
  transform-origin: left;
  transition: transform 0.25s ease;
}

.kimi-nav-item:hover {
  color: #f5efe0;
}

.kimi-nav-item.active {
  color: #c9a86a;
}

.kimi-nav-item.active::after {
  transform: scaleX(1);
}

.kimi-admin-link {
  margin-left: auto;
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: #9a917c;
  text-decoration: none;
  transition: color 0.2s ease;
}

.kimi-admin-link:hover {
  color: #c9a86a;
}

.kimi-header-right {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 14px;
}

.kimi-logout-btn {
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  background: none;
  border: 1px solid rgba(201, 168, 106, 0.25);
  border-radius: 4px;
  cursor: pointer;
  padding: 6px 12px;
  color: #9a917c;
  transition: all 0.2s ease;
  font-family: 'Inter', 'Noto Sans TC', sans-serif;
}

.kimi-logout-btn:hover {
  color: #c9a86a;
  border-color: #c9a86a;
}

.kimi-app-content {
  position: relative;
  z-index: 1;
}

@media (max-width: 768px) {
  .kimi-app-header {
    gap: 16px;
    padding: 0 16px;
  }
  .kimi-main-nav {
    display: none;
  }
}
</style>
