<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { setLocale } from '../../locales'
import {
  NButton,
  NForm,
  NFormItem,
  NInputNumber,
  NSelect,
  NSwitch,
  NIcon,
  NSpace,
  NAlert,
  NRadio,
  NRadioGroup,
  NSlider,
} from 'naive-ui'
import {
  ShieldCheckmarkOutline,
  SparklesOutline,
  ServerOutline,
  SaveOutline,
  RefreshOutline,
  CheckmarkCircleOutline,
  InformationCircleOutline,
  LanguageOutline,
} from '@vicons/ionicons5'

const { t, locale } = useI18n()

const languageOptions = [
  { label: 'English', value: 'en' },
  { label: '繁體中文', value: 'zh-TW' },
]

function handleLanguageChange(val: string) {
  setLocale(val)
}

// Risk Model Settings
const riskSettings = ref({
  confidenceLevel: 95,
  lookbackWindow: 252,
  returnModel: 'historical',
  useBootstrap: false,
  bootstrapSamples: 1000,
})

const returnModelOptions = [
  { label: () => t('settings.risk.returnModelHistorical'), value: 'historical' },
  { label: () => t('settings.risk.returnModelParametric'), value: 'parametric' },
  { label: () => t('settings.risk.returnModelMonteCarlo'), value: 'monte-carlo' },
]

// AI Settings
const aiSettings = ref({
  model: 'gpt-4',
  temperature: 0.7,
  maxTokens: 4000,
  requireCitations: true,
  autoGenerateReports: false,
  criticEnabled: true,
})

const modelOptions = [
  { label: 'GPT-4', value: 'gpt-4' },
  { label: 'GPT-4 Turbo', value: 'gpt-4-turbo' },
  { label: 'GPT-3.5 Turbo', value: 'gpt-3.5-turbo' },
]

// Data Source Settings
const dataSettings = ref({
  priceSource: 'yahoo',
  reportSource: 'sec',
  autoUpdatePrices: true,
  updateFrequency: 'daily',
  cacheDuration: 24,
})

const priceSourceOptions = [
  { label: 'Yahoo Finance', value: 'yahoo' },
  { label: 'Alpha Vantage', value: 'alpha-vantage' },
  { label: 'Bloomberg API', value: 'bloomberg' },
]

const updateFrequencyOptions = [
  { label: () => t('settings.data.realtime'), value: 'realtime' },
  { label: () => t('settings.data.hourly'), value: 'hourly' },
  { label: () => t('settings.data.daily'), value: 'daily' },
]

const saving = ref(false)
const saveSuccess = ref(false)

async function handleSave() {
  saving.value = true
  await new Promise(resolve => setTimeout(resolve, 800))
  saving.value = false
  saveSuccess.value = true
  setTimeout(() => saveSuccess.value = false, 3000)
}

function handleReset() {
  riskSettings.value = {
    confidenceLevel: 95,
    lookbackWindow: 252,
    returnModel: 'historical',
    useBootstrap: false,
    bootstrapSamples: 1000,
  }
  aiSettings.value = {
    model: 'gpt-4',
    temperature: 0.7,
    maxTokens: 4000,
    requireCitations: true,
    autoGenerateReports: false,
    criticEnabled: true,
  }
  dataSettings.value = {
    priceSource: 'yahoo',
    reportSource: 'sec',
    autoUpdatePrices: true,
    updateFrequency: 'daily',
    cacheDuration: 24,
  }
}
</script>

