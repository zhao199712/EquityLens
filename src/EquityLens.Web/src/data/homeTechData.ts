// 科技感首頁展示用 mock 資料(deterministic,不接 API)

export interface HeroKpi {
  label: string
  value: number
  prefix: string
  suffix: string
  decimals: number
  sub: string
  tone: 'neutral' | 'positive' | 'negative'
}

export interface TickerItem {
  code: string
  name: string
  price: string
  changePct: number
}

export interface EquityPoint {
  date: string
  value: number
}

export type EquityRange = '1M' | '3M' | '6M' | '1Y'

export interface AllocationSlice {
  name: string
  value: number
}

export interface RiskRadarData {
  indicators: string[]
  scores: number[]
}

export interface VarMetric {
  label: string
  value: number // 百分比,負值代表損失
}

export interface AiFeedItem {
  title: string
  kind: 'Research' | 'Agent Run' | 'Risk Run' | 'Report'
  status: 'completed' | 'running' | 'failed'
  time: string
}

export const heroKpis: HeroKpi[] = [
  { label: 'Total Assets', value: 12580000, prefix: 'NT$', suffix: '', decimals: 0, sub: '較上月 +2.4%', tone: 'neutral' },
  { label: 'Total Return', value: 18.72, prefix: '+', suffix: '%', decimals: 2, sub: 'YTD 年化 21.3%', tone: 'positive' },
  { label: 'Sharpe Ratio', value: 1.42, prefix: '', suffix: '', decimals: 2, sub: '滾動 252 日', tone: 'neutral' },
  { label: 'Max Drawdown', value: -8.4, prefix: '', suffix: '%', decimals: 1, sub: '過去一年', tone: 'negative' },
]

export const tickerItems: TickerItem[] = [
  { code: '2330', name: '台積電', price: '1,085.00', changePct: 1.64 },
  { code: '2317', name: '鴻海', price: '212.50', changePct: -0.70 },
  { code: '2454', name: '聯發科', price: '1,420.00', changePct: 2.31 },
  { code: '2308', name: '台達電', price: '398.00', changePct: 0.88 },
  { code: '2382', name: '廣達', price: '286.50', changePct: -1.21 },
  { code: '0050', name: '元大台灣50', price: '182.35', changePct: 0.52 },
  { code: '2881', name: '富邦金', price: '88.40', changePct: 0.11 },
  { code: '2412', name: '中華電', price: '128.50', changePct: -0.39 },
  { code: '2603', name: '長榮', price: '196.00', changePct: 1.03 },
  { code: '00679B', name: '元大美債20年', price: '28.76', changePct: -0.14 },
]

// 固定 seed 的偽隨機產生器,保證每次 render 曲線一致
function mulberry32(seed: number) {
  return () => {
    seed |= 0
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function generateEquityCurve(days: number, seed: number, startValue: number, drift: number): EquityPoint[] {
  const rand = mulberry32(seed)
  const points: EquityPoint[] = []
  const today = new Date('2026-06-03T00:00:00')
  let value = startValue
  for (let i = days - 1; i >= 0; i--) {
    const date = new Date(today)
    date.setDate(today.getDate() - i)
    const shock = (rand() - 0.48) * 0.018 + drift
    value = Math.max(Math.round(value * (1 + shock)), 1000000)
    points.push({
      date: date.toISOString().slice(0, 10),
      value,
    })
  }
  return points
}

export const equityCurves: Record<EquityRange, EquityPoint[]> = {
  '1M': generateEquityCurve(22, 101, 12200000, 0.0012),
  '3M': generateEquityCurve(66, 202, 11680000, 0.0010),
  '6M': generateEquityCurve(132, 303, 10950000, 0.0009),
  '1Y': generateEquityCurve(252, 404, 10580000, 0.0007),
}

export const equityRanges: EquityRange[] = ['1M', '3M', '6M', '1Y']

export const allocation: AllocationSlice[] = [
  { name: '半導體', value: 38 },
  { name: '電子代工', value: 17 },
  { name: '金融', value: 14 },
  { name: 'ETF', value: 16 },
  { name: '債券', value: 9 },
  { name: '現金', value: 6 },
]

export const allocationColors = ['#22d3ee', '#38bdf8', '#818cf8', '#34d399', '#fbbf24', '#475569']

export const riskRadar: RiskRadarData = {
  indicators: ['成長', '動能', '價值', '品質', '波動', '流動性'],
  scores: [82, 74, 58, 88, 46, 79],
}

export const varMetrics: VarMetric[] = [
  { label: 'VaR 95%', value: -2.3 },
  { label: 'VaR 99%', value: -3.8 },
  { label: 'ES 97.5%', value: -4.6 },
]

export const aiFeed: AiFeedItem[] = [
  { title: '台積電 2026 Q1 法說會重點摘要', kind: 'Research', status: 'completed', time: '2 小時前' },
  { title: 'CriticReview:半導體配置證據檢查', kind: 'Agent Run', status: 'running', time: '進行中' },
  { title: '科技成長組合 Monte Carlo 風險試算', kind: 'Risk Run', status: 'completed', time: '5 小時前' },
  { title: '鴻海 FII 供應鏈研究備忘錄', kind: 'Report', status: 'completed', time: '1 天前' },
  { title: 'DraftRevision:月報答案修訂', kind: 'Agent Run', status: 'failed', time: '2 天前' },
]
