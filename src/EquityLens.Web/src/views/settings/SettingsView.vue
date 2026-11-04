<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getRiskPreference, saveRiskPreference, submitRiskQuestionnaire, deleteRiskPreference, type RiskAnswers, type RiskPreference, type RiskPreferenceProfile } from '../../services/settings'
import { useI18n } from 'vue-i18n'
import { setLocale } from '../../locales'

const { t, locale } = useI18n()
const profile = ref<RiskPreferenceProfile | null>(null)
const selectedRisk = ref<RiskPreference>('Balanced')
const loading = ref(true)
const saving = ref(false)
const message = ref('')
const failed = ref(false)
const questionnaireOpen = ref(false)
const answerKeys = ['c1', 'c2', 'c3', 'w1', 'w2', 'w3'] as const
const answers = ref<Record<keyof RiskAnswers, number | null>>({ c1: null, c2: null, c3: null, w1: null, w2: null, w3: null })
const complete = computed(() => answerKeys.every(k => answers.value[k] !== null))
function clearAnswers() { answerKeys.forEach(k => { answers.value[k] = null }) }
function toggleQuestionnaire() { clearAnswers(); questionnaireOpen.value = !questionnaireOpen.value }
function assessmentDate(value: string) { return new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium' }).format(new Date(value)) }
async function updateProfile(action: () => Promise<RiskPreferenceProfile>) {
  if (saving.value || !profile.value) return
  saving.value = true; message.value = ''; failed.value = false
  try {
    profile.value = await action(); selectedRisk.value = profile.value.value
    clearAnswers(); questionnaireOpen.value = false; message.value = 'settings.risk.saved'
  } catch { failed.value = true; message.value = 'settings.risk.saveError' }
  finally { saving.value = false }
}
async function submitQuestionnaire() {
  if (!complete.value) return
  await updateProfile(() => submitRiskQuestionnaire({ ...answers.value } as RiskAnswers))
}
async function removeProfile() { await updateProfile(deleteRiskPreference) }
onMounted(async () => {
  try {
    profile.value = await getRiskPreference()
    selectedRisk.value = profile.value.value
  } catch { failed.value = true; message.value = 'settings.risk.loadError' }
  finally { loading.value = false }
})
async function saveRisk() { await updateProfile(() => saveRiskPreference(selectedRisk.value)) }
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
          <p>{{ t('settings.risk.source') }} {{ t(`settings.risk.source${profile.source || (profile.isDefault ? 'Default' : 'Manual')}`) }}</p>
          <p v-if="profile.assessedAt">{{ t('settings.risk.assessedAt') }} {{ assessmentDate(profile.assessedAt) }}</p>
          <p v-if="profile.capacityLevel && profile.willingnessLevel" data-testid="risk-sublevels">
            {{ t('settings.risk.capacity') }} {{ t(`settings.risk.${profile.capacityLevel}`) }} ·
            {{ t('settings.risk.willingness') }} {{ t(`settings.risk.${profile.willingnessLevel}`) }}
            — {{ t('settings.risk.lowerLevel') }}
          </p>
          <p v-if="profile.stale" role="status">{{ t('settings.risk.stale') }}</p>
          <button data-testid="assess-risk" class="prestige-btn" :disabled="saving" @click="toggleQuestionnaire">
            {{ t(questionnaireOpen ? 'settings.risk.cancel' : 'settings.risk.assess') }}
          </button>
          <form v-if="questionnaireOpen" data-testid="risk-questionnaire" @submit.prevent="submitQuestionnaire">
            <p>{{ t('settings.risk.questionnaireNote') }}</p>
            <fieldset :disabled="saving">
              <legend>{{ t('settings.risk.questionnaireTitle') }}</legend>
              <div v-for="key in answerKeys" :key="key">
                <label :for="`risk-${key}`">{{ t(`settings.risk.questions.${key}.title`) }}</label>
                <select :id="`risk-${key}`" v-model="answers[key]" class="prestige-input" required>
                  <option :value="null" disabled>{{ t('settings.risk.choose') }}</option>
                  <option v-if="key === 'w3'" :value="0">{{ t('settings.risk.questions.w3.none') }}</option>
                  <option v-for="n in [1, 2, 3]" :key="n" :value="n">{{ t(`settings.risk.questions.${key}.a${n}`) }}</option>
                </select>
              </div>
              <button data-testid="submit-questionnaire" class="prestige-btn prestige-btn-solid" :disabled="saving || !complete">{{ t('settings.risk.submit') }}</button>
            </fieldset>
          </form>
          <label for="risk-preference">{{ t('settings.risk.label') }}</label>
          <select id="risk-preference" v-model="selectedRisk" class="prestige-input" :disabled="saving">
            <option value="Conservative">{{ t('settings.risk.Conservative') }}</option>
            <option value="Balanced">{{ t('settings.risk.Balanced') }}</option>
            <option value="Aggressive">{{ t('settings.risk.Aggressive') }}</option>
          </select>
          <button data-testid="save-manual-risk" class="prestige-btn prestige-btn-solid" :disabled="saving" @click="saveRisk">
            {{ t(saving ? 'settings.risk.saving' : 'settings.risk.save') }}
          </button>
          <button v-if="!profile.isDefault" data-testid="delete-risk" class="prestige-btn" :disabled="saving" @click="removeProfile">{{ t('settings.risk.remove') }}</button>
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
fieldset { border: 1px solid var(--line); margin-top: 24px; padding: 16px; }
.risk-panel button { display: block; margin-top: 16px; }
h2 { margin: 0 0 12px; font-family: var(--serif); font-size: 22px; }
.language-description, .language-note { color: var(--muted); line-height: 1.7; }
label { display: block; margin: 28px 0 10px; }
select { width: min(100%, 320px); }
.language-note { font-size: 13px; margin: 14px 0 0; }
</style>
