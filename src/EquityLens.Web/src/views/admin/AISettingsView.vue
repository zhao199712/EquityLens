<script setup lang="ts">
import { NButton, NEmpty, NIcon } from 'naive-ui'
import { SparklesOutline } from '@vicons/ionicons5'
</script>

<template>
  <main class="page animate-fade-in">
    <section class="page-heading">
      <div>
        <p class="eyebrow">AI Settings</p>
        <h1>AI Report 設定</h1>
      </div>
      <NButton type="primary" class="btn-primary">
        <template #icon>
          <NIcon><SparklesOutline /></NIcon>
        </template>
        儲存設定
      </NButton>
    </section>

    <div class="empty-state">
      <NEmpty description="AI Report 設定功能開發中">
        <template #extra>
          <NButton type="primary" class="btn-primary">瞭解更多</NButton>
        </template>
      </NEmpty>
    </div>
  </main>
</template>
