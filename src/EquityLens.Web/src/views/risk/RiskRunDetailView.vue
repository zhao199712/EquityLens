<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ScrollReveal from '../../components/kimi/ScrollReveal.vue'
import Footer from '../../components/kimi/Footer.vue'
import {
  getPortfolio,
  getPortfolioRisk,
  type PortfolioRiskResponse,
} from '../../services/risk.ts'
import {
  varHistogram,
  varEsComparison,
  stressScenarios,
  backtestData,
  monteCarloPaths,
  monteCarloPercentiles,
  mcStats,
  severityColor,
  severityLabel,
} from '../../data/riskKimiData.ts'

const router = useRouter()
const route = useRoute()
const portfolioId = computed(() => String(route.params.id))

const portfolioName = ref('投資組合風險分析')
const risk = ref<PortfolioRiskResponse | null>(null)
const loading = ref(false)
const error = ref('')

const selectedScenario = ref<string | null>(null)
const displayedPaths = monteCarloPaths.slice(0, 8)

const today = new Date()
const oneYearAgo = new Date(today.getFullYear() - 1, today.getMonth(), today.getDate())
const fromDate = computed(() => oneYearAgo.toISOString().split('T')[0])
const toDate = computed(() => today.toISOString().split('T')[0])

const runInfo = computed(() => {
  const r = risk.value
  const modelName =
    r?.covarianceMethod === 'MultivariateEWMA'
      ? 'MVEWMA-FHS'
      : 'Historical VaR + Monte Carlo'
  return {
    id: `RR-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}-001`,
    portfolio: portfolioName.value,
    model: modelName,
    confidence: r ? `${(r.confidenceLevel * 100).toFixed(0)}%` : '95%',
    lookback: r ? `${r.alignedReturnCount} 天` : '252 天',
    date: new Date().toISOString().split('T')[0],
    duration: '—',
  }
})

const metrics = computed(() => {
  const r = risk.value
  if (!r) return []
  const h1 = r.horizons.find((h) => h.horizonDays === 1)
  return [
    {
      label: 'VaR 95% (1日)',
      value: h1 ? `${(h1.historicalVaR * 100).toFixed(2)}%` : '—',
      sub: 'Historical Simulation',
    },
    {
      label: 'ES 95% (1日)',
      value: h1 ? `${(h1.historicalES * 100).toFixed(2)}%` : '—',
      sub: 'Expected Shortfall',
    },
    {
      label: '年化波動率',
      value: `${(r.historicalAnnualizedVolatility * 100).toFixed(2)}%`,
      sub: r.volatilityMethod,
    },
    {
      label: '最大回撤',
      value: `${(r.maxDrawdown * 100).toFixed(2)}%`,
      sub: 'Historical',
    },
  ]
})

const riskKPIData = computed(() => {
  const r = risk.value
  const h1 = r?.horizons.find((h) => h.horizonDays === 1)
  const total = r?.totalMarketValue ?? 0
  const varAmount = h1 ? total * Number(h1.historicalVaR) : 0
  const esAmount = h1 ? total * Number(h1.historicalES) : 0
  return [
    {
      label: 'DAILY VaR (95%)',
      value: h1 ? `${(h1.historicalVaR * 100).toFixed(2)}%` : '—',
      sub: varAmount ? `${formatMoney(varAmount)} ${r?.baseCurrency ?? ''}` : '—',
      color: '#FF6B00',
    },
    {
      label: 'DAILY ES (95%)',
      value: h1 ? `${(h1.historicalES * 100).toFixed(2)}%` : '—',
      sub: esAmount ? `預期損失 ${formatMoney(esAmount)}` : '—',
      color: '#8B1A2B',
    },
    {
      label: 'STRESS VaR',
      value: '-18.52%',
      sub: 'AI泡沫情境（示範資料）',
      color: '#8B1A2B',
    },
    {
      label: 'MAX DRAWDOWN',
      value: r ? `${(r.maxDrawdown * 100).toFixed(2)}%` : '—',
      sub: '歷史最大回撤',
      color: '#666666',
    },
    {
      label: 'SHARPE RATIO',
      value: r ? r.sharpeRatio.toFixed(2) : '—',
      sub: '風險調整後報酬',
      color: '#666666',
    },
  ]
})

