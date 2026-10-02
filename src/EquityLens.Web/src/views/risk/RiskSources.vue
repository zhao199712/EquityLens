<script setup lang="ts">
import { computed } from 'vue'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import type { ConditionalRisk, PortfolioRiskResponse } from '../../services/risk'
import { riskPercent } from './riskPresentation'

const props = defineProps<{ conditional?: ConditionalRisk | null; fallback?: PortfolioRiskResponse | null }>()
const holdingsRisk = computed(() => props.conditional?.holdings ?? props.fallback?.holdings ?? [])
const industriesRisk = computed(() => props.conditional?.industries ?? props.fallback?.industries ?? [])
const marginalPoints = (value: number | null) => value == null ? '—' : `${value.toFixed(2)} 百分點`
</script>

<template>
  <p v-if="conditional" class="table-note conditional-note">
    下一交易日加權對數報酬的條件波動率，以 √252 換算年化尺度；不是一年期預測，亦不等同 FHS 簡單報酬模擬的標準差。
  </p>
  <ScrollReveal v-if="holdingsRisk.length" class="mt-20">
    <div class="prestige-panel" style="margin-bottom: 40px">
      <div class="section-head">
        <h2 class="panel-title">持倉風險摘要</h2>
        <span class="prestige-label">{{ conditional ? 'VT-GARCH 條件波動率分解' : 'C# MVEWMA-FHS 回退資料' }}</span>
      </div>
      <div class="section-body table-wrap">
        <div class="holdings-headline">
          風險來源模型年化波動率：<span class="prestige-mono hl">{{ riskPercent(conditional?.annualizedVolatility ?? fallback?.riskSourceAnnualizedVolatility) }}</span>
        </div>
        <table class="prestige-table">
          <thead>
            <tr>
              <th>代號</th>
              <th>名稱</th>
              <th>產業</th>
              <th style="text-align: right">權重</th>
              <th style="text-align: right">{{ conditional ? '條件波動率（年化）' : '年化波動率' }}</th>
              <th style="text-align: right">Component</th>
              <th style="text-align: right">風險占比</th>
              <th style="text-align: right">Marginal（權重 +1 百分點）</th>
              <th style="text-align: right">Incremental</th>
              <th style="text-align: right">資料筆數</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="h in holdingsRisk" :key="h.securityId">
              <td>{{ h.ticker }}</td>
              <td>{{ h.securityName }}</td>
              <td class="td-muted">{{ h.industry }}</td>
              <td class="prestige-mono td-num">{{ (h.weight * 100).toFixed(2) }}%</td>
              <td class="prestige-mono td-num" :class="{ 'td-warn': h.annualizedVolatility > 0.3 }">{{ (h.annualizedVolatility * 100).toFixed(2) }}%</td>
              <td class="prestige-mono td-num">{{ riskPercent(h.componentVolatility) }}</td>
              <td class="prestige-mono td-num" :class="(h.componentRiskShare ?? 0) < 0 ? 'td-pos' : 'td-danger'">{{ riskPercent(h.componentRiskShare, 1) }}</td>
              <td class="prestige-mono td-num">{{ marginalPoints(h.marginalVolatility) }}</td>
              <td class="prestige-mono td-num">{{ (h.incrementalVolatility * 100).toFixed(2) }} 百分點</td>
              <td class="prestige-mono td-num">{{ h.dataPointCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="table-note" style="padding: 0 24px 20px; margin-top: 0">
        Component 可加總為上方的風險來源模型年化波動率；Marginal 為從現金增加權重 1 個百分點的年化波動率近似變化；Incremental 假設移除部位後轉為現金。負值代表分散效果。
      </p>
    </div>
  </ScrollReveal>

  <ScrollReveal class="mt-20" v-if="industriesRisk.length">
    <div class="prestige-panel" style="margin-bottom: 40px">
      <div class="section-head">
        <h2 class="panel-title">產業風險來源</h2>
        <span class="prestige-label">{{ conditional ? 'VT-GARCH 條件波動率分解' : 'C# MVEWMA-FHS 回退資料' }}</span>
      </div>
      <p v-if="conditional" class="table-note conditional-note">產業 Marginal 依產業內原權重比例增加合計 1 個百分點；Incremental 為整個產業轉為現金。</p>
      <p v-else class="table-note conditional-note">C# 產業 Marginal 為各持倉邊際值加總，表示產業內每個持倉各增加 1 個百分點。</p>
      <div class="section-body table-wrap">
        <table class="prestige-table">
          <thead>
            <tr>
              <th>產業</th>
              <th style="text-align: right">持倉數</th>
              <th style="text-align: right">權重</th>
              <th style="text-align: right">Component</th>
              <th style="text-align: right">風險占比</th>
              <th style="text-align: right">Marginal（權重 +1 百分點）</th>
              <th style="text-align: right">Incremental</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="industry in industriesRisk" :key="industry.industry">
              <td>{{ industry.industry }}</td>
              <td class="prestige-mono td-num">{{ industry.holdingCount }}</td>
              <td class="prestige-mono td-num">{{ (industry.weight * 100).toFixed(2) }}%</td>
              <td class="prestige-mono td-num">{{ riskPercent(industry.componentVolatility) }}</td>
              <td class="prestige-mono td-num" :class="(industry.componentRiskShare ?? 0) < 0 ? 'td-pos' : 'td-danger'">{{ riskPercent(industry.componentRiskShare, 1) }}</td>
              <td class="prestige-mono td-num">{{ marginalPoints(industry.marginalVolatility) }}</td>
              <td class="prestige-mono td-num">{{ (industry.incrementalVolatility * 100).toFixed(2) }} 百分點</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </ScrollReveal>

</template>

<style scoped>
.conditional-note { padding: 0 24px 16px; }
.mt-20 {
  margin-top: 40px;
}
.panel-title {
  margin: 0 0 6px;
  font-family: var(--serif);
  font-size: 20px;
  font-weight: 600;
}
.section-head {
  padding: 20px 24px;
  border-bottom: 1px solid var(--gold-border-soft);
}
.section-body {
  padding: 24px;
}
.table-wrap {
  overflow-x: auto;
}
.prestige-table th,
.prestige-table td {
  white-space: nowrap;
}
.td-num {
  text-align: right;
}
.td-muted {
  color: var(--muted);
}
.td-warn {
  color: #d4a24e;
}
.td-danger {
  color: var(--down);
}
.td-pos {
  color: var(--up);
}
.table-note {
  font-size: 11px;
  color: var(--muted);
  margin: 12px 0 0;
}
.holdings-headline {
  margin-bottom: 16px;
  color: var(--ivory);
  font-size: 14px;
}
.hl {
  color: var(--gold);
  font-weight: 600;
}
@media (max-width: 767px) {
  .section-head, .section-body { padding: 16px; }
}
</style>
