<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { setLocale } from '../../locales'

const { t, locale } = useI18n()
function changeLanguage(event: Event) {
  setLocale((event.target as HTMLSelectElement).value)
}
</script>

<template>
  <main class="prestige-page settings-view">
    <section class="prestige-section">
      <header class="prestige-section-head">
        <div>
          <p class="prestige-label">{{ t('settings.pageEyebrow') }}</p>
          <h1 class="prestige-section-title">{{ t('settings.pageTitle') }}</h1>
        </div>
      </header>
      <div class="prestige-panel prestige-panel-pad language-panel">
        <h2>{{ t('settings.language.title') }}</h2>
        <p class="language-description">{{ t('settings.language.description') }}</p>
        <label for="display-language">{{ t('settings.language.label') }}</label>
        <select id="display-language" class="prestige-input" :value="locale" @change="changeLanguage">
          <option value="en">English</option>
          <option value="zh-TW">繁體中文</option>
        </select>
        <p class="language-note">{{ t('settings.language.instant') }}</p>
      </div>
    </section>
  </main>
</template>

<style scoped>
.settings-view { min-height: calc(100vh - 60px); }
.language-panel { max-width: 640px; }
h2 { margin: 0 0 12px; font-family: var(--serif); font-size: 22px; }
.language-description, .language-note { color: var(--muted); line-height: 1.7; }
label { display: block; margin: 28px 0 10px; }
select { width: min(100%, 320px); }
.language-note { font-size: 13px; margin: 14px 0 0; }
</style>
