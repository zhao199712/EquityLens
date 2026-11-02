<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ParticleCanvas from '../components/kimi/ParticleCanvas.vue'
import ScrollReveal from '../components/kimi/ScrollReveal.vue'
import {
  getPortfolios,
  getPortfolioRisk,
  getPortfolioValuation,
  getPortfolioValuationHistory,
  type PortfolioListItem,
  type PortfolioRiskResponse,
  type PortfolioValuationResponse,
} from '../services/risk'
import { listResearchRuns, type ResearchRunSummary } from '../services/research'
import { listAgentRuns, type AgentRunListItem } from '../services/agentRuns'
import { useAuthStore } from '../stores/auth'

const router = useRouter()
const { t } = useI18n()
const authStore = useAuthStore()

type RunStatus = 'completed' | 'running' | 'failed'

interface KpiCard {
  label: string
  value: string
  sub: string
}

interface RiskRow {
  key: string
  name: string
  date: string
  var95: string
  var99: string
  status: RunStatus
  to?: RouteLocationRaw
}

interface ReportRow {
  key: string
  name: string
  date: string
  tags: string[]
  to: RouteLocationRaw
}

interface ActivityRow {
  key: string
  title: string
  desc: string
  time: string
}

// ---- 即時資料狀態(登入且至少有一個組合才切換為 live;個別區塊失敗時保留 demo)----
const dataMode = ref<'live' | 'demo'>('demo')

const demoKpis = computed<KpiCard[]>(() => [
  { label: t('dashboard.kpi.totalAssets'), value: 'NT$ 12,580,000', sub: t('dashboard.kpi.totalAssetsSub') },
  { label: t('dashboard.kpi.totalReturn'), value: '+18.72%', sub: t('dashboard.kpi.totalReturnSub') },
  { label: t('dashboard.kpi.portfolioBeta'), value: '1.08', sub: t('dashboard.kpi.portfolioBetaSub') },
  { label: t('dashboard.kpi.sharpeRatio'), value: '1.42', sub: t('dashboard.kpi.sharpeRatioSub') },
])

const demoRiskRuns: RiskRow[] = [
  { key: 'demo-1', name: '科技成長型投資組合', date: '2026-06-03', var95: '-2.3%', var99: '-3.8%', status: 'completed' },
  { key: 'demo-2', name: '價值型藍籌股組合', date: '2026-06-02', var95: '-1.5%', var99: '-2.7%', status: 'completed' },
  { key: 'demo-3', name: '全球平衡型組合', date: '2026-06-01', var95: '-1.8%', var99: '-3.1%', status: 'completed' },
]

const demoReports = computed<ReportRow[]>(() => [
  { key: 'demo-1', name: 'TSMC 2025 Q1 財報分析', date: '2026-06-03', tags: ['AI Memo', `${t('dashboard.confidence')}: 高`], to: { name: 'financial-report-detail', params: { id: '1' } } },
  { key: 'demo-2', name: 'Apple FY2025 半年報', date: '2026-06-02', tags: ['Financial Report', `${t('dashboard.confidence')}: 中`], to: { name: 'financial-report-detail', params: { id: '2' } } },
  { key: 'demo-3', name: 'NVIDIA 風險評估報告', date: '2026-06-01', tags: ['Risk Report', `${t('dashboard.confidence')}: 高`], to: { name: 'financial-report-detail', params: { id: '3' } } },
])

const demoActivities = computed<ActivityRow[]>(() => [
  { key: 'demo-1', title: t('dashboard.activity.riskComplete'), desc: t('dashboard.activity.riskCompleteDesc'), time: t('dashboard.activity.time2h') },
  { key: 'demo-2', title: t('dashboard.activity.reportAdded'), desc: t('dashboard.activity.reportAddedDesc'), time: t('dashboard.activity.time5h') },
  { key: 'demo-3', title: t('dashboard.activity.portfolioCreated'), desc: t('dashboard.activity.portfolioCreatedDesc'), time: t('dashboard.activity.time1d') },
  { key: 'demo-4', title: t('dashboard.activity.riskModelUpdated'), desc: t('dashboard.activity.riskModelUpdatedDesc'), time: t('dashboard.activity.time2d') },
])

