<script setup lang="ts">
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { ShieldCheckmarkOutline } from '@vicons/ionicons5'
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">Risk Model Management</p>
        <h1>Risk Model 管理</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><ShieldCheckmarkOutline /></NIcon>
        </template>
        新增模型
      </NButton>
    </section>

    <div class="empty-state">
      <NEmpty description="Risk Model 管理功能開發中">
        <template #extra>
          <NButton type="primary" class="btn-primary">瞭解更多</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>
