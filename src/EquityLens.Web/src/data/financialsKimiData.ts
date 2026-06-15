// eslint-disable-next-line @typescript-eslint/no-explicit-any
type TFunction = (key: string, ...args: any[]) => string

export function getFinancialKPIData(t: TFunction) {
  return [
    { label: t('data.financial.revenue'), value: 'NT$ 85.2B', sub: 'YoY +12.3%', positive: true },
    { label: t('data.financial.netIncome'), value: 'NT$ 18.7B', sub: 'YoY +8.5%', positive: true },
    { label: t('data.financial.grossMargin'), value: '42.8%', sub: '+1.2pp', positive: true },
    { label: t('data.financial.earningsPerShare'), value: 'NT$ 7.24', sub: 'YoY +9.1%', positive: true },
    { label: t('data.financial.returnOnEquity'), value: '18.5%', sub: '+0.8pp', positive: true },
  ]
}

export const profitabilityData = {
  grossMargin: [38.5, 39.2, 39.8, 40.1, 40.5, 41.0, 41.5, 41.8, 42.0, 42.2, 42.5, 42.8],
  operatingMargin: [14.2, 14.8, 15.1, 15.5, 15.8, 16.2, 16.5, 16.8, 17.0, 17.5, 17.8, 18.2],
  netMargin: [18.5, 18.8, 18.2, 18.5, 18.8, 19.0, 19.5, 19.8, 20.2, 20.5, 21.0, 21.9],
}

export const cashFlowData = {
  labels: ['23Q1', '23Q2', '23Q3', '23Q4', '24Q1', '24Q2', '24Q3', '24Q4', '25Q1', '25Q2', '25Q3', '25Q4'],
  operating: [8.2, 9.5, 8.8, 10.2, 9.5, 11.2, 10.8, 12.5, 11.8, 13.2, 14.5, 15.8],
  investing: [-5.2, -6.8, -4.5, -7.2, -5.8, -8.5, -6.2, -9.1, -7.5, -8.8, -7.2, -9.5],
  financing: [-2.1, -1.5, -2.8, -1.2, -2.5, -1.8, -3.2, -1.5, -2.8, -2.2, -3.5, -2.1],
  freeCashFlow: [3.0, 2.7, 4.3, 3.0, 3.7, 2.7, 4.6, 3.4, 4.3, 4.4, 7.3, 6.3],
}

export function getRadarData(t: TFunction) {
  return [
    { label: t('data.radar.liquidity'), value: 85, max: 100 },
    { label: t('data.radar.solvency'), value: 78, max: 100 },
    { label: t('data.radar.profitability'), value: 92, max: 100 },
    { label: t('data.radar.growth'), value: 88, max: 100 },
    { label: t('data.radar.efficiency'), value: 82, max: 100 },
    { label: t('data.radar.cashQuality'), value: 90, max: 100 },
  ]
}

export function getRatioTableData(t: TFunction) {
  return [
    { name: t('data.ratios.currentRatio'), current: '185%', prev: '172%', change: '+13pp', trend: 'up' as const },
    { name: t('data.ratios.quickRatio'), current: '142%', prev: '135%', change: '+7pp', trend: 'up' as const },
    { name: t('data.ratios.debtRatio'), current: '38.5%', prev: '41.2%', change: '-2.7pp', trend: 'down' as const },
    { name: t('data.ratios.interestCoverage'), current: '12.4x', prev: '10.8x', change: '+1.6x', trend: 'up' as const },
    { name: t('data.ratios.roa'), current: '14.2%', prev: '13.5%', change: '+0.7pp', trend: 'up' as const },
    { name: t('data.ratios.inventoryDays'), current: '45天', prev: '52天', change: '-7天', trend: 'up' as const },
    { name: t('data.ratios.receivableDays'), current: '38天', prev: '42天', change: '-4天', trend: 'up' as const },
  ]
}

export function getBalanceSheetData(t: TFunction) {
  return {
    assets: [
      { label: t('data.balanceSheet.cash'), value: 'NT$28.5B', ratio: 28.5, color: '#FFFFFF' },
      { label: t('data.balanceSheet.receivables'), value: 'NT$15.2B', ratio: 15.2, color: '#666666' },
      { label: t('data.balanceSheet.inventory'), value: 'NT$12.8B', ratio: 12.8, color: '#333333' },
      { label: t('data.balanceSheet.fixedAssets'), value: 'NT$22.4B', ratio: 22.4, color: '#999999' },
      { label: t('data.balanceSheet.other'), value: 'NT$6.3B', ratio: 6.3, color: '#555555' },
    ],
    liabilities: [
      { label: t('data.balanceSheet.shortTermDebt'), value: 'NT$8.2B', ratio: 8.2, color: '#666666' },
      { label: t('data.balanceSheet.longTermDebt'), value: 'NT$12.5B', ratio: 12.5, color: '#333333' },
      { label: t('data.balanceSheet.equity'), value: 'NT$15.0B', ratio: 15.0, color: '#FFFFFF' },
      { label: t('data.balanceSheet.retainedEarnings'), value: 'NT$28.0B', ratio: 28.0, color: '#888888' },
      { label: t('data.balanceSheet.otherEquity'), value: 'NT$5.2B', ratio: 5.2, color: '#444444' },
    ],
  }
}

export function getDupontData(t: TFunction) {
  return {
    roe: '18.5%',
    netMargin: '21.9%',
    assetTurnover: '0.68',
    equityMultiplier: '1.64',
    table: [
      { metric: t('data.dupont.netMargin'), current: '21.9%', industry: '18.5%', diff: '+3.4pp', positive: true },
      { metric: t('data.dupont.assetTurnover'), current: '0.68', industry: '0.72', diff: '-0.04', positive: false },
      { metric: t('data.dupont.equityMultiplier'), current: '1.64', industry: '1.58', diff: '+0.06', positive: true },
      { metric: t('data.dupont.roe'), current: '18.5%', industry: '16.2%', diff: '+2.3pp', positive: true },
    ],
  }
}

export const revenueProfitData = {
  quarters: ['23Q1', '23Q2', '23Q3', '23Q4', '24Q1', '24Q2', '24Q3', '24Q4', '25Q1', '25Q2', '25Q3', '25Q4'],
  r23: [18.2, 19.5, 20.1, 21.8, null, null, null, null, null, null, null, null],
  r24: [null, null, null, null, 22.5, 23.8, 24.2, 26.1, null, null, null, null],
  r25: [null, null, null, null, null, null, null, null, 25.8, 27.2, 28.5, 30.1],
  netIncome: [3.8, 4.2, 4.1, 4.8, 4.5, 5.1, 5.3, 6.2, 5.8, 6.5, 7.2, 8.0],
}