const liveKpis = ref<(KpiCard | null)[] | null>(null)
const liveRiskRuns = ref<RiskRow[] | null>(null)
const liveReports = ref<ReportRow[] | null>(null)
const liveActivities = ref<ActivityRow[] | null>(null)

// live 卡片缺值時退回該格 demo,避免整排出現空白
const kpiData = computed(() =>
  demoKpis.value.map((demo, i) => liveKpis.value?.[i] ?? (dataMode.value === 'live' ? { ...demo, value: '—', sub: t('dashboard.live.unavailable') } : demo)),
)
const recentRiskRuns = computed(() => liveRiskRuns.value ?? demoRiskRuns)
const recentReports = computed(() => liveReports.value ?? demoReports.value)
const activities = computed(() => liveActivities.value ?? demoActivities.value)

const statusColor: Record<RunStatus, string> = {
  completed: '#34d399',
  running: '#fbbf24',
  failed: '#f87171',
}

// ================= 格式化 =================

function formatLocalDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function oneYearAgo(): string {
  const from = new Date()
  from.setFullYear(from.getFullYear() - 1)
  return formatLocalDate(from)
}

const formatMoney = (value: number) => `NT$ ${Math.round(value).toLocaleString('en-US')}`

const formatSignedMoney = (value: number) =>
  `${value < 0 ? '-' : '+'}NT$ ${Math.round(Math.abs(value)).toLocaleString('en-US')}`

const formatSignedPercent = (ratio: number, decimals = 2) =>
  `${ratio >= 0 ? '+' : ''}${(ratio * 100).toFixed(decimals)}%`

const truncate = (text: string, max = 48) => (text.length > max ? `${text.slice(0, max)}…` : text)

function relativeTime(iso: string): string {
  const minutes = Math.floor((Date.now() - new Date(iso).getTime()) / 60000)
  if (minutes < 1) return t('dashboard.live.justNow')
  if (minutes < 60) return t('dashboard.live.minutesAgo', { n: minutes })
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return t('dashboard.live.hoursAgo', { n: hours })
  return t('dashboard.live.daysAgo', { n: Math.floor(hours / 24) })
}

function mapRunStatus(status: string): RunStatus {
  const normalized = status.toLowerCase()
  if (normalized === 'completed' || normalized === 'succeeded') return 'completed'
  if (normalized === 'running' || normalized === 'pending' || normalized === 'queued') return 'running'
  return 'failed'
}

// 取最短天期的歷史 VaR(後端回傳為負值比例,例如 -0.023)
function shortestHorizonVaR(risk: PortfolioRiskResponse): string {
  const horizon = [...risk.horizons].sort((a, b) => a.horizonDays - b.horizonDays)[0]
  return horizon ? `${(horizon.historicalVaR * 100).toFixed(1)}%` : '—'
}

// ================= 即時資料載入 =================

// KPI 1/2:所有組合估值加總
function buildValuationKpis(valuations: PortfolioValuationResponse[]): [KpiCard, KpiCard | null] {
  const totalAssets = valuations.reduce((sum, v) => sum + v.totalAssetValue, 0)
  const totalPnl = valuations.reduce((sum, v) => sum + v.totalUnrealizedPnl, 0)
  const totalCost = valuations.reduce((sum, v) => sum + v.totalCostValue, 0)

  const assets: KpiCard = {
    label: t('dashboard.kpi.totalAssets'),
    value: formatMoney(totalAssets),
    sub: t('dashboard.live.portfolioCount', { n: valuations.length }),
  }
  const totalReturn: KpiCard | null = totalCost > 0
    ? {
        label: t('dashboard.kpi.totalReturn'),
        value: formatSignedPercent(totalPnl / totalCost),
        sub: t('dashboard.live.unrealizedPnl', { amount: formatSignedMoney(totalPnl) }),
      }
    : null
  return [assets, totalReturn]
}

