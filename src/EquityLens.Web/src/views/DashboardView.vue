<script setup lang="ts">
import { NButton, NCard, NGrid, NGridItem, NStatistic, NTag } from 'naive-ui'
import { useAppStore } from '../stores/app'

const app = useAppStore()

const metrics = [
  { label: 'Tracked portfolios', value: 0 },
  { label: 'Risk runs', value: 0 },
  { label: 'Uploaded reports', value: 0 },
  { label: 'AI memos', value: 0 },
]
</script>

<template>
  <main class="page">
    <section class="page-heading">
      <div>
        <p class="eyebrow">Investment intelligence workspace</p>
        <h1>EquityLens Dashboard</h1>
      </div>
      <NTag :type="app.apiStatus === 'online' ? 'success' : 'default'">
        API {{ app.apiStatus }}
      </NTag>
    </section>

    <NGrid :cols="4" :x-gap="16" :y-gap="16" responsive="screen">
      <NGridItem v-for="metric in metrics" :key="metric.label">
        <NCard size="small">
          <NStatistic :label="metric.label" :value="metric.value" />
        </NCard>
      </NGridItem>
    </NGrid>

    <section class="work-panel">
      <div>
        <h2>Next build target</h2>
        <p>Wire this shell to the .NET API, then add portfolio CRUD, report upload, and risk analysis views.</p>
      </div>
      <NButton type="primary">Create Portfolio</NButton>
    </section>
  </main>
</template>
