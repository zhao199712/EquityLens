<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { NConfigProvider, NLayout, NLayoutHeader, NLayoutContent, NMenu } from 'naive-ui'

const route = useRoute()
const router = useRouter()

const menuOptions = [
  {
    label: 'Dashboard',
    key: 'dashboard',
  },
  {
    label: 'Portfolios',
    key: 'portfolios',
  },
]

const activeMenuKey = computed(() => String(route.name ?? 'dashboard'))

function handleMenuSelect(key: string) {
  if (key !== activeMenuKey.value) {
    router.push({ name: key })
  }
}
</script>

<template>
  <NConfigProvider>
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
          @update:value="handleMenuSelect"
        />
      </NLayoutHeader>

      <NLayoutContent class="app-content">
        <RouterView />
      </NLayoutContent>
    </NLayout>
  </NConfigProvider>
</template>