// 每個組合各算一次 95% / 99% VaR,失敗的組合標示為 failed 而非整表退回 demo
async function loadRiskRows(portfolios: PortfolioListItem[]): Promise<{ rows: RiskRow[]; risks: Map<string, PortfolioRiskResponse> }> {
  const range = { from: oneYearAgo(), to: formatLocalDate(new Date()), horizonDays: 1, simulations: 10000 }
  const risks = new Map<string, PortfolioRiskResponse>()
  const rows = await Promise.all(
    portfolios.slice(0, 5).map(async (portfolio): Promise<RiskRow> => {
      const [r95, r99] = await Promise.allSettled([
        getPortfolioRisk(portfolio.id, { ...range, confidenceLevel: 0.95 }),
        getPortfolioRisk(portfolio.id, { ...range, confidenceLevel: 0.99 }),
      ])
      const to = { name: 'portfolio-detail', params: { id: portfolio.id } }
      if (r95.status === 'rejected') {
        return { key: portfolio.id, name: portfolio.name, date: '—', var95: '—', var99: '—', status: 'failed', to }
      }
      risks.set(portfolio.id, r95.value)
      return {
        key: portfolio.id,
        name: portfolio.name,
        date: r95.value.dataAsOfDate ?? r95.value.to,
        var95: shortestHorizonVaR(r95.value),
        var99: r99.status === 'fulfilled' ? shortestHorizonVaR(r99.value) : '—',
        status: 'completed',
        to,
      }
    }),
  )
  return { rows, risks }
}

function buildReports(runs: ResearchRunSummary[]): ReportRow[] {
  return runs.slice(0, 3).map((run) => ({
    key: run.id,
    name: truncate(`${run.ticker} ${run.question}`),
    date: relativeTime(run.createdAtUtc),
    tags: ['Research', mapRunStatus(run.status).toUpperCase(), t('dashboard.live.citations', { n: run.citationCount })],
    to: { name: 'research-run-detail', params: { id: run.id } },
  }))
}

// 最近活動:研究、Agent Run、組合建立依時間合併取前 5
function buildActivities(
  portfolios: PortfolioListItem[],
  researchRuns: ResearchRunSummary[],
  agentRuns: AgentRunListItem[],
): ActivityRow[] {
  const merged: (ActivityRow & { at: string })[] = [
    ...portfolios.map((p) => ({
      key: `portfolio-${p.id}`,
      title: t('dashboard.activity.portfolioCreated'),
      desc: p.name,
      at: p.createdAtUtc,
      time: '',
    })),
    ...researchRuns.map((run) => ({
      key: `research-${run.id}`,
      title: t(`dashboard.live.research.${mapRunStatus(run.status)}`),
      desc: truncate(`${run.ticker} ${run.question}`),
      at: run.createdAtUtc,
      time: '',
    })),
    ...agentRuns.map((run) => ({
      key: `agent-${run.id}`,
      title: t(`dashboard.live.agentRun.${mapRunStatus(run.status)}`),
      desc: `${run.workflowType} / ${run.agentType}`,
      at: run.completedAtUtc ?? run.createdAtUtc,
      time: '',
    })),
  ]
  return merged
    .sort((a, b) => b.at.localeCompare(a.at))
    .slice(0, 5)
    .map(({ at, ...row }) => ({ ...row, time: relativeTime(at) }))
}

