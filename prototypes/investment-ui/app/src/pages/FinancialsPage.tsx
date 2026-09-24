import NavigationHeader from '@/components/NavigationHeader'
import Footer from '@/components/Footer'
import ScrollReveal, { StaggerContainer } from '@/components/ScrollReveal'
import DataTable from '@/components/DataTable'
import StackedBarChart from '@/components/StackedBarChart'
import RadarChart from '@/components/RadarChart'
import ParticleCanvas from '@/components/ParticleCanvas'
import {
  financialKPIData,
  profitabilityData,
  cashFlowData,
  radarData,
  ratioTableData,
  balanceSheetData,
  dupontData,
} from '@/data/financialsData'

export default function FinancialsPage() {
  // Revenue profit combined data
  const revCombined = [
    { q: '23Q1', r23: 18.2, r24: null, r25: null, ni: 3.8 },
    { q: '23Q2', r23: 19.5, r24: null, r25: null, ni: 4.2 },
    { q: '23Q3', r23: 20.1, r24: null, r25: null, ni: 4.1 },
    { q: '23Q4', r23: 21.8, r24: null, r25: null, ni: 4.8 },
    { q: '24Q1', r23: null, r24: 22.5, r25: null, ni: 4.5 },
    { q: '24Q2', r23: null, r24: 23.8, r25: null, ni: 5.1 },
    { q: '24Q3', r23: null, r24: 24.2, r25: null, ni: 5.3 },
    { q: '24Q4', r23: null, r24: 26.1, r25: null, ni: 6.2 },
    { q: '25Q1', r23: null, r24: null, r25: 25.8, ni: 5.8 },
    { q: '25Q2', r23: null, r24: null, r25: 27.2, ni: 6.5 },
    { q: '25Q3', r23: null, r24: null, r25: 28.5, ni: 7.2 },
    { q: '25Q4', r23: null, r24: null, r25: 30.1, ni: 8.0 },
  ]

  return (
    <div className="min-h-screen" style={{ backgroundColor: '#0A0A0A' }}>
      {/* Navigation Hero */}
      <div className="relative">
        <ParticleCanvas theme="red" />
        <div className="relative" style={{ zIndex: 1 }}>
          <NavigationHeader isDark={true} />
        </div>
      </div>

      {/* Content */}
      <div className="page-padding" style={{ marginTop: '-80px', position: 'relative', zIndex: 2 }}>
        {/* KPI Cards */}
        <ScrollReveal>
          <div className="w-full border-t" style={{ borderColor: '#333333' }}>
            <div className="grid grid-cols-2 lg:grid-cols-5">
              {financialKPIData.map((kpi, i) => (
                <div
                  key={i}
                  className="flex flex-col justify-center items-center py-6 group cursor-default"
                  style={{
                    borderLeft: i === 0 ? 'none' : '1px solid #333333',
                    borderRight: '1px solid #333333',
                    borderBottom: '1px solid #333333',
                    height: 130,
                    backgroundColor: '#0A0A0A',
                  }}
                >
                  <span className="text-caption uppercase mb-2" style={{ color: '#666666' }}>
                    {kpi.label}
                  </span>
                  <span className="text-data" style={{ color: '#FFFFFF' }}>
                    {kpi.value}
                  </span>
                  <span
                    className="text-xs mt-1"
                    style={{ color: kpi.positive ? '#8B1A2B' : '#666666' }}
                  >
                    {kpi.sub}
                  </span>
                  <div
                    className="absolute left-0 top-0 bottom-0 w-0 group-hover:w-0.5 transition-all duration-300"
                    style={{ backgroundColor: '#8B1A2B' }}
                  />
                </div>
              ))}
            </div>
          </div>
        </ScrollReveal>

        {/* Revenue & Profit Trend */}
        <ScrollReveal className="mt-20">
          <div className="w-full border" style={{ borderColor: '#333333', backgroundColor: '#0A0A0A' }}>
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between p-5 border-b" style={{ borderColor: '#333333' }}>
              <div>
                <h2 className="font-heading text-2xl font-semibold" style={{ color: '#FFFFFF' }}>
                  營收與獲利趨勢
                </h2>
                <span className="text-caption block mt-1" style={{ color: '#666666' }}>
                  REVENUE & PROFIT TREND
                </span>
              </div>
              <div className="flex gap-3 mt-4 sm:mt-0">
                <span className="text-xs cursor-pointer" style={{ color: '#FFFFFF' }}>百萬</span>
                <span className="text-xs cursor-pointer" style={{ color: '#666666' }}>十億</span>
              </div>
            </div>

            {/* Custom combined bar+line chart */}
            <div className="p-5">
              <svg width="100%" height={420} viewBox="0 0 900 420" className="w-full">
                {/* Grid */}
                {[0, 1, 2, 3, 4, 5].map((i) => {
                  const y = 40 + (i / 5) * 320
                  return (
                    <line key={`g-${i}`} x1={80} y1={y} x2={860} y2={y} stroke="#333333" strokeWidth={1} strokeDasharray="4 4" />
                  )
                })}
                {/* Y axis labels */}
                {['100B', '80B', '60B', '40B', '20B', '0'].map((l, i) => (
                  <text key={`y-${i}`} x={75} y={40 + (i / 5) * 320 + 4} textAnchor="end" fill="#666666" fontSize={11}>
                    {l}
                  </text>
                ))}

                {/* Bars */}
                {revCombined.map((d, i) => {
                  const groupX = 80 + (i / 12) * 780
                  const barW = 16
                  const gap = 2
                  const baseY = 40 + 320

                  return (
                    <g key={`bg-${i}`}>
                      {d.r23 !== null && (
                        <rect
                          className="bar-anim"
                          x={groupX - barW - gap}
                          y={baseY - (d.r23 / 100) * 320}
                          width={barW}
                          height={(d.r23 / 100) * 320}
                          fill="#333333"
                        />
                      )}
                      {d.r24 !== null && (
                        <rect
                          className="bar-anim"
                          x={groupX - barW / 2}
                          y={baseY - (d.r24 / 100) * 320}
                          width={barW}
                          height={(d.r24 / 100) * 320}
                          fill="#666666"
                        />
                      )}
                      {d.r25 !== null && (
                        <rect
                          className="bar-anim"
                          x={groupX + gap}
                          y={baseY - (d.r25 / 100) * 320}
                          width={barW}
                          height={(d.r25 / 100) * 320}
                          fill="#FFFFFF"
                        />
                      )}
                      <text x={groupX} y={385} textAnchor="middle" fill="#666666" fontSize={10}>
                        {d.q}
                      </text>
                    </g>
                  )
                })}

                {/* Net income line */}
                <polyline
                  points={revCombined.map((d, i) => {
                    const x = 80 + (i / 12) * 780
                    const y = 40 + 320 - (d.ni / 100) * 320
                    return `${x},${y}`
                  }).join(' ')}
                  fill="none"
                  stroke="#8B1A2B"
                  strokeWidth={2}
                />
                {revCombined.map((d, i) => {
                  const x = 80 + (i / 12) * 780
                  const y = 40 + 320 - (d.ni / 100) * 320
                  return <circle key={`ni-${i}`} cx={x} cy={y} r={4} fill="#8B1A2B" />
                })}

                {/* Legend */}
                <g transform="translate(350, 410)">
                  <rect x={0} y={-8} width={12} height={10} fill="#333333" />
                  <text x={18} y={0} fill="#666666" fontSize={10}>2023營收</text>
                  <rect x={80} y={-8} width={12} height={10} fill="#666666" />
                  <text x={98} y={0} fill="#666666" fontSize={10}>2024營收</text>
                  <rect x={160} y={-8} width={12} height={10} fill="#FFFFFF" />
                  <text x={178} y={0} fill="#666666" fontSize={10}>2025營收</text>
                  <circle cx={265} cy={-3} r={4} fill="#8B1A2B" />
                  <text x={275} y={0} fill="#666666" fontSize={10}>淨利</text>
                </g>
              </svg>
            </div>
          </div>
        </ScrollReveal>

        {/* Profitability Analysis */}
        <StaggerContainer className="mt-20" staggerDelay={0.15}>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            {/* Gross Margin */}
            <div className="stagger-item border p-5" style={{ borderColor: '#333333', backgroundColor: '#0A0A0A' }}>
              <h3 className="font-heading text-lg font-medium mb-4" style={{ color: '#FFFFFF' }}>
                毛利率趨勢
              </h3>
              <svg width="100%" height={160} viewBox="0 0 300 160">
                <polyline
                  points={profitabilityData.grossMargin.map((v, i) => {
                    const x = 20 + (i / 11) * 260
                    const y = 140 - ((v - 35) / 10) * 120
                    return `${x},${y}`
                  }).join(' ')}
                  fill="none"
                  stroke="#FFFFFF"
                  strokeWidth={2}
                />
                {profitabilityData.grossMargin.map((v, i) => {
                  const x = 20 + (i / 11) * 260
                  const y = 140 - ((v - 35) / 10) * 120
                  return <circle key={i} cx={x} cy={y} r={3} fill="#FFFFFF" />
                })}
                <text x={280} y={140 - ((42.8 - 35) / 10) * 120 + 4} textAnchor="end" fill="#FFFFFF" fontSize={14} fontWeight={600}>
                  42.8%
                </text>
              </svg>
            </div>

            {/* Operating Margin */}
            <div className="stagger-item border p-5" style={{ borderColor: '#333333', backgroundColor: '#0A0A0A' }}>
              <h3 className="font-heading text-lg font-medium mb-4" style={{ color: '#FFFFFF' }}>
                營業利益率
              </h3>
              <svg width="100%" height={160} viewBox="0 0 300 160">
                <polyline
                  points={profitabilityData.operatingMargin.map((v, i) => {
                    const x = 20 + (i / 11) * 260
                    const y = 140 - ((v - 10) / 10) * 120
                    return `${x},${y}`
                  }).join(' ')}
                  fill="none"
                  stroke="#8B1A2B"
                  strokeWidth={2}
                />
                {profitabilityData.operatingMargin.map((v, i) => {
                  const x = 20 + (i / 11) * 260
                  const y = 140 - ((v - 10) / 10) * 120
                  return <circle key={i} cx={x} cy={y} r={3} fill="#8B1A2B" />
                })}
                <text x={280} y={140 - ((18.2 - 10) / 10) * 120 + 4} textAnchor="end" fill="#FFFFFF" fontSize={14} fontWeight={600}>
                  18.2%
                </text>
              </svg>
            </div>

            {/* Net Margin */}
            <div className="stagger-item border p-5" style={{ borderColor: '#333333', backgroundColor: '#0A0A0A' }}>
              <h3 className="font-heading text-lg font-medium mb-4" style={{ color: '#FFFFFF' }}>
                淨利率
              </h3>
              <svg width="100%" height={160} viewBox="0 0 300 160">
                <polyline
                  points={profitabilityData.netMargin.map((v, i) => {
                    const x = 20 + (i / 11) * 260
                    const y = 140 - ((v - 15) / 10) * 120
                    return `${x},${y}`
                  }).join(' ')}
                  fill="none"
                  stroke="#666666"
                  strokeWidth={2}
                />
                {profitabilityData.netMargin.map((v, i) => {
                  const x = 20 + (i / 11) * 260
                  const y = 140 - ((v - 15) / 10) * 120
                  return <circle key={i} cx={x} cy={y} r={3} fill="#666666" />
                })}
                <text x={280} y={140 - ((21.9 - 15) / 10) * 120 + 4} textAnchor="end" fill="#FFFFFF" fontSize={14} fontWeight={600}>
                  21.9%
                </text>
              </svg>
            </div>
          </div>
        </StaggerContainer>

        {/* Cash Flow */}
        <ScrollReveal className="mt-20">
          <div className="w-full border" style={{ borderColor: '#333333', backgroundColor: '#0A0A0A' }}>
            <div className="p-5 border-b" style={{ borderColor: '#333333' }}>
              <h2 className="font-heading text-2xl font-semibold" style={{ color: '#FFFFFF' }}>
                現金流量分析
              </h2>
              <span className="text-caption block mt-1" style={{ color: '#666666' }}>
                CASH FLOW STATEMENT
              </span>
            </div>
            <div className="p-5">
              <StackedBarChart
                data={cashFlowData.labels.map((label, i) => ({
                  label,
                  values: [
                    cashFlowData.operating[i],
                    cashFlowData.investing[i],
                    cashFlowData.financing[i],
                  ],
                }))}
                colors={['#FFFFFF', '#666666', '#333333']}
                labels={['營業活動', '投資活動', '籌資活動', '自由現金流']}
                yAxisLabels={['-10B', '0', '10B', '20B', '30B']}
                lineData={cashFlowData.freeCashFlow}
              />
            </div>
          </div>
        </ScrollReveal>

        {/* Radar Chart + Ratio Table */}
        <div className="mt-20 grid grid-cols-1 lg:grid-cols-2 gap-0 border" style={{ borderColor: '#333333' }}>
          {/* Radar */}
          <ScrollReveal className="p-5 flex flex-col items-center" style={{ borderRight: '1px solid #333333' }}>
            <h2 className="font-heading text-2xl font-semibold mb-6" style={{ color: '#FFFFFF' }}>
              財務健康度
            </h2>
            <RadarChart dimensions={radarData} />
          </ScrollReveal>

          {/* Ratio Table */}
          <ScrollReveal className="p-5">
            <h2 className="font-heading text-2xl font-semibold mb-4" style={{ color: '#FFFFFF' }}>
              關鍵財務比率
            </h2>
            <DataTable
              headers={['比率名稱', '當期', '上期', '變動', '趨勢']}
              rows={ratioTableData.map((r) => [
                r.name,
                r.current,
                r.prev,
                r.change,
                r.trend === 'up' ? '↑' : '↓',
              ])}
              darkMode={true}
              compact={true}
            />
          </ScrollReveal>
        </div>

        {/* Balance Sheet */}
        <ScrollReveal className="mt-20">
          <div className="w-full border" style={{ borderColor: '#333333', backgroundColor: '#0A0A0A' }}>
            <div className="p-5 border-b" style={{ borderColor: '#333333' }}>
              <h2 className="font-heading text-2xl font-semibold" style={{ color: '#FFFFFF' }}>
                資產負債結構
              </h2>
              <span className="text-caption block mt-1" style={{ color: '#666666' }}>
                BALANCE SHEET OVERVIEW
              </span>
            </div>
            <div className="grid grid-cols-1 lg:grid-cols-2">
              {/* Assets */}
              <div className="p-5" style={{ borderRight: '1px solid #333333' }}>
                <h3 className="text-sm font-medium mb-4" style={{ color: '#FFFFFF' }}>
                  資產結構
                </h3>
                {/* Stacked bar */}
                <div className="flex h-10 w-full mb-4">
                  <div className="h-full" style={{ width: '55%', backgroundColor: '#FFFFFF' }} />
                  <div className="h-full" style={{ width: '30%', backgroundColor: '#666666' }} />
                  <div className="h-full" style={{ width: '15%', backgroundColor: '#333333' }} />
                </div>
                <div className="flex flex-col gap-2">
                  {balanceSheetData.assets.map((a, i) => (
                    <div key={i} className="flex items-center gap-2">
                      <div className="w-3 h-3" style={{ backgroundColor: a.color }} />
                      <span className="text-sm flex-1" style={{ color: a.color === '#FFFFFF' ? '#FFFFFF' : '#999999' }}>
                        {a.label}
                      </span>
                      <span className="text-sm" style={{ color: '#FFFFFF' }}>
                        {a.value}
                      </span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Liabilities */}
              <div className="p-5">
                <h3 className="text-sm font-medium mb-4" style={{ color: '#FFFFFF' }}>
                  負債與權益
                </h3>
                <div className="flex h-10 w-full mb-4">
                  <div className="h-full" style={{ width: '35%', backgroundColor: '#666666' }} />
                  <div className="h-full" style={{ width: '25%', backgroundColor: '#333333' }} />
                  <div className="h-full" style={{ width: '40%', backgroundColor: '#FFFFFF' }} />
                </div>
                <div className="flex flex-col gap-2">
                  {balanceSheetData.liabilities.map((l, i) => (
                    <div key={i} className="flex items-center gap-2">
                      <div className="w-3 h-3" style={{ backgroundColor: l.color }} />
                      <span className="text-sm flex-1" style={{ color: l.color === '#FFFFFF' ? '#FFFFFF' : '#999999' }}>
                        {l.label}
                      </span>
                      <span className="text-sm" style={{ color: '#FFFFFF' }}>
                        {l.value}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        </ScrollReveal>

        {/* DuPont Analysis */}
        <ScrollReveal className="mt-20 mb-20">
          <div className="w-full border" style={{ borderColor: '#333333', backgroundColor: '#0A0A0A' }}>
            <div className="p-5 border-b" style={{ borderColor: '#333333' }}>
              <h2 className="font-heading text-2xl font-semibold" style={{ color: '#FFFFFF' }}>
                杜邦分析
              </h2>
              <span className="text-caption block mt-1" style={{ color: '#666666' }}>
                DUPONT ANALYSIS
              </span>
            </div>
            <div className="p-5">
              {/* Decomposition tree */}
              <div className="flex flex-col md:flex-row items-center justify-center gap-4 md:gap-8 mb-8">
                {/* ROE */}
                <div
                  className="border p-5 text-center"
                  style={{ borderColor: '#8B1A2B', minWidth: 140 }}
                >
                  <span className="text-caption block mb-1" style={{ color: '#666666' }}>
                    ROE
                  </span>
                  <span className="text-data" style={{ color: '#FFFFFF' }}>
                    {dupontData.roe}
                  </span>
                </div>

                {/* Equals */}
                <span className="text-2xl" style={{ color: '#666666' }}>
                  =
                </span>

                {/* Three factors */}
                <div className="flex flex-col sm:flex-row items-center gap-3">
                  {[
                    { label: '淨利率', value: dupontData.netMargin },
                    { label: '資產周轉率', value: dupontData.assetTurnover },
                    { label: '權益乘數', value: dupontData.equityMultiplier },
                  ].map((f, i) => (
                    <div key={i} className="flex items-center gap-3">
                      <div
                        className="border p-4 text-center"
                        style={{ borderColor: '#333333', minWidth: 100 }}
                      >
                        <span className="text-caption block mb-1" style={{ color: '#666666' }}>
                          {f.label}
                        </span>
                        <span className="text-lg font-medium" style={{ color: '#FFFFFF' }}>
                          {f.value}
                        </span>
                      </div>
                      {i < 2 && <span className="text-lg" style={{ color: '#666666' }}>×</span>}
                    </div>
                  ))}
                </div>
              </div>

              {/* Comparison table */}
              <DataTable
                headers={['指標', '當期', '同期業平均', '差異']}
                rows={dupontData.table.map((r) => [
                  r.metric,
                  r.current,
                  r.industry,
                  <span key={r.metric} style={{ color: r.positive ? '#8B1A2B' : '#666666' }}>
                    {r.diff}
                  </span>,
                ])}
                darkMode={true}
              />
            </div>
          </div>
        </ScrollReveal>

        <div className="h-10" />
      </div>

      <Footer isDark={true} />
    </div>
  )
}
