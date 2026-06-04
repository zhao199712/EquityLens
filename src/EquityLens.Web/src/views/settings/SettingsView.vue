<script setup lang="ts">
import { ref } from 'vue'
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
} from '@vicons/ionicons5'

// Risk Model Settings
const riskSettings = ref({
  confidenceLevel: 95,
  lookbackWindow: 252,
  returnModel: 'historical',
  useBootstrap: false,
  bootstrapSamples: 1000,
})

const returnModelOptions = [
  { label: '歷史模擬法', value: 'historical' },
  { label: '參數法 (常態分配)', value: 'parametric' },
  { label: '蒙地卡羅模擬', value: 'monte-carlo' },
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
  { label: '即時', value: 'realtime' },
  { label: '每小時', value: 'hourly' },
  { label: '每日', value: 'daily' },
]

const saving = ref(false)
const saveSuccess = ref(false)

async function handleSave() {
  saving.value = true
  // Simulate API call
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
        <p class="eyebrow">System Settings</p>
        <h1>設定</h1>
      </div>
      <NSpace>
        <NButton size="small" @click="handleReset">
          <template #icon>
            <NIcon><RefreshOutline /></NIcon>
          </template>
          重設預設值
        </NButton>
        <NButton type="primary" class="btn-primary" size="small" :loading="saving" @click="handleSave">
          <template #icon>
            <NIcon><SaveOutline /></NIcon>
          </template>
          儲存設定
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
      設定已成功儲存
    </NAlert>

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
          <h2 style="margin: 0;">風險模型設定</h2>
          <p style="color: var(--text-secondary); font-size: 13px; margin: 4px 0 0;">配置 VaR / ES 計算參數與模型</p>
        </div>
      </div>

      <NForm label-placement="left" label-width="180px">
        <NFormItem label="置信水準">
          <div style="width: 100%;">
            <NSlider v-model:value="riskSettings.confidenceLevel" :min="90" :max="99" :step="1" :marks="{90: '90%', 95: '95%', 99: '99%'}" />
            <div style="color: var(--text-secondary); font-size: 13px; margin-top: 8px;">
              當前設定: <strong style="color: var(--accent-primary);">{{ riskSettings.confidenceLevel }}%</strong>
            </div>
          </div>
        </NFormItem>

        <NFormItem label="回顧期間 (交易日)">
          <NInputNumber
            v-model:value="riskSettings.lookbackWindow"
            :min="30"
            :max="1000"
            style="max-width: 200px;"
          >
            <template #suffix>日</template>
          </NInputNumber>
        </NFormItem>

        <NFormItem label="報酬率模型">
          <NRadioGroup v-model:value="riskSettings.returnModel">
            <NSpace>
              <NRadio v-for="option in returnModelOptions" :key="option.value" :value="option.value">
                {{ option.label }}
              </NRadio>
            </NSpace>
          </NRadioGroup>
        </NFormItem>

        <NFormItem label="啟用 Bootstrap">
          <NSwitch v-model:value="riskSettings.useBootstrap" />
        </NFormItem>

        <NFormItem v-if="riskSettings.useBootstrap" label="Bootstrap 樣本數">
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
          <h2 style="margin: 0;">AI 分析設定</h2>
          <p style="color: var(--text-secondary); font-size: 13px; margin: 4px 0 0;">配置 AI 模型參數與報告生成選項</p>
        </div>
      </div>

      <NForm label-placement="left" label-width="180px">
        <NFormItem label="AI 模型">
          <NSelect
            v-model:value="aiSettings.model"
            :options="modelOptions"
            style="max-width: 300px;"
          />
        </NFormItem>

        <NFormItem label="Temperature">
          <div style="width: 100%;">
            <NSlider v-model:value="aiSettings.temperature" :min="0" :max="1" :step="0.1" :marks="{0: '精確', 0.5: '平衡', 1: '創意'}" />
            <div style="color: var(--text-secondary); font-size: 13px; margin-top: 8px;">
              當前設定: <strong style="color: var(--accent-primary);">{{ aiSettings.temperature }}</strong>
              <span style="margin-left: 8px;">
                {{ aiSettings.temperature < 0.3 ? '(較精確、保守)' : aiSettings.temperature > 0.7 ? '(較有創意)' : '(平衡)' }}
              </span>
            </div>
          </div>
        </NFormItem>

        <NFormItem label="最大 Token 數">
          <NInputNumber
            v-model:value="aiSettings.maxTokens"
            :min="1000"
            :max="8000"
            :step="500"
            style="max-width: 200px;"
          />
        </NFormItem>

        <NFormItem label="要求資料來源引用">
          <NSwitch v-model:value="aiSettings.requireCitations" />
        </NFormItem>

        <NFormItem label="啟用批判檢視">
          <NSwitch v-model:value="aiSettings.criticEnabled" />
        </NFormItem>

        <NFormItem label="自動生成報告">
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
          <h2 style="margin: 0;">資料來源設定</h2>
          <p style="color: var(--text-secondary); font-size: 13px; margin: 4px 0 0;">配置市場資料與財報資料來源</p>
        </div>
      </div>

      <NForm label-placement="left" label-width="180px">
        <NFormItem label="股價資料來源">
          <NSelect
            v-model:value="dataSettings.priceSource"
            :options="priceSourceOptions"
            style="max-width: 300px;"
          />
        </NFormItem>

        <NFormItem label="自動更新股價">
          <NSwitch v-model:value="dataSettings.autoUpdatePrices" />
        </NFormItem>

        <NFormItem label="更新頻率">
          <NSelect
            v-model:value="dataSettings.updateFrequency"
            :options="updateFrequencyOptions"
            style="max-width: 200px;"
            :disabled="!dataSettings.autoUpdatePrices"
          />
        </NFormItem>

        <NFormItem label="快取時間 (小時)">
          <NInputNumber
            v-model:value="dataSettings.cacheDuration"
            :min="1"
            :max="168"
            style="max-width: 200px;"
          >
            <template #suffix>小時</template>
          </NInputNumber>
        </NFormItem>
      </NForm>

      <div class="glass-divider"></div>

      <NAlert type="info" :show-icon="true">
        <template #icon>
          <NIcon><InformationCircleOutline /></NIcon>
        </template>
        資料來源變更將在下次資料更新時生效。手動更新請前往「市場價格管理」頁面。
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