onMounted(async () => {
  if (!authStore.isAuthenticated) return

  const portfolios = await getPortfolios().catch(() => null)
  if (!portfolios || portfolios.length === 0) return
  dataMode.value = 'live'

  const [valuationResults, riskResult, researchResult, agentResult] = await Promise.all([
    Promise.allSettled(portfolios.map((p) => getPortfolioValuation(p.id))),
    loadRiskRows(portfolios),
    listResearchRuns({ limit: 5 }).catch(() => null),
    listAgentRuns({ limit: 5 }).catch(() => null),
  ])

  const valuations = valuationResults.flatMap((r) => (r.status === 'fulfilled' ? [r.value] : []))
  const kpis: (KpiCard | null)[] = [null, null, null, null]
  if (valuations.length > 0) {
    ;[kpis[0], kpis[1]] = buildValuationKpis(valuations)
  }

  // Beta / Sharpe 取總資產最大的組合
  const primary = valuations.length > 0
    ? valuations.reduce((best, v) => (v.totalAssetValue > best.totalAssetValue ? v : best))
    : null
  if (primary) {
    const history = await getPortfolioValuationHistory(primary.portfolioId, {
      from: oneYearAgo(),
      to: formatLocalDate(new Date()),
    }).catch(() => null)
    if (history?.beta != null) {
      kpis[2] = { label: t('dashboard.kpi.portfolioBeta'), value: history.beta.toFixed(2), sub: t('dashboard.live.betaSub') }
    }
    const risk = riskResult.risks.get(primary.portfolioId)
    if (risk) {
      kpis[3] = { label: t('dashboard.kpi.sharpeRatio'), value: risk.sharpeRatio.toFixed(2), sub: t('dashboard.live.sharpeSub') }
    }
  }
  liveKpis.value = kpis

  liveRiskRuns.value = riskResult.rows
  liveReports.value = researchResult ? buildReports(researchResult) : []
  liveActivities.value = buildActivities(portfolios, researchResult ?? [], agentResult ?? [])
})
</script>

