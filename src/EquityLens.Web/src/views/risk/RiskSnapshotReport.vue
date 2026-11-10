<script setup lang="ts">
import { computed } from 'vue'
import type { PortfolioRiskReportSnapshotDetail } from '../../services/risk'
import { riskPercent, qualityStatusLabel, qualityCodeLabel } from './riskPresentation'
const props = defineProps<{ report: PortfolioRiskReportSnapshotDetail }>()
defineEmits<{ close: [] }>()
type ObjectValue = Record<string, unknown>
const object = (value: unknown): ObjectValue => value && typeof value === 'object' && !Array.isArray(value) ? value as ObjectValue : {}
const list = (value: unknown): ObjectValue[] => Array.isArray(value) ? value.map(object) : []
const number = (value: unknown): number | null => typeof value === 'number' && Number.isFinite(value) ? value : null
const text = (value: unknown) => typeof value === 'string' ? value : '—'
// Older full reports used PascalCase for projected entity fields.
function normalize(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(normalize)
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k, v]) => [k[0]!.toLowerCase() + k.slice(1), normalize(v)]))
  return value
}
const snapshot = computed(() => object(normalize(props.report.snapshot)))
const risk = computed(() => object(snapshot.value.risk))
const source = computed(() => object(snapshot.value.source))
const input = computed(() => object(source.value.input))
const quality = computed(() => object(snapshot.value.qualityEvaluation))
const holdings = computed(() => list(object(risk.value.conditionalRisk).holdings ?? risk.value.holdings ?? snapshot.value.holdings))
const horizons = computed(() => list(risk.value.horizons).flatMap(h => {
  const levels = list(h.confidenceLevels)
  return levels.length ? levels.map(c => ({ days: h.horizonDays, confidence: number(c.confidenceLevel), var: number(c.var), es: number(c.expectedShortfall) }))
    : [{ days: h.horizonDays, confidence: number(risk.value.confidenceLevel), var: number(h.monteCarloVaR), es: number(h.monteCarloES) }]
}))
const warnings = computed(() => [quality.value.failureCodes, quality.value.warningCodes].flatMap(value => Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : []))
const legacyBacktests = computed(() => list(snapshot.value.backtest))
const stress = computed(() => list(snapshot.value.stress))
function downloadJson() {
  const url = URL.createObjectURL(new Blob([JSON.stringify(props.report.snapshot, null, 2)], { type: 'application/json' }))
  const a = document.createElement('a'); a.href = url; a.download = `risk-report-${props.report.id}.json`; a.click()
  URL.revokeObjectURL(url)
}
function print() { window.print() }
</script>

