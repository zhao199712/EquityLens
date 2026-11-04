<script setup lang="ts">
import type { RiskMeasurementBasis } from '../../services/risk'
defineProps<{ basis?: RiskMeasurementBasis | null }>()
</script>
<template>
  <p v-if="basis" class="body-text">
    期間 {{ basis.from ?? '未知' }} ～ {{ basis.to ?? '未知' }}；{{ basis.weighting }}；{{ basis.cashTreatment }}。
    {{ basis.returnMethod }}；{{ basis.volatilityMethod }}。
    樣本 {{ basis.observationCount ?? '未知' }}：{{ basis.sampleDefinition }}。
    <span v-if="basis.confidenceLevel != null">信心水準 {{ basis.confidenceLevel * 100 }}%；</span>
    <span v-if="basis.horizonDays != null">持有期間 {{ basis.horizonDays }} 日。</span>
  </p>
  <p v-else class="body-text">此結果未保存完整計算口徑，不能直接與其他頁面或情境數字比較。</p>
</template>