<template>
  <div class="kimi-page-light">
    <!-- Hero -->
    <div class="kimi-hero" style="min-height: 80vh">
      <ParticleCanvas theme="warm" />
      <div class="kimi-hero-content">
        <span class="kimi-font-mono" style="font-size: 14px; letter-spacing: 0.3em; color: var(--kimi-text-light)">
          EQUITYLENS
        </span>
        <span class="kimi-caption" style="font-size: 12px; letter-spacing: 0.2em; text-transform: uppercase">
          AI-ASSISTED INVESTMENT ANALYTICS
        </span>

        <div style="text-align: center; margin-top: 24px">
          <h1 class="kimi-display" style="margin: 0">PORTFOLIO</h1>
          <h1 class="kimi-display" style="margin: 0">ANALYSIS</h1>
        </div>

        <p style="text-align: center; color: var(--kimi-muted); font-size: 14px; max-width: 480px; line-height: 1.6">
          {{ t('dashboard.heroSubtitle') }}
        </p>

        <div style="display: flex; gap: 12px; margin-top: 16px">
          <button class="kimi-btn kimi-btn-solid" @click="router.push({ name: 'portfolios' })">
            {{ t('dashboard.viewPortfolios') }}
          </button>
          <button class="kimi-btn" @click="router.push({ name: 'financial-reports' })">
            {{ t('dashboard.viewReports') }}
          </button>
        </div>

        <span class="kimi-tag data-mode-badge" data-testid="data-mode" :class="dataMode">
          {{ dataMode === 'live' ? 'LIVE DATA' : 'DEMO DATA' }}
        </span>
      </div>
    </div>

    <!-- Content -->
    <div class="kimi-content">
      <!-- KPI Cards -->
      <ScrollReveal>
        <div class="kimi-section">
          <div class="kimi-kpi-grid">
            <div
              v-for="(kpi, i) in kpiData"
              :key="i"
              class="kimi-kpi-cell"
              data-testid="kpi"
            >
              <span class="kimi-caption" style="margin-bottom: 8px">{{ kpi.label }}</span>
              <span class="kimi-data" style="color: var(--kimi-text-light)">{{ kpi.value }}</span>
              <span style="font-size: 12px; color: var(--kimi-muted); margin-top: 4px">{{ kpi.sub }}</span>
              <div class="accent-bar" style="background-color: var(--kimi-accent-orange)" />
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Recent Risk Runs -->
      <ScrollReveal :delay="0.1" style="margin-top: 60px">
        <div class="kimi-section">
          <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">{{ t('dashboard.recentRiskAnalysis') }}</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('dashboard.recentRiskAnalysisEn') }}</span>
          </div>
          <div style="overflow-x: auto">
            <table class="kimi-table kimi-table-light">
              <thead>
                <tr>
                  <th>{{ t('dashboard.riskTable.portfolio') }}</th>
                  <th>{{ t('dashboard.riskTable.date') }}</th>
                  <th>VaR 95%</th>
                  <th>VaR 99%</th>
                  <th>{{ t('dashboard.riskTable.status') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="run in recentRiskRuns"
                  :key="run.key"
                  data-testid="risk-row"
                  :style="run.to ? 'cursor: pointer' : undefined"
                  @click="run.to && router.push(run.to)"
                >
                  <td style="font-weight: 600">{{ run.name }}</td>
                  <td style="color: var(--kimi-muted)">{{ run.date }}</td>
                  <td style="color: #f87171; font-weight: 600">{{ run.var95 }}</td>
                  <td style="color: #f87171; font-weight: 600">{{ run.var99 }}</td>
                  <td>
                    <span class="kimi-tag" :style="{ borderColor: statusColor[run.status], color: statusColor[run.status] }">
                      {{ run.status === 'failed' ? t('dashboard.live.insufficientData') : run.status.toUpperCase() }}
                    </span>
                  </td>
                </tr>
                <tr v-if="recentRiskRuns.length === 0">
                  <td colspan="5" style="color: var(--kimi-muted)">{{ t('dashboard.live.empty') }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </ScrollReveal>

      <!-- Recent AI Reports -->
      <ScrollReveal :delay="0.15" style="margin-top: 60px">
        <div class="kimi-section">
          <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">{{ t('dashboard.recentAIReports') }}</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('dashboard.recentAIReportsEn') }}</span>
          </div>
          <div>
            <div
              v-for="report in recentReports"
              :key="report.key"
              data-testid="report-row"
              style="display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light); cursor: pointer; transition: background 0.2s"
              @click="router.push(report.to)"
            >
              <div>
                <div style="font-weight: 600; font-size: 14px">{{ report.name }}</div>
                <div style="font-size: 12px; color: var(--kimi-muted); margin-top: 2px">{{ report.date }}</div>
              </div>
              <div style="display: flex; gap: 8px">
                <span v-for="tag in report.tags" :key="tag" class="kimi-tag">{{ tag }}</span>
              </div>
            </div>
            <div v-if="recentReports.length === 0" style="padding: 16px 20px; color: var(--kimi-muted); font-size: 13px">
              {{ t('dashboard.live.empty') }}
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Activities -->
      <ScrollReveal :delay="0.2" style="margin-top: 60px">
        <div class="kimi-section">
          <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">{{ t('dashboard.recentActivity') }}</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">{{ t('dashboard.recentActivityEn') }}</span>
          </div>
          <div>
            <div
              v-for="act in activities"
              :key="act.key"
              data-testid="activity-row"
              style="display: flex; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light)"
            >
              <div>
                <div style="font-weight: 600; font-size: 14px">{{ act.title }}</div>
                <div style="font-size: 13px; color: var(--kimi-muted); margin-top: 2px">{{ act.desc }}</div>
              </div>
              <span style="font-size: 12px; color: var(--kimi-muted); white-space: nowrap">{{ act.time }}</span>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Bottom spacing -->
      <div style="height: 80px" />
    </div>

    <!-- Footer -->
    <footer class="kimi-footer">
      <span>RISE VISION 2026</span>
      <span class="kimi-font-mono" style="letter-spacing: 0.1em; text-transform: uppercase; font-size: 11px">DASHBOARD</span>
      <span>{{ dataMode === 'live' ? t('dashboard.live.footer') : t('dashboard.footer') }}</span>
    </footer>
  </div>
</template>

<style scoped>
.data-mode-badge {
  margin-top: 20px;
  letter-spacing: 0.15em;
}
.data-mode-badge.live {
  border-color: #34d399;
  color: #34d399;
}
</style>