const varTableRows = computed(() => {
  const r = risk.value
  const h1 = r?.horizons.find((h) => h.horizonDays === 1)
  return [
    {
      method: '歷史模擬法（後端資料）',
      var95: h1?.historicalVaR ?? 0,
      var99: (h1?.historicalVaR ?? 0) * 1.35,
      note: '1日',
    },
    {
      method: '蒙地卡羅模擬（後端資料）',
      var95: h1?.monteCarloVaR ?? 0,
      var99: (h1?.monteCarloVaR ?? 0) * 1.35,
      note: '1日',
    },
    { method: '參數法（示範資料）', var95: -0.0455, var99: -0.0692, note: '假設常態分配' },
    { method: 'EWMA（示範資料）', var95: -0.0468, var99: -0.0721, note: 'λ=0.94' },
  ]
})

const esTableRows = computed(() => {
  const r = risk.value
  const h1 = r?.horizons.find((h) => h.horizonDays === 1)
  return [
    {
      method: '歷史模擬法（後端資料）',
      es95: h1?.historicalES ?? 0,
      es99: (h1?.historicalES ?? 0) * 1.35,
    },
    {
      method: '蒙地卡羅模擬（後端資料）',
      es95: h1?.monteCarloES ?? 0,
      es99: (h1?.monteCarloES ?? 0) * 1.35,
    },
    { method: '參數法（示範資料）', es95: -0.0578, es99: -0.0842 },
    { method: 'EWMA（示範資料）', es95: -0.0592, es99: -0.0895 },
  ]
})

const mcP1 = computed(() => {
  const upper = monteCarloPercentiles.p99.map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ')
  const lower = monteCarloPercentiles.p1.map((v, i) => `${70 + ((252 - i) / 252) * 780},${200 - v * 400}`).join(' ')
  return `${upper} ${lower}`
})

const mcP5 = computed(() => {
  const upper = monteCarloPercentiles.p95.map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ')
  const lower = monteCarloPercentiles.p5.map((v, i) => `${70 + ((252 - i) / 252) * 780},${200 - v * 400}`).join(' ')
  return `${upper} ${lower}`
})

