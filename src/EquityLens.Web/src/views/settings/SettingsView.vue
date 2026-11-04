<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getRiskPreference, saveRiskPreference, type RiskPreference, type RiskPreferenceProfile } from '../../services/settings'
import { useI18n } from 'vue-i18n'
import { setLocale } from '../../locales'

const { t, locale } = useI18n()
const profile = ref<RiskPreferenceProfile | null>(null)
const selectedRisk = ref<RiskPreference>('Balanced')
const loading = ref(true)
const saving = ref(false)
const message = ref('')
const failed = ref(false)
onMounted(async () => {
  try {
    profile.value = await getRiskPreference()
    selectedRisk.value = profile.value.value
  } catch { failed.value = true; message.value = 'settings.risk.loadError' }
  finally { loading.value = false }
})
async function saveRisk() {
  if (saving.value || !profile.value) return
  saving.value = true
  message.value = ''
  failed.value = false
  try {
    profile.value = await saveRiskPreference(selectedRisk.value)
    selectedRisk.value = profile.value.value
    message.value = 'settings.risk.saved'
  } catch { failed.value = true; message.value = 'settings.risk.saveError' }
  finally { saving.value = false }
}
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
      <div class="prestige-panel prestige-panel-pad language-panel risk-panel">
        <h2>{{ t('settings.risk.title') }}</h2>
        <p class="language-description">{{ t('settings.risk.description') }}</p>
        <p v-if="loading" role="status">{{ t('settings.risk.loading') }}</p>
        <template v-else-if="profile">
          <p data-testid="saved-risk-preference">{{ t('settings.risk.current') }} {{ t(`settings.risk.${profile.value}`) }}
            <span v-if="profile.isDefault">{{ t('settings.risk.default') }}</span>
            <span v-else>{{ t('settings.risk.explicit') }}</span>
          </p>
          <label for="risk-preference">{{ t('settings.risk.label') }}</label>
          <select id="risk-preference" v-model="selectedRisk" class="prestige-input" :disabled="saving">
            <option value="Conservative">{{ t('settings.risk.Conservative') }}</option>
            <option value="Balanced">{{ t('settings.risk.Balanced') }}</option>
            <option value="Aggressive">{{ t('settings.risk.Aggressive') }}</option>
          </select>
          <button class="prestige-btn prestige-btn-solid" :disabled="saving" @click="saveRisk">
            {{ t(saving ? 'settings.risk.saving' : 'settings.risk.save') }}
          </button>
        </template>
        <p v-if="message" :role="failed ? 'alert' : 'status'">{{ t(message) }}</p>
      </div>
    </section>
  </main>
</template>

<style scoped>
.settings-view { min-height: calc(100vh - 60px); }
.language-panel { max-width: 640px; }
.risk-panel { margin-top: 24px; }
.risk-panel button { display: block; margin-top: 16px; }
h2 { margin: 0 0 12px; font-family: var(--serif); font-size: 22px; }
.language-description, .language-note { color: var(--muted); line-height: 1.7; }
label { display: block; margin: 28px 0 10px; }
select { width: min(100%, 320px); }
.language-note { font-size: 13px; margin: 14px 0 0; }
</style>
