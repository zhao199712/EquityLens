<script setup lang="ts">
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { TrendingUpOutline } from '@vicons/ionicons5'
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">Price Management</p>
        <h1>市場價格管理</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><TrendingUpOutline /></NIcon>
        </template>
        匯入價格
      </NButton>
    </section>

    <div class="empty-state">
      <NEmpty description="價格資料管理功能開發中">
        <template #extra>
          <NButton type="primary" class="btn-primary">瞭解更多</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>
