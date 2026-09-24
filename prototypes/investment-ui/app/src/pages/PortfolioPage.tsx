import { useState } from 'react'
import NavigationHeader from '@/components/NavigationHeader'
import Footer from '@/components/Footer'
import ScrollReveal, { StaggerContainer } from '@/components/ScrollReveal'
import LineChart from '@/components/LineChart'
import DonutChart from '@/components/DonutChart'
import BarChart from '@/components/BarChart'
import DataTable from '@/components/DataTable'
import ScatterPlot from '@/components/ScatterPlot'
import ParticleCanvas from '@/components/ParticleCanvas'
import {
  kpiData,
  portfolioValueData,
  allocationData,
  performanceAttribution,
  riskData,
} from '@/data/portfolioData'

export default function PortfolioPage() {
  const [timeRange, setTimeRange] = useState('1Y')
  const [hoveredSegment, setHoveredSegment] = useState<number | null>(null)

  const timeRanges = ['1Y', '6M', '3M', '1M', 'YTD']

  return (
    <div className="min-h-screen" style={{ backgroundColor: '#F5F5F5' }}>
      {/* Navigation Hero */}
      <div className="relative">
        <ParticleCanvas theme="warm" />
        <div className="relative" style={{ zIndex: 1 }}>
          <NavigationHeader isDark={false} />
        </div>
      </div>

      {/* Content */}
      <div className="page-padding" style={{ marginTop: '-80px', position: 'relative', zIndex: 2 }}>
        {/* KPI Cards */}
        <ScrollReveal>
          <div
            className="w-full border-t"
            style={{ borderColor: '#E0E0E0' }}
          >
            <div className="grid grid-cols-2 lg:grid-cols-4">
              {kpiData.map((kpi, i) => (
                <div
                  key={i}
                  className="flex flex-col justify-center items-center py-6 transition-all duration-300 hover:scale-[1.02] group cursor-default"
                  style={{
                    borderLeft: i === 0 ? 'none' : '1px solid #E0E0E0',
                    borderRight: '1px solid #E0E0E0',
                    borderBottom: '1px solid #E0E0E0',
                    height: 120,
                  }}
                >
                  <span className="text-caption uppercase mb-2" style={{ color: '#666666' }}>
                    {kpi.label}
                  </span>
                  <span className="text-data" style={{ color: '#000000' }}>
                    {kpi.value}
                  </span>
                  <span className="text-xs mt-1" style={{ color: '#666666' }}>
                    {kpi.sub}
                  </span>
                  <div
                    className="absolute left-0 top-0 bottom-0 w-0 group-hover:w-0.5 transition-all duration-300"
                    style={{ backgroundColor: '#FF6B00' }}
                  />
                </div>
              ))}
            </div>
          </div>
        </ScrollReveal>

        {/* Portfolio Value Trend */}
        <ScrollReveal className="mt-20">
          <div className="w-full border" style={{ borderColor: '#E0E0E0' }}>
            {/* Header */}
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between p-5 border-b" style={{ borderColor: '#E0E0E0' }}>
              <div>
                <h2 className="font-heading text-2xl font-semibold" style={{ color: '#000000' }}>
                  投資組合價值走勢
                </h2>
                <span className="text-caption block mt-1" style={{ color: '#666666' }}>
                  PORTFOLIO VALUE TREND
                </span>
                <span className="text-caption block mt-0.5" style={{ color: '#666666' }}>
                  2024.01 — 2025.12
                </span>
              </div>
              <div className="flex gap-2 mt-4 sm:mt-0">
                {timeRanges.map((r) => (
                  <button
                    key={r}
                    onClick={() => setTimeRange(r)}
                    className="font-mono text-xs px-3 py-1 transition-all duration-200"
                    style={{
                      border: `1px solid ${timeRange === r ? '#000000' : '#E0E0E0'}`,
                      backgroundColor: timeRange === r ? '#000000' : 'transparent',
                      color: timeRange === r ? '#FFFFFF' : '#666666',
                      borderRadius: 0,
                    }}
                  >
                    {r}
                  </button>
                ))}
              </div>
            </div>

            {/* Chart */}
            <div className="p-5">
              <LineChart
                data={portfolioValueData.values}
                labels={portfolioValueData.labels}
                yAxisLabels={portfolioValueData.yAxisLabels}
                height={400}
                lineColor="#000000"
                showArea={true}
              />
            </div>

            {/* Summary */}
            <div
              className="grid grid-cols-1 sm:grid-cols-3 gap-4 p-5 border-t"
              style={{ borderColor: '#E0E0E0' }}
            >
              <span className="text-sm" style={{ color: '#666666' }}>
                年初資產 {portfolioValueData.summary.start}
              </span>
              <span className="text-sm" style={{ color: '#666666' }}>
                最高資產 {portfolioValueData.summary.high}
              </span>
              <span className="text-sm" style={{ color: '#666666' }}>
                最低資產 {portfolioValueData.summary.low}
              </span>
            </div>
          </div>
        </ScrollReveal>

        {/* Asset Allocation */}
        <ScrollReveal className="mt-20">
          <div className="w-full border grid grid-cols-1 lg:grid-cols-5" style={{ borderColor: '#E0E0E0' }}>
            {/* Donut Chart */}
            <div
              className="lg:col-span-2 flex flex-col items-center justify-center py-10"
              style={{ borderRight: '1px solid #E0E0E0' }}
            >
              <DonutChart
                segments={allocationData.segments}
                centerLabel="NT$12.58M"
                centerSubLabel="4 類資產"
                activeIndex={hoveredSegment}
                onSegmentHover={setHoveredSegment}
              />
            </div>

            {/* Holdings Table */}
            <div className="lg:col-span-3 p-5">
              <div className="mb-4">
                <h2 className="font-heading text-2xl font-semibold" style={{ color: '#000000' }}>
                  持倉明細
                </h2>
                <span className="text-caption" style={{ color: '#666666' }}>
                  HOLDINGS
                </span>
              </div>
              <DataTable
                headers={['代碼', '名稱', '類別', '持有股數', '現價', '市值', '占比', '損益']}
                rows={allocationData.holdings.map((h) => [
                  h.code,
                  h.name,
                  h.category,
                  h.shares,
                  h.price,
                  h.value,
                  h.ratio,
                  h.pnl,
                ])}
                highlightRow={hoveredSegment}
                onRowHover={setHoveredSegment}
              />
            </div>
          </div>
        </ScrollReveal>

        {/* Performance Attribution */}
        <StaggerContainer className="mt-20" staggerDelay={0.15}>
          <div className="mb-6">
            <h2 className="font-heading text-2xl font-semibold" style={{ color: '#000000' }}>
              績效歸因分析
            </h2>
            <span className="text-caption" style={{ color: '#666666' }}>
              PERFORMANCE ATTRIBUTION
            </span>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
            {/* Sector Contribution */}
            <div className="stagger-item border p-5" style={{ borderColor: '#E0E0E0' }}>
              <h3 className="font-heading text-lg font-medium mb-4" style={{ color: '#000000' }}>
                產業別貢獻
              </h3>
              <BarChart
                data={performanceAttribution.sector.map((s) => ({
                  label: s.label,
                  value: s.value,
                  color: '#000000',
                }))}
                width={280}
              />
            </div>

            {/* Stock Alpha */}
            <div className="stagger-item border p-5" style={{ borderColor: '#E0E0E0' }}>
              <h3 className="font-heading text-lg font-medium mb-4" style={{ color: '#000000' }}>
                選股 Alpha
              </h3>
              <div className="flex flex-col">
                {performanceAttribution.alpha.map((a, i) => (
                  <div
                    key={i}
                    className="flex justify-between items-center py-2"
                    style={{
                      borderBottom: i < performanceAttribution.alpha.length - 1 ? '1px solid #E0E0E0' : 'none',
                    }}
                  >
                    <span className="text-sm" style={{ color: '#000000' }}>
                      {a.name}
                    </span>
                    <span
                      className="text-sm font-medium"
                      style={{ color: a.value > 0 ? '#000000' : '#666666' }}
                    >
                      {a.value > 0 ? '+' : ''}
                      {a.value}%
                    </span>
                  </div>
                ))}
              </div>
            </div>

            {/* Time-weighted return mini chart */}
            <div className="stagger-item border p-5" style={{ borderColor: '#E0E0E0' }}>
              <h3 className="font-heading text-lg font-medium mb-4" style={{ color: '#000000' }}>
                時間加權報酬
              </h3>
              <svg width="100%" height={120} viewBox="0 0 280 120">
                {performanceAttribution.monthlyReturns.map((v, i) => {
                  const x = (i / 11) * 260 + 10
                  const y = 60 - v * 8
                  return (
                    <g key={i}>
                      {i > 0 && (
                        <line
                          x1={((i - 1) / 11) * 260 + 10}
                          y1={60 - performanceAttribution.monthlyReturns[i - 1] * 8}
                          x2={x}
                          y2={y}
                          stroke={v >= 0 ? '#000000' : '#999999'}
                          strokeWidth={1.5}
                        />
                      )}
                      <circle cx={x} cy={y} r={3} fill={v >= 0 ? '#000000' : '#999999'} />
                    </g>
                  )
                })}
                <line x1={10} y1={60} x2={270} y2={60} stroke="#E0E0E0" strokeWidth={1} />
              </svg>
            </div>

            {/* Risk Metrics */}
            <div className="stagger-item border p-5" style={{ borderColor: '#E0E0E0' }}>
              <h3 className="font-heading text-lg font-medium mb-4" style={{ color: '#000000' }}>
                風險指標
              </h3>
              <div className="flex flex-col gap-4">
                {[
                  { label: '波動率', value: performanceAttribution.risk.volatility },
                  { label: '最大回撤', value: performanceAttribution.risk.maxDrawdown },
                  { label: '索提諾比率', value: performanceAttribution.risk.sortino },
                  { label: '資訊比率', value: performanceAttribution.risk.infoRatio },
                ].map((r, i) => (
                  <div key={i} className="flex justify-between items-center">
                    <span className="text-caption uppercase" style={{ color: '#666666' }}>
                      {r.label}
                    </span>
                    <span className="text-data" style={{ color: '#000000', fontSize: '22px' }}>
                      {r.value}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </StaggerContainer>

        {/* Risk Analysis */}
        <div className="mt-20 grid grid-cols-1 lg:grid-cols-2 gap-0 border" style={{ borderColor: '#E0E0E0' }}>
          {/* Risk Scatter */}
          <ScrollReveal className="p-5" style={{ borderRight: '1px solid #E0E0E0' }}>
            <h2 className="font-heading text-2xl font-semibold mb-4" style={{ color: '#000000' }}>
              風險矩陣
            </h2>
            <ScatterPlot
              data={riskData.scatter}
              xAxisLabel="波動率（標準差）"
              yAxisLabel="預期報酬率"
              xRange={[0, 30]}
              yRange={[-5, 25]}
              frontierCurve={[
                [5, 2], [8, 5], [10, 7], [12, 9], [15, 11], [18, 13], [22, 15], [25, 16],
              ]}
            />
          </ScrollReveal>

          {/* Drawdown History */}
          <ScrollReveal className="p-5">
            <h2 className="font-heading text-2xl font-semibold mb-4" style={{ color: '#000000' }}>
              歷史回撤
            </h2>
            <svg width="100%" height={250} viewBox="0 0 500 250">
              {/* Area */}
              <polygon
                points={`60,${20 + (1 - (-8.2) / 15) * 200} ${riskData.drawdown.values.map((v, i) => {
                  const x = 60 + (i / 11) * 420
                  const y = 20 + (1 - v / 15) * 200
                  return `${x},${y}`
                }).join(' ')} 480,${20 + (1 - 0 / 15) * 200}`}
                fill="rgba(0,0,0,0.06)"
              />
              {/* Line */}
              <polyline
                points={riskData.drawdown.values.map((v, i) => {
                  const x = 60 + (i / 11) * 420
                  const y = 20 + (1 - v / 15) * 200
                  return `${x},${y}`
                }).join(' ')}
                fill="none"
                stroke="#000000"
                strokeWidth={1.5}
              />
              {/* Max drawdown line */}
              <line
                x1={60 + (2 / 11) * 420}
                y1={20 + (1 - (-8.2) / 15) * 200}
                x2={60 + (2 / 11) * 420}
                y2={20 + 200}
                stroke="#000000"
                strokeWidth={1}
                strokeDasharray="4 4"
              />
              <text
                x={60 + (2 / 11) * 420 + 5}
                y={20 + (1 - (-8.2) / 15) * 200 - 5}
                fill="#000000"
                fontSize={11}
                fontWeight={600}
              >
                -8.2%
              </text>
              {/* Axis labels */}
              {[0, -5, -10, -15].map((v, i) => (
                <text
                  key={i}
                  x={55}
                  y={20 + (1 - v / 15) * 200 + 4}
                  textAnchor="end"
                  fill="#666666"
                  fontSize={10}
                >
                  {v}%
                </text>
              ))}
              {riskData.drawdown.labels.map((l, i) => (
                <text
                  key={`x-${i}`}
                  x={60 + (i / 11) * 420}
                  y={240}
                  textAnchor="middle"
                  fill="#666666"
                  fontSize={9}
                >
                  {l}
                </text>
              ))}
            </svg>
          </ScrollReveal>
        </div>

        {/* Bottom spacing */}
        <div className="h-20" />
      </div>

      <Footer isDark={false} />
    </div>
  )
}
