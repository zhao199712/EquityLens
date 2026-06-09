<script setup lang="ts">
import { useRouter } from 'vue-router'
import ParticleCanvas from '../components/kimi/ParticleCanvas.vue'
import ScrollReveal from '../components/kimi/ScrollReveal.vue'

const router = useRouter()

const kpiData = [
  { label: 'TOTAL ASSETS', value: 'NT$ 12,580,000', sub: '較上月 +2.3%' },
  { label: 'TOTAL RETURN', value: '+18.72%', sub: '年化報酬' },
  { label: 'PORTFOLIO BETA', value: '1.08', sub: '相對大盤' },
  { label: 'SHARPE RATIO', value: '1.42', sub: '風險調整後報酬' },
]

const recentRiskRuns = [
  { name: '科技成長型投資組合', date: '2026-06-03', var95: '-2.3%', var99: '-3.8%', status: 'completed' },
  { name: '價值型藍籌股組合', date: '2026-06-02', var95: '-1.5%', var99: '-2.7%', status: 'completed' },
  { name: '全球平衡型組合', date: '2026-06-01', var95: '-1.8%', var99: '-3.1%', status: 'completed' },
]

const recentReports = [
  { name: 'TSMC 2025 Q1 財報分析', type: 'AI Memo', confidence: '高', date: '2026-06-03' },
  { name: 'Apple FY2025 半年報', type: 'Financial Report', confidence: '中', date: '2026-06-02' },
  { name: 'NVIDIA 風險評估報告', type: 'Risk Report', confidence: '高', date: '2026-06-01' },
]

const activities = [
  { title: '完成投資組合風險分析', desc: '科技成長型投資組合 — VaR 95%: -2.3%', time: '2 小時前' },
  { title: '新增 AI 財務分析報告', desc: 'TSMC 2025 Q1 財報分析', time: '5 小時前' },
  { title: '建立新投資組合', desc: '價值型藍籌股組合', time: '1 天前' },
  { title: '風險模型參數更新', desc: '更新置信水準至 99%', time: '2 天前' },
]
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
          即時追蹤投資組合表現，智能分析收益與風險，數據驅動的資產配置洞察
        </p>

        <div style="display: flex; gap: 12px; margin-top: 16px">
          <button class="kimi-btn kimi-btn-solid" @click="router.push({ name: 'portfolios' })">
            VIEW PORTFOLIOS
          </button>
          <button class="kimi-btn" @click="router.push({ name: 'financial-reports' })">
            VIEW REPORTS
          </button>
        </div>
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
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">最近風險分析</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">RECENT RISK ANALYSIS</span>
          </div>
          <div style="overflow-x: auto">
            <table class="kimi-table kimi-table-light">
              <thead>
                <tr>
                  <th>投資組合</th>
                  <th>日期</th>
                  <th>VaR 95%</th>
                  <th>VaR 99%</th>
                  <th>狀態</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="run in recentRiskRuns" :key="run.name">
                  <td style="font-weight: 600">{{ run.name }}</td>
                  <td style="color: var(--kimi-muted)">{{ run.date }}</td>
                  <td style="color: #f87171; font-weight: 600">{{ run.var95 }}</td>
                  <td style="color: #f87171; font-weight: 600">{{ run.var99 }}</td>
                  <td>
                    <span class="kimi-tag" style="border-color: #34d399; color: #34d399">COMPLETED</span>
                  </td>
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
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">最近 AI 財報分析</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">RECENT AI REPORTS</span>
          </div>
          <div>
            <div
              v-for="(report, i) in recentReports"
              :key="i"
              style="display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--kimi-border-light); cursor: pointer; transition: background 0.2s"
              @click="router.push({ name: 'financial-report-detail', params: { id: String(i + 1) } })"
            >
              <div>
                <div style="font-weight: 600; font-size: 14px">{{ report.name }}</div>
                <div style="font-size: 12px; color: var(--kimi-muted); margin-top: 2px">{{ report.date }}</div>
              </div>
              <div style="display: flex; gap: 8px">
                <span class="kimi-tag">{{ report.type }}</span>
                <span class="kimi-tag">信心: {{ report.confidence }}</span>
              </div>
            </div>
          </div>
        </div>
      </ScrollReveal>

      <!-- Activities -->
      <ScrollReveal :delay="0.2" style="margin-top: 60px">
        <div class="kimi-section">
          <div style="padding: 20px; border-bottom: 1px solid var(--kimi-border-light)">
            <h2 style="margin: 0; font-size: 20px; font-weight: 600">最近活動</h2>
            <span class="kimi-caption" style="margin-top: 4px; display: block">RECENT ACTIVITY</span>
          </div>
          <div>
            <div
              v-for="(act, i) in activities"
              :key="i"
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
      <span>數據僅供參考</span>
    </footer>
  </div>
</template>