<template>
  <main class="page animate-fade-in">
    <!-- Header -->
    <section class="page-heading">
      <div>
        <p class="eyebrow">{{ t('settings.pageEyebrow') }}</p>
        <h1>{{ t('settings.pageTitle') }}</h1>
      </div>
      <NSpace>
        <NButton size="small" @click="handleReset">
          <template #icon>
            <NIcon><RefreshOutline /></NIcon>
          </template>
          {{ t('settings.resetDefaults') }}
        </NButton>
        <NButton type="primary" class="btn-primary" size="small" :loading="saving" @click="handleSave">
          <template #icon>
            <NIcon><SaveOutline /></NIcon>
          </template>
          {{ t('settings.saveSettings') }}
        </NButton>
      </NSpace>
    </section>

    <!-- Save Success Alert -->
    <NAlert
      v-if="saveSuccess"
      type="success"
      :show-icon="true"
      style="margin-bottom: 24px;"
    >
      <template #icon>
        <NIcon><CheckmarkCircleOutline /></NIcon>
      </template>
      {{ t('settings.saveSuccess') }}
    </NAlert>

    <!-- Language Settings -->
    <div class="glass-panel" style="margin-bottom: 24px;">
      <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 24px;">
        <div
          style="
            width: 40px;
            height: 40px;
            border-radius: 10px;
            display: grid;
            place-items: center;
            background: rgba(251, 191, 36, 0.15);
          "
        >
          <NIcon :size="20" color="#fbbf24"><LanguageOutline /></NIcon>
        </div>
        <div>
          <h2 style="margin: 0;">{{ t('settings.language.title') }}</h2>
          <p style="color: var(--text-secondary); font-size: 13px; margin: 4px 0 0;">{{ t('settings.language.description') }}</p>
        </div>
      </div>

      <NForm label-placement="left" label-width="180px">
        <NFormItem :label="t('settings.language.label')">
          <NSelect
            :value="locale"
            :options="languageOptions"
            style="max-width: 300px;"
            @update:value="handleLanguageChange"
          />
        </NFormItem>
      </NForm>
    </div>

    <!-- Risk Model Settings -->
    <div class="glass-panel" style="margin-bottom: 24px;">
      <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 24px;">
        <div
          style="
            width: 40px;
            height: 40px;
            border-radius: 10px;
            display: grid;
            place-items: center;
            background: rgba(96, 165, 250, 0.15);
          "
        >
          <NIcon :size="20" color="#60a5fa"><ShieldCheckmarkOutline /></NIcon>
        </div>
        <div>
          <h2 style="margin: 0;">{{ t('settings.risk.title') }}</h2>
          <p style="color: var(--text-secondary); font-size: 13px; margin: 4px 0 0;">{{ t('settings.risk.description') }}</p>
        </div>
      </div>

      <NForm label-placement="left" label-width="180px">
        <NFormItem :label="t('settings.risk.confidenceLevel')">
          <div style="width: 100%;">
            <NSlider v-model:value="riskSettings.confidenceLevel" :min="90" :max="99" :step="1" :marks="{90: '90%', 95: '95%', 99: '99%'}" />
            <div style="color: var(--text-secondary); font-size: 13px; margin-top: 8px;">
              {{ t('settings.risk.currentSetting') }}: <strong style="color: var(--accent-primary);">{{ riskSettings.confidenceLevel }}%</strong>
            </div>
          </div>
        </NFormItem>

        <NFormItem :label="t('settings.risk.lookbackWindow')">
          <NInputNumber
            v-model:value="riskSettings.lookbackWindow"
            :min="30"
            :max="1000"
            style="max-width: 200px;"
          >
            <template #suffix>{{ t('settings.risk.days') }}</template>
          </NInputNumber>
        </NFormItem>

        <NFormItem :label="t('settings.risk.returnModel')">
          <NRadioGroup v-model:value="riskSettings.returnModel">
            <NSpace>
              <NRadio v-for="option in returnModelOptions" :key="option.value" :value="option.value">
                {{ option.label() }}
              </NRadio>
            </NSpace>
          </NRadioGroup>
        </NFormItem>

        <NFormItem :label="t('settings.risk.enableBootstrap')">
          <NSwitch v-model:value="riskSettings.useBootstrap" />
        </NFormItem>

        <NFormItem v-if="riskSettings.useBootstrap" :label="t('settings.risk.bootstrapSamples')">
          <NInputNumber
            v-model:value="riskSettings.bootstrapSamples"
            :min="100"
            :max="10000"
            :step="100"
            style="max-width: 200px;"
          />
        </NFormItem>
      </NForm>
    </div>

    <!-- AI Settings -->
    <div class="glass-panel" style="margin-bottom: 24px;">
      <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 24px;">
        <div
          style="
            width: 40px;
            height: 40px;
            border-radius: 10px;
            display: grid;
            place-items: center;
            background: rgba(192, 132, 252, 0.15);
          "
        >
          <NIcon :size="20" color="#c084fc"><SparklesOutline /></NIcon>
        </div>
        <div>
          <h2 style="margin: 0;">{{ t('settings.ai.title') }}</h2>
          <p style="color: var(--text-secondary); font-size: 13px; margin: 4px 0 0;">{{ t('settings.ai.description') }}</p>
        </div>
      </div>

      <NForm label-placement="left" label-width="180px">
        <NFormItem :label="t('settings.ai.model')">
          <NSelect
            v-model:value="aiSettings.model"
            :options="modelOptions"
            style="max-width: 300px;"
          />
        </NFormItem>

        <NFormItem label="Temperature">
          <div style="width: 100%;">
            <NSlider v-model:value="aiSettings.temperature" :min="0" :max="1" :step="0.1" :marks="{0: t('settings.ai.precise'), 0.5: t('settings.ai.balanced'), 1: t('settings.ai.creative')}" />
            <div style="color: var(--text-secondary); font-size: 13px; margin-top: 8px;">
              {{ t('settings.ai.currentSetting') }}: <strong style="color: var(--accent-primary);">{{ aiSettings.temperature }}</strong>
              <span style="margin-left: 8px;">
                {{ aiSettings.temperature < 0.3 ? t('settings.ai.tempPrecise') : aiSettings.temperature > 0.7 ? t('settings.ai.tempCreative') : t('settings.ai.tempBalanced') }}
              </span>
            </div>
          </div>
        </NFormItem>

        <NFormItem :label="t('settings.ai.maxTokens')">
          <NInputNumber
            v-model:value="aiSettings.maxTokens"
            :min="1000"
            :max="8000"
            :step="500"
            style="max-width: 200px;"
          />
        </NFormItem>

        <NFormItem :label="t('settings.ai.requireCitations')">
          <NSwitch v-model:value="aiSettings.requireCitations" />
        </NFormItem>

        <NFormItem :label="t('settings.ai.enableCritic')">
          <NSwitch v-model:value="aiSettings.criticEnabled" />
        </NFormItem>

        <NFormItem :label="t('settings.ai.autoGenerateReports')">
          <NSwitch v-model:value="aiSettings.autoGenerateReports" />
        </NFormItem>
      </NForm>
    </div>

    <!-- Data Source Settings -->
    <div class="glass-panel">
      <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 24px;">
        <div
          style="
            width: 40px;
            height: 40px;
            border-radius: 10px;
            display: grid;
            place-items: center;
            background: rgba(52, 211, 153, 0.15);
          "
        >
          <NIcon :size="20" color="#34d399"><ServerOutline /></NIcon>
        </div>
        <div>
          <h2 style="margin: 0;">{{ t('settings.data.title') }}</h2>
          <p style="color: var(--text-secondary); font-size: 13px; margin: 4px 0 0;">{{ t('settings.data.description') }}</p>
        </div>
      </div>

      <NForm label-placement="left" label-width="180px">
        <NFormItem :label="t('settings.data.priceSource')">
          <NSelect
            v-model:value="dataSettings.priceSource"
            :options="priceSourceOptions"
            style="max-width: 300px;"
          />
        </NFormItem>

        <NFormItem :label="t('settings.data.autoUpdatePrices')">
          <NSwitch v-model:value="dataSettings.autoUpdatePrices" />
        </NFormItem>

        <NFormItem :label="t('settings.data.updateFrequency')">
          <NSelect
            v-model:value="dataSettings.updateFrequency"
            :options="updateFrequencyOptions"
            style="max-width: 200px;"
            :disabled="!dataSettings.autoUpdatePrices"
          />
        </NFormItem>

        <NFormItem :label="t('settings.data.cacheDuration')">
          <NInputNumber
            v-model:value="dataSettings.cacheDuration"
            :min="1"
            :max="168"
            style="max-width: 200px;"
          >
            <template #suffix>{{ t('settings.data.hours') }}</template>
          </NInputNumber>
        </NFormItem>
      </NForm>

      <div class="glass-divider"></div>

      <NAlert type="info" :show-icon="true">
        <template #icon>
          <NIcon><InformationCircleOutline /></NIcon>
        </template>
        {{ t('settings.data.dataChangeNote') }}
      </NAlert>
    </div>
  </main>
</template>

<style scoped>
:deep(.n-form-item-label) {
  color: var(--text-secondary) !important;
  font-weight: 500;
}

:deep(.n-radio__label) {
  color: var(--text-secondary) !important;
}

:deep(.n-slider) {
  --n-fill-color: var(--accent-primary) !important;
}

:deep(.n-switch.n-switch--active) {
  background: var(--accent-primary) !important;
}
</style>