function formatMoney(n: number) {
  if (n === 0) return '0'
  const abs = Math.abs(n)
  const sign = n < 0 ? '-' : ''
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(2)}M`
  if (abs >= 1_000) return `${sign}${(abs / 1_000).toFixed(2)}K`
  return `${sign}${abs.toFixed(2)}`
}

onMounted(async () => {
  loading.value = true
  try {
    const [portfolio, riskData] = await Promise.all([
      getPortfolio(portfolioId.value),
      getPortfolioRisk(portfolioId.value, {
        from: fromDate.value,
        to: toDate.value,
        horizonDays: 30,
        confidenceLevel: 0.95,
        simulations: 10000,
        model: 'mvewma_fhs',
      }),
    ])
    portfolioName.value = portfolio.name
    risk.value = riskData
  } catch (e) {
    error.value = '無法載入風險分析資料，請稍後再試。'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="kimi-page-dark" style="padding-top: 40px">
    <div class="kimi-content" style="margin-top: 0; padding-top: 20px">
      <button class="kimi-btn kimi-btn-dark" style="margin-bottom: 24px" @click="router.push({ name: 'risk-runs' })">
        ← BACK TO RISK RUNS
      </button>

      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 40px">
        <div>
          <h1 style="font-size: 28px; font-weight: 700; margin: 0; color: #FFFFFF">{{ runInfo.portfolio }}</h1>
          <span class="kimi-caption" style="margin-top: 4px; display: block">RISK ANALYSIS RUN — {{ runInfo.id }}</span>
        </div>
        <span class="kimi-tag" style="border-color: #34d399; color: #34d399">COMPLETED</span>
      </div>

      <!-- Loading / Error -->
      <ScrollReveal v-if="loading">
        <div class="kimi-section-dark" style="padding: 40px; text-align: center; color: #666666">載入風險分析資料中...</div>
      </ScrollReveal>

      <ScrollReveal v-if="error && !loading">
        <div class="kimi-section-dark" style="padding: 40px; text-align: center; color: #f87171">{{ error }}</div>
      </ScrollReveal>

      <template v-if="!loading && risk">
        <!-- Run Info -->
        <ScrollReveal>
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div style="display: grid; grid-template-columns: repeat(2, 1fr)">
              <div
                v-for="(item, i) in [
                  { label: 'MODEL', value: runInfo.model },
                  { label: 'CONFIDENCE', value: runInfo.confidence },
                  { label: 'LOOKBACK', value: runInfo.lookback },
                  { label: 'DATE RANGE', value: `${fromDate} ~ ${toDate}` },
                ]"
                :key="i"
                style="padding: 20px; border-right: 1px solid #333333; border-bottom: 1px solid #333333"
              >
                <span class="kimi-caption" style="margin-bottom: 4px; display: block; color: #666666">{{ item.label }}</span>
                <span style="font-size: 16px; font-weight: 600; color: #FFFFFF">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Model Metadata -->
        <ScrollReveal :delay="0.05">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <h3 style="margin: 0 0 20px; font-size: 16px; font-weight: 600; color: #FFFFFF">模型細節</h3>
            <div style="display: grid; grid-template-columns: repeat(4, 1fr)">
              <div
                v-for="(item, i) in [
                  { label: 'COVARIANCE METHOD', value: risk.covarianceMethod ?? '—' },
                  { label: 'RESIDUAL SAMPLING', value: risk.residualSampling ?? '—' },
                  { label: 'COMMON TRADING DAYS', value: risk.commonTradingDays ? `${risk.commonTradingDays} 天` : '—' },
                  { label: 'SHRINKAGE ALPHA', value: risk.shrinkageAlpha != null ? `${(risk.shrinkageAlpha * 100).toFixed(2)}%` : '—' },
                ]"
                :key="i"
                style="padding: 20px; border-right: 1px solid #333333"
              >
                <span class="kimi-caption" style="margin-bottom: 4px; display: block; color: #666666">{{ item.label }}</span>
                <span style="font-size: 16px; font-weight: 600; color: #FFFFFF">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- KPI Cards -->
        <ScrollReveal :delay="0.1">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div class="kimi-kpi-grid-5">
              <div
                v-for="(kpi, i) in riskKPIData"
                :key="i"
                class="kimi-kpi-cell kimim-kpi-cell-dark"
                :class="{ 'kimi-kpi-cell-dark': true }"
              >
                <span class="kimi-caption" style="margin-bottom: 8px; display: block">{{ kpi.label }}</span>
                <span class="kimi-data" :style="{ color: kpi.color }">{{ kpi.value }}</span>
                <span style="font-size: 11px; color: #666666; margin-top: 4px; display: block">{{ kpi.sub }}</span>
                <div class="accent-bar" style="background-color: #8B1A2B" />
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Core Metrics -->
        <ScrollReveal :delay="0.15">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div class="kimi-kpi-grid">
              <div
                v-for="(m, i) in metrics"
                :key="i"
                class="kimi-kpi-cell kimi-kpi-cell-dark"
              >
                <span class="kimi-caption" style="color: #666666; margin-bottom: 8px; display: block">{{ m.label }}</span>
                <span class="kimi-data" style="color: #FFFFFF">{{ m.value }}</span>
                <span style="font-size: 12px; color: #666666; margin-top: 4px; display: block">{{ m.sub }}</span>
                <div class="accent-bar" style="background-color: #8B1A2B" />
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- VaR Results Table -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">Value at Risk 分析</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">VALUE AT RISK — 95% & 99% 信賴區間</span>
            </div>
            <div style="padding: 20px">
              <div class="kimi-grid-2" style="gap: 32px">
                <div>
                  <table                   class="kimi-table kimi-table-dark" style="border-collapse: collapse">
                    <thead>
                      <tr>
                        <th style="text-align: left">計算方法</th>
                        <th style="text-align: right">VaR 95%</th>
                        <th style="text-align: right">VaR 99%</th>
                        <th style="text-align: left; padding-left: 16px">備註</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(v, i) in varTableRows" :key="i">
                        <td style="color: #FFFFFF">{{ v.method }}</td>
                        <td class="kimi-font-mono" style="text-align: right; color: v.var95 < -0.048 ? '#FF6B00' : '#FFFFFF'">{{ (v.var95 * 100).toFixed(2) }}%</td>
                        <td class="kimi-font-mono" style="text-align: right; color: '#8B1A2B'">{{ (v.var99 * 100).toFixed(2) }}%</td>
                        <td style="color: #666666; padding-left: 16px">{{ v.note }}</td>
                      </tr>
                    </tbody>
                  </table>
                  <p style="font-size: 11px; color: #666666; margin-top: 12px">
                    註：後端資料為歷史模擬法與蒙地卡羅模擬；參數法與 EWMA 為示範資料。
                  </p>
                </div>

                <div>
                  <h3 style="font-size: 14px; font-weight: 500; margin: 0 0 12px; color: #FFFFFF">日報酬分布直方圖（250日）</h3>
                  <div class="kimi-mock-label">示範資料</div>
                  <svg width="100%" height="240" viewBox="0 0 400 240">
                    <line v-for="i in 5" :key="'g-' + i" x1="50" :y1="30 + (i - 1) * 40" x2="380" :y2="30 + (i - 1) * 40" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                    <text v-for="i in 5" :key="'gy-' + i" x="45" :y="30 + (i - 1) * 40 + 4" text-anchor="end" fill="#666666" font-size="10">{{ Math.round(50 - (i - 1) * 12) }}</text>

                    <g v-for="(bin, i) in varHistogram" :key="i">
                      <rect
                        :x="55 + i * 29"
                        :y="210 - (bin.count / 50) * 180"
                        width="24"
                        :height="(bin.count / 50) * 180"
                        :fill="bin.isTail99 ? '#8B1A2B' : bin.isTail95 ? '#FF6B00' : '#333333'"
                        class="transition-all"
                      />
                      <text :x="55 + i * 29 + 12" y="228" text-anchor="middle" fill="#666666" font-size="8">{{ bin.bin }}</text>
                    </g>

                    <g transform="translate(55, 12)">
                      <rect x="0" y="-6" width="10" height="8" fill="#333333" />
                      <text x="14" y="0" fill="#666666" font-size="9">正常區間</text>
                      <rect x="70" y="-6" width="10" height="8" fill="#FF6B00" />
                      <text x="84" y="0" fill="#666666" font-size="9">95% 尾部</text>
                      <rect x="145" y="-6" width="10" height="8" fill="#8B1A2B" />
                      <text x="159" y="0" fill="#666666" font-size="9">99% 尾部</text>
                    </g>
                  </svg>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- ES / CVaR Analysis -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">Expected Shortfall (ES) 分析</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">CONDITIONAL VaR — 尾部損失期望值</span>
            </div>
            <div style="padding: 20px">
              <div class="kimi-grid-2" style="gap: 32px">
                <div>
                  <table class="kimi-table kimi-table-dark">
                    <thead>
                      <tr>
                        <th style="text-align: left">計算方法</th>
                        <th style="text-align: right">ES 95%</th>
                        <th style="text-align: right">ES 99%</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(e, i) in esTableRows" :key="i">
                        <td style="color: #FFFFFF">{{ e.method }}</td>
                        <td class="kimi-font-mono" style="text-align: right; color: '#FF6B00'">{{ (e.es95 * 100).toFixed(2) }}%</td>
                        <td class="kimi-font-mono" style="text-align: right; color: '#8B1A2B'">{{ (e.es99 * 100).toFixed(2) }}%</td>
                      </tr>
                    </tbody>
                  </table>
                  <div class="kimi-panel-dark" style="margin-top: 16px">
                    <p style="font-size: 12px; color: #666666; margin: 0">
                      ES（Expected Shortfall）衡量當損失超過 VaR 閾值時的
                      <strong style="color: #FFFFFF">平均損失程度</strong>。
                      相較於 VaR 僅反映單一分位數點，ES 更完整地捕捉尾部風險。
                    </p>
                  </div>
                </div>

                <div>
                  <h3 style="font-size: 14px; font-weight: 500; margin: 0 0 12px; color: #FFFFFF">VaR vs ES 比較</h3>
                  <div class="kimi-mock-label">示範資料</div>
                  <svg width="100%" height="280" viewBox="0 0 400 280">
                    <line v-for="i in 6" :key="'g-' + i" x1="80" :y1="30 + (i - 1) * 40" x2="380" :y2="30 + (i - 1) * 40" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                    <text v-for="i in 6" :key="'y-' + i" x="75" :y="30 + (i - 1) * 40 + 4" text-anchor="end" fill="#666666" font-size="10">{{ -(i - 1) * 2 }}%</text>

                    <g v-for="(d, i) in varEsComparison" :key="i">
                      <rect :x="85 + i * 58" :y="30 + ((-d.var) / 12) * 200" width="22" :height="(-d.var / 12) * 200" fill="#FF6B00" opacity="0.8" />
                      <rect :x="110 + i * 58" :y="30 + ((-d.es) / 12) * 200" width="22" :height="(-d.es / 12) * 200" fill="#8B1A2B" opacity="0.8" />
                      <text :x="85 + i * 58 + 22" y="255" text-anchor="middle" fill="#666666" font-size="9">{{ d.confidence }}</text>
                    </g>

                    <g transform="translate(90, 270)">
                      <rect x="0" y="-6" width="12" height="8" fill="#FF6B00" opacity="0.8" />
                      <text x="16" y="0" fill="#666666" font-size="9">VaR</text>
                      <rect x="50" y="-6" width="12" height="8" fill="#8B1A2B" opacity="0.8" />
                      <text x="66" y="0" fill="#666666" font-size="9">ES</text>
                    </g>
                  </svg>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Stress Test Scenarios -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <div class="kimi-mock-label" style="margin-bottom: 8px">示範資料</div>
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">壓力測試情境</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">STRESS TESTING — 六大極端情境模擬</span>
            </div>
            <div style="padding: 20px">
              <div class="kimi-grid-3">
                <div
                  v-for="s in stressScenarios"
                  :key="s.id"
                  class="kimi-panel-dark"
                  style="cursor: pointer; transition: all 0.2s ease"
                  :style="{ borderColor: selectedScenario === s.id ? '#8B1A2B' : '#333333', backgroundColor: selectedScenario === s.id ? '#111111' : '#0A0A0A' }"
                  @click="selectedScenario = selectedScenario === s.id ? null : s.id"
                >
                  <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px">
                    <span style="font-size: 14px; font-weight: 500; color: #FFFFFF">{{ s.name }}</span>
                    <span class="kimi-tag" :style="{ borderColor: severityColor(s.severity), color: severityColor(s.severity) }">{{ severityLabel(s.severity) }}</span>
                  </div>

                  <p style="font-size: 12px; color: #666666; margin: 0 0 12px">{{ s.description }}</p>

                  <div style="display: flex; align-items: baseline; gap: 8px">
                    <span style="font-size: 22px; font-weight: 600; color: #8B1A2B">{{ s.impact }}%</span>
                    <span class="kimi-caption">投組衝擊</span>
                  </div>

                  <div v-if="selectedScenario === s.id" style="margin-top: 16px; padding-top: 16px; border-top: 1px solid #333333">
                    <div v-for="(d, i) in s.details" :key="i" style="display: flex; justify-content: space-between; align-items: center; padding: 6px 0">
                      <span style="font-size: 12px; color: #666666">{{ d.metric }}</span>
                      <span style="font-size: 12px; color: #FFFFFF; font-family: var(--kimi-font-mono)">{{ d.value }}</span>
                    </div>
                    <div style="margin-top: 12px; display: flex; flex-wrap: wrap; gap: 6px">
                      <span v-for="sector in s.affectedSectors" :key="sector" class="kimi-tag" style="border-color: #333333; color: #999999">{{ sector }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Backtest Results -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <div class="kimi-mock-label" style="margin-bottom: 8px">示範資料</div>
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">VaR 回測驗證</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">BACKTESTING — 例外事件統計</span>
            </div>
            <div style="padding: 20px">
              <div class="kimi-grid-2" style="gap: 32px">
                <svg width="100%" height="280" viewBox="0 0 500 280">
                  <line v-for="i in 6" :key="'g-' + i" x1="50" :y1="30 + (i - 1) * 40" x2="480" :y2="30 + (i - 1) * 40" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                  <text v-for="v in [5, 0, -5, -10, -15, -20]" :key="v" x="45" :y="30 + ((5 - v) / 25) * 40 + 4" text-anchor="end" fill="#666666" font-size="10">{{ v }}%</text>

                  <line x1="50" :y1="30 + (5 / 25) * 200" x2="480" :y2="30 + (5 / 25) * 200" stroke="#666666" stroke-width="2" />

                  <line x1="50" :y1="30 + ((5 - (-4.82)) / 25) * 200" x2="480" :y2="30 + ((5 - (-4.82)) / 25) * 200" stroke="#FF6B00" stroke-width="1" stroke-dasharray="4 4" />
                  <text x="485" :y="30 + ((5 - (-4.82)) / 25) * 200 + 3" fill="#FF6B00" font-size="9">VaR 95%</text>

                  <g v-for="(d, i) in backtestData" :key="i">
                    <line
                      :x1="55 + i * 35"
                      :y1="30 + ((5 - 0) / 25) * 200"
                      :x2="55 + i * 35"
                      :y2="30 + ((5 - d.actual) / 25) * 200"
                      :stroke="d.breached ? '#8B1A2B' : d.actual >= 0 ? '#FFFFFF' : '#666666'"
                      stroke-width="4"
                    />
                    <text v-if="d.breached" :x="55 + i * 35" :y="30 + ((5 - d.actual) / 25) * 200 - 6" text-anchor="middle" fill="#8B1A2B" font-size="8">!</text>
                  </g>

                  <text v-for="(d, i) in backtestData" :key="'xl-' + i" :x="55 + i * 35" y="260" text-anchor="middle" fill="#666666" font-size="8">{{ d.date.slice(5) }}</text>
                </svg>

                <div style="display: flex; flex-direction: column; justify-content: center">
                  <div class="kimi-panel-dark" style="margin-bottom: 16px">
                    <div class="kimi-grid-2" style="gap: 16px">
                      <div v-for="stat in [
                        { label: '觀察期間', value: '250 交易日' },
                        { label: '例外次數', value: '12 次' },
                        { label: '例外比率', value: '4.8%' },
                        { label: '預期比率', value: '5.0%' },
                        { label: 'Kupiec 檢定', value: 'p=0.82 通過' },
                        { label: 'Christoffersen', value: 'p=0.71 通過' },
                      ]" :key="stat.label"
                      >
                        <span class="kimi-caption" style="display: block; margin-bottom: 4px">{{ stat.label }}</span>
                        <span style="font-size: 18px; font-weight: 600; color: #FFFFFF">{{ stat.value }}</span>
                      </div>
                    </div>
                  </div>

                  <p style="font-size: 12px; color: #666666; margin: 0">
                    回測結果顯示實際例外比率接近理論預期，
                    Kupiec 比例檢定與 Christoffersen 獨立性檢定均通過，
                    表示 VaR 模型在統計上是可靠的。以上為示範資料。
                  </p>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Monte Carlo Simulation -->
        <ScrollReveal class="mt-20" style="margin-bottom: 80px">
          <div class="kimi-section-dark">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <div class="kimi-mock-label" style="margin-bottom: 8px">示範資料</div>
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">蒙地卡羅模擬</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">MONTE CARLO — 10,000 次路徑模擬</span>
            </div>
            <div style="padding: 20px">
              <svg width="100%" height="400" viewBox="0 0 900 400">
                <line v-for="i in 7" :key="'g-' + i" x1="70" :y1="30 + (i - 1) * 50" x2="850" :y2="30 + (i - 1) * 50" stroke="#333333" stroke-width="1" stroke-dasharray="4 4" />
                <text
                  v-for="l in ['+20%', '+10%', '0%', '-10%', '-20%', '-30%', '-40%']"
                  :key="l"
                  x="65"
                  :y="30 + ({ '+20%': 0, '+10%': 1, '0%': 2, '-10%': 3, '-20%': 4, '-30%': 5, '-40%': 6 }[l]) * 50 + 4"
                  text-anchor="end"
                  fill="#666666"
                  font-size="10"
                >{{ l }}</text>

                <polygon :points="mcP1" fill="#8B1A2B" fill-opacity="0.05" />
                <polygon :points="mcP5" fill="#8B1A2B" fill-opacity="0.08" />

                <polyline :points="monteCarloPercentiles.p50.map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ')" fill="none" stroke="#FFFFFF" stroke-width="2" />

                <polyline
                  v-for="(path, idx) in displayedPaths"
                  :key="idx"
                  :points="path.map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ')"
                  fill="none"
                  :stroke="['#333333', '#444444', '#555555', '#666666'][idx % 4]"
                  stroke-width="0.5"
                  opacity="0.4"
                />

                <g transform="translate(80, 18)">
                  <line x1="0" y1="0" x2="20" y2="0" stroke="#FFFFFF" stroke-width="2" />
                  <text x="25" y="4" fill="#FFFFFF" font-size="10">中位數路徑</text>
                  <rect x="100" y="-6" width="16" height="10" fill="#8B1A2B" fill-opacity="0.15" />
                  <text x="120" y="4" fill="#666666" font-size="10">99% 區間</text>
                  <rect x="180" y="-6" width="16" height="10" fill="#8B1A2B" fill-opacity="0.1" />
                  <text x="200" y="4" fill="#666666" font-size="10">95% 區間</text>
                </g>
              </svg>

              <div class="kimi-kpi-grid-5" style="margin-top: 20px">
                <div v-for="stat in mcStats" :key="stat.label" class="kimi-kpi-cell kimi-kpi-cell-dark"
                >
                  <span class="kimi-caption" style="display: block; margin-bottom: 4px">{{ stat.label }}</span>
                  <span style="font-size: 18px; font-weight: 600; font-family: var(--kimi-font-mono); color: var(--kimi-text-dark)" :style="{ color: stat.color }">{{ stat.value }}</span>
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        <!-- Holdings Risk Contribution -->
        <ScrollReveal class="mt-20">
          <div class="kimi-section-dark" style="margin-bottom: 40px">
            <div class="kimi-section-header" style="padding: 20px; border-bottom: 1px solid #333333">
              <h2 style="font-size: 24px; font-weight: 600; margin: 0; color: #FFFFFF">持倉風險貢獻</h2>
              <span class="kimi-caption" style="margin-top: 4px; display: block">HOLDING RISK CONTRIBUTION — 後端資料</span>
            </div>
            <div style="padding: 20px; overflow-x: auto">
              <table class="kimi-table kimi-table-dark">
                <thead>
                  <tr>
                    <th>代號</th>
                    <th>名稱</th>
                    <th style="text-align: right">交易所</th>
                    <th style="text-align: right">權重</th>
                    <th style="text-align: right">年化波動率</th>
                    <th style="text-align: right">資料筆數</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="h in risk.holdings" :key="h.securityId">
                    <td style="color: #FFFFFF">{{ h.ticker }}</td>
                    <td style="color: #FFFFFF">{{ h.securityName }}</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #666666">{{ h.exchange }}</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #FFFFFF">{{ (h.weight * 100).toFixed(2) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: h.annualizedVolatility > 0.3 ? '#FF6B00' : '#FFFFFF'">{{ (h.annualizedVolatility * 100).toFixed(2) }}%</td>
                    <td class="kimi-font-mono" style="text-align: right; color: #FFFFFF">{{ h.dataPointCount }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </ScrollReveal>
      </template>

      <div style="height: 80px" />
    </div>

    <Footer :dark="true" label="RISK" />
  </div>
</template>

<style scoped>
.mt-20 {
  margin-top: 40px;
}

.transition-all {
  transition: all 0.3s ease;
}

.kimi-mock-label {
  display: inline-block;
  padding: 2px 8px;
  font-size: 10px;
  font-weight: 500;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  border: 1px solid #8B1A2B;
  color: #8B1A2B;
  margin-bottom: 8px;
}

@media (max-width: 1024px) {
  .kimi-grid-2 > * {
    border-right: none !important;
  }
}
</style>
