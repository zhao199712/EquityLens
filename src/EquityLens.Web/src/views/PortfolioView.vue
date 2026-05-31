<script setup lang="ts">
import { NButton, NEmpty } from 'naive-ui'
</script>

<template>
  <main class="page">
    <section class="page-heading">
      <div>
        <p class="eyebrow">Portfolio management</p>
        <h1>Portfolios</h1>
      </div>
      <NButton type="primary">New Portfolio</NButton>
    </section>

    <div class="empty-state">
      <NEmpty description="No portfolios yet" />
    </div>
  </main>
</template>
