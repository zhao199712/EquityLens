// eslint-disable-next-line @typescript-eslint/no-explicit-any
type TFunction = (key: string, ...args: any[]) => string

export function getKpiData(t: TFunction) {
  return [
    { label: t('data.kpi.totalAssets'), value: 'NT$ 12,580,000', sub: t('data.kpi.totalAssetsSub') },
    { label: t('data.kpi.totalReturn'), value: '+18.72%', sub: t('data.kpi.totalReturnSub') },
    { label: t('data.kpi.portfolioBeta'), value: '1.08', sub: t('data.kpi.portfolioBetaSub') },
    { label: t('data.kpi.sharpeRatio'), value: '1.42', sub: t('data.kpi.sharpeRatioSub') },
  ]
}

export function getPortfolioValueData(t: TFunction) {
  return {
    labels: [
      t('data.months.jan'), t('data.months.feb'), t('data.months.mar'), t('data.months.apr'),
      t('data.months.may'), t('data.months.jun'), t('data.months.jul'), t('data.months.aug'),
      t('data.months.sep'), t('data.months.oct'), t('data.months.nov'), t('data.months.dec'),
    ],
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
}

export function getAllocationData(t: TFunction) {
  return {
    segments: [
      { label: t('data.allocation.stocks'), value: 45, color: '#000000' },
      { label: t('data.allocation.bonds'), value: 25, color: '#666666' },
      { label: t('data.allocation.etf'), value: 18, color: '#E0E0E0' },
      { label: t('data.allocation.cash'), value: 12, color: '#F5F5F5', borderColor: '#E0E0E0' },
    ],
    holdings: [
      { code: '2330', name: t('data.holdings.tsmc'), category: t('data.holdings.stock'), shares: 100, price: 'NT$985', value: 'NT$98,500', ratio: '7.83%', pnl: '+12.4%' },
      { code: '0050', name: t('data.holdings.yuantaTW50'), category: t('data.holdings.etf'), shares: 500, price: 'NT$175.2', value: 'NT$87,600', ratio: '6.96%', pnl: '+8.2%' },
      { code: '2317', name: t('data.holdings.honHai'), category: t('data.holdings.stock'), shares: 200, price: 'NT$198.5', value: 'NT$39,700', ratio: '3.15%', pnl: '-2.1%' },
      { code: '00878', name: t('data.holdings.cathaySustainDiv'), category: t('data.holdings.etf'), shares: 800, price: 'NT$22.35', value: 'NT$17,880', ratio: '1.42%', pnl: '+15.8%' },
      { code: '00679B', name: t('data.holdings.yuantaUSBond20Y'), category: t('data.holdings.bond'), shares: 300, price: 'NT$42.8', value: 'NT$12,840', ratio: '1.02%', pnl: '-5.3%' },
    ],
  }
}

export function getPerformanceAttribution(t: TFunction) {
  return {
    sector: [
      { label: t('data.performance.semiconductor'), value: 4.2 },
      { label: t('data.performance.electronics'), value: 2.8 },
      { label: t('data.performance.finance'), value: 1.5 },
      { label: t('data.performance.traditional'), value: 0.6 },
    ],
    alpha: [
      { name: t('data.alpha.tsmc'), value: 2.35 },
      { name: t('data.alpha.honHai'), value: -0.85 },
      { name: t('data.alpha.mediatek'), value: 1.12 },
      { name: t('data.alpha.delta'), value: 0.67 },
    ],
    monthlyReturns: [2.1, -1.5, 3.2, 1.8, -0.5, 2.4, 4.1, -2.3, 1.2, 0.8, 3.5, 1.9],
    risk: {
      volatility: '12.4%',
      maxDrawdown: '-8.2%',
      sortino: '1.85',
      infoRatio: '0.92',
    },
  }
}

export function getRiskData(t: TFunction) {
  return {
    scatter: [
      { x: 22, y: 18, label: `2330 ${t('data.alpha.tsmc')}` },
      { x: 18, y: 12, label: `0050 ${t('data.holdings.yuantaTW50')}` },
      { x: 20, y: 8, label: `2317 ${t('data.alpha.honHai')}` },
      { x: 8, y: 6, label: `00878 ${t('data.allocation.etf')}` },
      { x: 12, y: 4, label: `00679B ${t('data.allocation.bonds')}` },
      { x: 15, y: 10, label: '投組整體' },
    ],
    drawdown: {
      labels: [
        t('data.months.jan'), t('data.months.feb'), t('data.months.mar'), t('data.months.apr'),
        t('data.months.may'), t('data.months.jun'), t('data.months.jul'), t('data.months.aug'),
        t('data.months.sep'), t('data.months.oct'), t('data.months.nov'), t('data.months.dec'),
      ],
      values: [-2.1, -1.5, -8.2, -5.3, -3.1, -4.2, -1.8, -0.5, -3.5, -2.8, -1.2, -0.8],
    },
  }
}

export function getScenarioData(t: TFunction) {
  return [
    { name: t('data.scenario.marketCrash'), impact: '-28.5%', probability: t('data.scenario.probabilityLow'), description: t('data.scenario.marketCrashDesc') },
    { name: t('data.scenario.rateHike'), impact: '-12.3%', probability: t('data.scenario.probabilityMedium'), description: t('data.scenario.rateHikeDesc') },
    { name: t('data.scenario.techRebound'), impact: '+22.1%', probability: t('data.scenario.probabilityHigh'), description: t('data.scenario.techReboundDesc') },
  ]
}