<template>
  <article class="prestige-panel saved-risk-report" aria-label="已保存的風險報告">
    <header>
      <div><h2>風險報告快照</h2><p>保存時間 {{ report.createdAtUtc ? new Date(report.createdAtUtc).toLocaleString('zh-TW') : '—' }} · 資料截止 {{ report.dataAsOfDate ?? '—' }}</p></div>
      <div class="report-actions"><button class="prestige-btn" @click="print">列印／另存 PDF</button><button class="prestige-btn" @click="$emit('close')">關閉報告</button></div>
    </header>
    <p>以下為保存當時的資料，不會隨目前持倉或行情改變。</p>
    <p v-if="snapshot.scope">{{ text(snapshot.scope) }}</p>
    <p>模型：{{ report.model }} · {{ qualityStatusLabel(report.overallStatus) }}</p>
    <p v-if="input.from || input.to">輸入資料期間 {{ text(input.from) }} ～ {{ text(input.to) }}</p>
    <p v-if="source.fallbackReason" class="warning">本次使用替代模型：{{ text(source.fallbackReason) }}</p>
    <p v-for="code in warnings" :key="code" class="warning">{{ qualityCodeLabel(code) }}</p>
    <dl class="report-metrics">
      <div><dt>歷史年化波動率</dt><dd>{{ riskPercent(number(risk.historicalAnnualizedVolatility ?? risk.annualizedVolatility)) }}</dd></div>
      <div><dt>歷史最大回撤</dt><dd>{{ riskPercent(number(risk.maxDrawdown)) }}</dd></div>
      <div><dt>樣本外回測觀測</dt><dd>{{ number(quality.observationCount) ?? '—' }} 個交易日</dd></div>
    </dl>
    <div v-if="horizons.length" class="report-table">
      <h3>損失估計</h3>
      <table class="prestige-table"><thead><tr><th>持有期間</th><th>信賴水準</th><th>損失門檻 VaR</th><th>超過門檻時平均損失 ES</th></tr></thead>
        <tbody><tr v-for="(h, i) in horizons" :key="i"><td>{{ h.days }} 日</td><td>{{ riskPercent(h.confidence, 0) }}</td><td>{{ riskPercent(h.var) }}</td><td>{{ riskPercent(h.es) }}</td></tr></tbody>
      </table>
    </div>
    <div v-if="holdings.length" class="report-table">
      <h3>持股風險來源</h3>
      <table class="prestige-table"><thead><tr><th>持股</th><th>持倉比重</th><th>風險占比</th></tr></thead>
        <tbody><tr v-for="(h, i) in holdings" :key="i"><td>{{ text(h.ticker) }} · {{ text(h.securityName) }}</td><td>{{ riskPercent(number(h.weight), 1) }}</td><td>{{ riskPercent(number(h.componentRiskShare), 1) }}</td></tr></tbody>
      </table>
    </div>
    <section v-if="quality.status">
      <h3>保存當時的回測結果</h3>
      <p>{{ riskPercent(number(quality.confidenceLevel), 0) }} VaR · 頻率檢驗 P = {{ number(quality.kupiecPValue)?.toFixed(4) ?? '—' }} · 聚集檢驗 P = {{ number(quality.christoffersenPValue)?.toFixed(4) ?? '—' }}</p>
      <p>此快照未保存逐日回測日期，不能由輸入期間推定實際驗證期間。通過僅適用於當次檢查範圍。</p>
    </section>
    <div v-if="legacyBacktests.length" class="report-table">
      <h3>歷史回測</h3>
      <table class="prestige-table"><thead><tr><th>模型</th><th>水準</th><th>觀測數</th><th>超損次數</th><th>頻率檢驗 P</th><th>聚集檢驗 P</th></tr></thead>
        <tbody><tr v-for="(b, i) in legacyBacktests" :key="i"><td>{{ text(b.model) }}</td><td>{{ riskPercent(number(b.confidenceLevel), 0) }}</td><td>{{ b.observationCount }}</td><td>{{ b.breachCount }}</td><td>{{ number(b.kupiecPValue)?.toFixed(4) ?? '—' }}</td><td>{{ number(b.christoffersenPValue)?.toFixed(4) ?? '—' }}</td></tr></tbody>
      </table>
    </div>
    <section v-if="stress.length"><h3>壓力情境</h3><p v-for="(s, i) in stress" :key="i">{{ text(s.name) }}：{{ riskPercent(number(s.totalImpact)) }}</p></section>
    <details class="report-technical"><summary>報告識別與匯出</summary><p>模型版本 {{ text(source.algorithmVersion) }} · 輸入識別 {{ text(source.inputHash) }} · 報告編號 {{ report.id }}</p><button class="prestige-btn" @click="downloadJson">下載原始 JSON</button></details>
  </article>
</template>

<style scoped>
.saved-risk-report { padding: 28px; margin: 24px 0; }
header { display: flex; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
h2 { margin: 0; font-size: 24px; } h3 { margin: 24px 0 12px; font-size: 17px; }
p, dt { color: var(--muted); font-size: 13px; line-height: 1.8; overflow-wrap: anywhere; }
.report-actions { display: flex; gap: 8px; align-items: flex-start; flex-wrap: wrap; }
.report-metrics { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 24px; margin: 24px 0; }
dd { margin: 8px 0; font-size: 24px; font-variant-numeric: tabular-nums; }
.report-table { overflow-x: auto; } th, td { white-space: nowrap; }
.warning { color: #d4a24e; } summary { cursor: pointer; color: var(--gold); padding: 20px 0; }
@media print {
  @page { margin: 14mm; }
  .saved-risk-report { border: 0; background: white; color: black; padding: 0; margin: 0; }
  h2, h3 { color: #111 !important; }
  p, dt { color: #333; } .report-actions, .report-technical { display: none; }
  .report-table { overflow: visible; } table { color: black; width: 100%; } th, td { white-space: normal; color: black; }
}
</style>
