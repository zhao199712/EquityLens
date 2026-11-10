<script setup lang="ts">
import { computed } from 'vue'
import type { RiskQualityEvaluation, PortfolioRiskBacktestRun } from '../../services/risk'
import { qualityStatusLabel, qualityCodeLabel } from './riskPresentation'
const props = defineProps<{
  evaluation?: RiskQualityEvaluation | null
  backtest?: PortfolioRiskBacktestRun | null
  detailed?: boolean
  loading?: boolean
  error?: string
}>()
defineEmits<{ view: []; run: []; retry: [] }>()
const model = computed(() => props.backtest?.result?.models.find(m =>
  m.model === props.evaluation?.model && m.confidenceLevel === props.evaluation?.confidenceLevel))
const dates = computed(() => model.value?.points.map(p => p.date).sort() ?? [])
const issues = computed(() => [...(props.evaluation?.failureCodes ?? []), ...(props.evaluation?.warningCodes ?? [])])
</script>

<template>
  <section class="prestige-panel quality-summary" aria-label="回測品質摘要">
    <div class="quality-heading">
      <div>
        <h2>{{ qualityStatusLabel(evaluation?.status) }}</h2>
        <p v-if="evaluation">
          {{ (evaluation.confidenceLevel * 100).toFixed(0) }}% VaR · {{ evaluation.observationCount ?? '尚無' }} 個樣本外交易日
          <template v-if="dates.length"> · 驗證期間 {{ dates[0] }} ～ {{ dates[dates.length - 1] }}</template>
          <template v-else> · {{ loading ? '讀取驗證期間中…' : '驗證期間尚未取得' }}</template>
        </p>
        <p v-else>尚未執行這筆結果的歷史回測。</p>
      </div>
      <button v-if="!detailed" class="prestige-btn" @click="$emit('view')">查看回測與方法</button>
      <button v-else-if="!evaluation || ['Error', 'Pending'].includes(evaluation.status)" class="prestige-btn" :disabled="loading" @click="$emit('run')">
        {{ loading ? '回測處理中…' : evaluation?.status === 'Pending' ? '更新回測進度' : evaluation?.status === 'Error' ? '重試歷史回測' : '執行歷史回測' }}
      </button>
    </div>
    <p v-for="code in issues" :key="code" class="quality-warning">{{ qualityCodeLabel(code) }}</p>
    <p v-if="evaluation?.status === 'Passed'" class="scope-note">僅代表本次資料通過基本檢查，不代表其他期間或信賴水準也通過。</p>
    <p v-if="error" role="alert">{{ error }} <button class="prestige-btn" :disabled="loading" @click="$emit('retry')">重新讀取</button></p>
    <template v-if="detailed && evaluation">
      <p>驗證模型：{{ evaluation.model }}</p>
      <p v-if="backtest">輸入資料期間 {{ backtest.from }} ～ {{ backtest.to }}；前 {{ backtest.lookbackDays }} 個報酬觀測用於估計，不計入驗證期間。</p>
      <dl class="quality-stats">
        <div><dt>超損次數</dt><dd>{{ model?.breachCount ?? '—' }} 次</dd></div>
        <div><dt>超損比率</dt><dd>{{ model ? `${(model.breachRate * 100).toFixed(1)}%` : '—' }}</dd></div>
        <div><dt>超損頻率檢驗（Kupiec P）</dt><dd>{{ evaluation.kupiecPValue?.toFixed(4) ?? '—' }}</dd></div>
        <div><dt>超損聚集檢驗（Christoffersen P）</dt><dd>{{ evaluation.christoffersenPValue?.toFixed(4) ?? '—' }}</dd></div>
      </dl>
      <p class="scope-note">基本檢查要求至少 100 個樣本外觀測、兩項 P 值均 ≥ 0.05，且擬合健康檢查未失敗。P 值不是準確率；此狀態不涵蓋多日 ES 聚集檢驗。</p>
    </template>
  </section>
</template>

<style scoped>
.quality-summary { padding: 20px 24px; margin: 20px 0; }
.quality-heading { display: flex; align-items: center; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
h2 { font-size: 17px; margin: 0 0 8px; }
p { color: var(--muted); font-size: 13px; line-height: 1.7; margin: 8px 0 0; }
.quality-warning { color: #d4a24e; }
.quality-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 20px; }
dt { color: var(--muted); font-size: 13px; } dd { margin: 8px 0 0; font-variant-numeric: tabular-nums; }
</style>
