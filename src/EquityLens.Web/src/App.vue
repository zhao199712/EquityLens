<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { NConfigProvider, darkTheme } from 'naive-ui'
import { useAuthStore } from './stores/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const isAuthPage = computed(() => route.name === 'login' || route.name === 'register')

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
  return name
})

function handleMenuSelect(key: string) {
  if (key !== activeMenuKey.value) {
    router.push({ name: key })
  }
}
</script>

<template>
  <NConfigProvider :theme="darkTheme" :theme-overrides="themeOverrides">
    <div class="kimi-app-shell">
      <!-- Header - hidden on login page -->
      <header v-if="!isAuthPage" class="kimi-app-header">
        <RouterLink class="kimi-brand" to="/">
          <span class="kimi-brand-mark">EL</span>
          <span class="kimi-brand-text">EquityLens</span>
        </RouterLink>

        <nav class="kimi-main-nav">
          <button
            :class="['kimi-nav-item', activeMenuKey === 'dashboard' && 'active']"
            @click="handleMenuSelect('dashboard')"
          >
            Dashboard
          </button>
          <span class="kimi-nav-sep">|</span>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'portfolios' && 'active']"
            @click="handleMenuSelect('portfolios')"
          >
            Portfolios
          </button>
          <span class="kimi-nav-sep">|</span>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'risk-runs' && 'active']"
            @click="handleMenuSelect('risk-runs')"
          >
            Risk Runs
          </button>
          <span class="kimi-nav-sep">|</span>
          <button
            :class="['kimi-nav-item', activeMenuKey === 'financial-reports' && 'active']"
            @click="handleMenuSelect('financial-reports')"
          >
            Reports
          </button>
        </nav>

        <div class="kimi-header-right">
          <RouterLink class="kimi-admin-link" to="/settings">Settings</RouterLink>
          <button class="kimi-logout-btn" @click="handleLogout">Logout</button>
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
  background: var(--kimi-bg-light);
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
  background: var(--kimi-bg-light);
  border-bottom: 1px solid var(--kimi-border-light);
}

.kimi-brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  text-decoration: none;
  color: var(--kimi-text-light);
}

.kimi-brand-mark {
  display: inline-grid;
  width: 32px;
  height: 32px;
  place-items: center;
  background: var(--kimi-text-light);
  color: var(--kimi-bg-light);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0;
}

.kimi-brand-text {
  font-size: 16px;
  font-weight: 700;
  letter-spacing: -0.02em;
}

.kimi-main-nav {
  display: flex;
  align-items: center;
  gap: 12px;
}

.kimi-nav-item {
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  background: none;
  border: none;
  cursor: pointer;
  padding: 4px 0;
  color: var(--kimi-muted);
  transition: all 0.2s ease;
  font-family: var(--kimi-font-body);
}

.kimi-nav-item:hover {
  color: var(--kimi-text-light);
}

.kimi-nav-item.active {
  color: var(--kimi-text-light);
  font-weight: 700;
}

.kimi-nav-sep {
  color: var(--kimi-border-light);
  font-size: 12px;
}

.kimi-admin-link {
  margin-left: auto;
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--kimi-muted);
  text-decoration: none;
  transition: color 0.2s ease;
}

.kimi-admin-link:hover {
  color: var(--kimi-text-light);
}

.kimi-header-right {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 16px;
}

.kimi-logout-btn {
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  background: none;
  border: 1px solid var(--kimi-border-light);
  cursor: pointer;
  padding: 6px 12px;
  color: var(--kimi-muted);
  transition: all 0.2s ease;
  font-family: var(--kimi-font-body);
}

.kimi-logout-btn:hover {
  color: var(--kimi-text-light);
  border-color: var(--kimi-text-light);
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
