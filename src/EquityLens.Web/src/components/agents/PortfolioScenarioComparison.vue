<script setup lang="ts">
import { computed } from 'vue'
export interface HoldingRisk { ticker: string; weight: number; componentVolatility: number; componentRiskShare: number }
interface ScenarioMetrics { annualizedVolatility: number; historicalVaR: number; expectedShortfall: number; hypotheticalMaxDrawdown: number; contributions: HoldingRisk[] }
interface ScenarioResult { name: string; status: string; weights: Record<string, number>; quantities: Record<string, number>; cashWeight: number; equityHhi: number; largestEquityWeight: number; metrics: ScenarioMetrics | null; differenceFromBaseline: ScenarioMetrics | null }
export interface ScenarioComparison { status: string; method: string; inputHash: string; observationCount: number; confidenceLevel: number; horizonDays: number; baseline: ScenarioResult | null; scenarios: ScenarioResult[]; warnings: string[]; snapshot: { capturedAtUtc: string; from: string; to: string; currency: string; dates: string[] } | null }
const props = defineProps<{ comparison: ScenarioComparison }>()
const columns = computed(() => [props.comparison.baseline, ...props.comparison.scenarios].filter((v): v is ScenarioResult => v !== null))
const percent = (n: number | null | undefined) => n == null ? '未提供' : `${(n * 100).toFixed(2)}%`
const delta = (n: number | null | undefined) => n == null ? '未提供' : `${n > 0 ? '+' : ''}${(n * 100).toFixed(2)} 個百分點`
// 原組合是差異的比較基準，本身沒有「與自己的差異」；不應顯示成缺資料。
const isBaseline = (column: ScenarioResult) => column === props.comparison.baseline
const deltaFor = (column: ScenarioResult, n: number | null | undefined) => isBaseline(column) ? '—（基準）' : delta(n)
</script>
<template>
  <section class="scenario" aria-label="假設配置比較">
    <h3>假設配置比較（不修改持倉）</h3>
    <p v-if="comparison.snapshot">期間 {{ comparison.snapshot.from }} ～ {{ comparison.snapshot.to }} · {{ comparison.observationCount }} 筆共同日報酬 · 快照 {{ comparison.snapshot.capturedAtUtc }} · {{ comparison.snapshot.currency }}</p>
    <p>固定權重歷史比較；差異僅相對本表原組合，不能直接與固定持股數量的風險分析比較。現金報酬為零。VaR／ES：{{ comparison.horizonDays }} 天、{{ percent(comparison.confidenceLevel) }} 信賴水準。尚未回測驗證。</p>
    <p v-if="comparison.snapshot?.dates.length">實際價格期間 {{ comparison.snapshot.dates[0] }} ～ {{ comparison.snapshot.dates[comparison.snapshot.dates.length - 1] }}（最多 253 個共同價格日）</p>
    <p v-if="comparison.status === 'Unavailable'">無法完成情境比較，請確認下方限制。</p>
    <div v-if="columns.length" class="table-scroll"><table class="prestige-table">
      <thead><tr><th>指標</th><th v-for="column in columns" :key="column.name">{{ column.name }}</th></tr></thead>
      <tbody>
        <tr><th>狀態</th><td v-for="column in columns" :key="column.name">{{ column.status === 'Calculated' ? '已計算' : '資料不足：僅顯示配置' }}</td></tr>
        <tr><th>證券權重</th><td v-for="column in columns" :key="column.name"><div v-for="(weight, ticker) in column.weights" :key="ticker">{{ ticker }}：{{ percent(weight) }}</div></td></tr>
        <tr><th>現金權重</th><td v-for="column in columns" :key="column.name">{{ percent(column.cashWeight) }}</td></tr>
        <tr><th>證券 HHI（排除現金、不重配）</th><td v-for="column in columns" :key="column.name">{{ column.equityHhi.toFixed(4) }}</td></tr>
        <tr><th>最大證券權重</th><td v-for="column in columns" :key="column.name">{{ percent(column.largestEquityWeight) }}</td></tr>
        <tr><th>年化波動率</th><td v-for="column in columns" :key="column.name">{{ percent(column.metrics?.annualizedVolatility) }}</td></tr>
        <tr><th>歷史 VaR</th><td v-for="column in columns" :key="column.name">{{ percent(column.metrics?.historicalVaR) }}</td></tr>
        <tr><th>歷史 ES</th><td v-for="column in columns" :key="column.name">{{ percent(column.metrics?.expectedShortfall) }}</td></tr>
        <tr><th>假設報酬序列回撤</th><td v-for="column in columns" :key="column.name">{{ percent(column.metrics?.hypotheticalMaxDrawdown) }}</td></tr>
        <tr><th>波動率差異（情境 − 原組合）</th><td v-for="column in columns" :key="column.name">{{ deltaFor(column, column.differenceFromBaseline?.annualizedVolatility) }}</td></tr>
        <tr><th>VaR 差異</th><td v-for="column in columns" :key="column.name">{{ deltaFor(column, column.differenceFromBaseline?.historicalVaR) }}</td></tr>
        <tr><th>ES 差異</th><td v-for="column in columns" :key="column.name">{{ deltaFor(column, column.differenceFromBaseline?.expectedShortfall) }}</td></tr>
        <tr><th>回撤差異</th><td v-for="column in columns" :key="column.name">{{ deltaFor(column, column.differenceFromBaseline?.hypotheticalMaxDrawdown) }}</td></tr>
      </tbody>
    </table></div>
    <details v-for="column in columns" :key="column.name"><summary>{{ column.name }} · 逐股風險貢獻</summary>
      <p v-if="!column.metrics">資料不足，未計算風險貢獻。</p>
      <div v-else class="table-scroll"><table class="prestige-table"><thead><tr><th>股票</th><th>年化波動貢獻</th><th>風險占比</th></tr></thead><tbody><tr v-for="item in column.metrics.contributions" :key="item.ticker"><td>{{ item.ticker }}</td><td>{{ percent(item.componentVolatility) }}</td><td>{{ percent(item.componentRiskShare) }}</td></tr></tbody></table></div>
    </details>
    <ul><li v-for="warning in comparison.warnings" :key="warning">{{ warning }}</li></ul>
    <details><summary>計算依據</summary><p>{{ comparison.method }}</p><p>輸入雜湊：{{ comparison.inputHash || '未提供' }}</p></details>
  </section>
</template>
<style scoped>
.scenario { margin:24px 0; border-top:1px solid #ffffff20; padding-top:16px; color:#d8d0bf; overflow-wrap:anywhere; }
.scenario p,.scenario li { font-size:13px; line-height:1.7; color:#a59b87; }
.table-scroll { overflow-x:auto; } th,td { min-width:130px; vertical-align:top; padding:12px; } details { margin:12px 0; } summary { cursor:pointer; color:#c9a86a; }
</style>
