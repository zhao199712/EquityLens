export const kpiData = [
  { label: 'TOTAL ASSETS', value: 'NT$ 12,580,000', sub: '較上月 +2.3%' },
  { label: 'TOTAL RETURN', value: '+18.72%', sub: '年化報酬' },
  { label: 'PORTFOLIO BETA', value: '1.08', sub: '相對大盤' },
  { label: 'SHARPE RATIO', value: '1.42', sub: '風險調整後報酬' },
]

export const portfolioValueData = {
  labels: ['1月', '2月', '3月', '4月', '5月', '6月', '7月', '8月', '9月', '10月', '11月', '12月'],
  values: [
    10580000, 10720000, 10200000, 10850000, 11020000, 11180000,
    11500000, 12850000, 12030000, 11890000, 12250000, 12580000,
  ],
  yAxisLabels: ['NT$8M', 'NT$9M', 'NT$10M', 'NT$11M', 'NT$12M', 'NT$13M', 'NT$14M'],
  summary: {
    start: 'NT$10,580,000',
    high: 'NT$12,850,000 (2025.08)',
    low: 'NT$10,200,000 (2024.03)',
  },
}

export const allocationData = {
  segments: [
    { label: '股票', value: 45, color: '#000000' },
    { label: '債券', value: 25, color: '#666666' },
    { label: 'ETF', value: 18, color: '#E0E0E0' },
    { label: '現金', value: 12, color: '#F5F5F5', borderColor: '#E0E0E0' },
  ],
  holdings: [
    { code: '2330', name: '台積電', category: '股票', shares: 100, price: 'NT$985', value: 'NT$98,500', ratio: '7.83%', pnl: '+12.4%' },
    { code: '0050', name: '元大台灣50', category: 'ETF', shares: 500, price: 'NT$175.2', value: 'NT$87,600', ratio: '6.96%', pnl: '+8.2%' },
    { code: '2317', name: '鴻海', category: '股票', shares: 200, price: 'NT$198.5', value: 'NT$39,700', ratio: '3.15%', pnl: '-2.1%' },
    { code: '00878', name: '國泰永續高股息', category: 'ETF', shares: 800, price: 'NT$22.35', value: 'NT$17,880', ratio: '1.42%', pnl: '+15.8%' },
    { code: '00679B', name: '元大美債20年', category: '債券', shares: 300, price: 'NT$42.8', value: 'NT$12,840', ratio: '1.02%', pnl: '-5.3%' },
  ],
}

export const performanceAttribution = {
  sector: [
    { label: '半導體', value: 4.2 },
    { label: '電子製造', value: 2.8 },
    { label: '金融', value: 1.5 },
    { label: '傳產', value: 0.6 },
  ],
  alpha: [
    { name: '台積電', value: 2.35 },
    { name: '鴻海', value: -0.85 },
    { name: '聯發科', value: 1.12 },
    { name: '台達電', value: 0.67 },
  ],
  monthlyReturns: [2.1, -1.5, 3.2, 1.8, -0.5, 2.4, 4.1, -2.3, 1.2, 0.8, 3.5, 1.9],
  risk: {
    volatility: '12.4%',
    maxDrawdown: '-8.2%',
    sortino: '1.85',
    infoRatio: '0.92',
  },
}

export const riskData = {
  scatter: [
    { x: 22, y: 18, label: '2330 台積電' },
    { x: 18, y: 12, label: '0050 台灣50' },
    { x: 20, y: 8, label: '2317 鴻海' },
    { x: 8, y: 6, label: '00878 高股息' },
    { x: 12, y: 4, label: '00679B 美債' },
    { x: 15, y: 10, label: '投組整體' },
  ],
  drawdown: {
    labels: ['1月', '2月', '3月', '4月', '5月', '6月', '7月', '8月', '9月', '10月', '11月', '12月'],
    values: [-2.1, -1.5, -8.2, -5.3, -3.1, -4.2, -1.8, -0.5, -3.5, -2.8, -1.2, -0.8],
  },
}
