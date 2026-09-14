export const financialKPIData = [
  { label: 'REVENUE', value: 'NT$ 85.2B', sub: 'YoY +12.3%', positive: true },
  { label: 'NET INCOME', value: 'NT$ 18.7B', sub: 'YoY +8.5%', positive: true },
  { label: 'GROSS MARGIN', value: '42.8%', sub: '+1.2pp', positive: true },
  { label: 'EARNINGS/SHARE', value: 'NT$ 7.24', sub: 'YoY +9.1%', positive: true },
  { label: 'RETURN ON EQUITY', value: '18.5%', sub: '+0.8pp', positive: true },
]

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

export const radarData = [
  { label: '流動性', value: 85, max: 100 },
  { label: '償債能力', value: 78, max: 100 },
  { label: '獲利能力', value: 92, max: 100 },
  { label: '成長性', value: 88, max: 100 },
  { label: '營運效率', value: 82, max: 100 },
  { label: '現金品質', value: 90, max: 100 },
]

export const ratioTableData = [
  { name: '流動比率', current: '185%', prev: '172%', change: '+13pp', trend: 'up' as const },
  { name: '速動比率', current: '142%', prev: '135%', change: '+7pp', trend: 'up' as const },
  { name: '負債比率', current: '38.5%', prev: '41.2%', change: '-2.7pp', trend: 'down' as const },
  { name: '利息保障倍數', current: '12.4x', prev: '10.8x', change: '+1.6x', trend: 'up' as const },
  { name: '資產報酬率 ROA', current: '14.2%', prev: '13.5%', change: '+0.7pp', trend: 'up' as const },
  { name: '存貨周轉天數', current: '45天', prev: '52天', change: '-7天', trend: 'up' as const },
  { name: '應收帳款周轉天數', current: '38天', prev: '42天', change: '-4天', trend: 'up' as const },
]

export const balanceSheetData = {
  assets: [
    { label: '現金及約當現金', value: 'NT$28.5B', ratio: 28.5, color: '#FFFFFF' },
    { label: '應收帳款', value: 'NT$15.2B', ratio: 15.2, color: '#666666' },
    { label: '存貨', value: 'NT$12.8B', ratio: 12.8, color: '#333333' },
    { label: '固定資產淨額', value: 'NT$22.4B', ratio: 22.4, color: '#999999' },
    { label: '其他', value: 'NT$6.3B', ratio: 6.3, color: '#555555' },
  ],
  liabilities: [
    { label: '短期借款', value: 'NT$8.2B', ratio: 8.2, color: '#666666' },
    { label: '長期負債', value: 'NT$12.5B', ratio: 12.5, color: '#333333' },
    { label: '股本', value: 'NT$15.0B', ratio: 15.0, color: '#FFFFFF' },
    { label: '保留盈餘', value: 'NT$28.0B', ratio: 28.0, color: '#888888' },
    { label: '其他權益', value: 'NT$5.2B', ratio: 5.2, color: '#444444' },
  ],
}

export const dupontData = {
  roe: '18.5%',
  netMargin: '21.9%',
  assetTurnover: '0.68',
  equityMultiplier: '1.64',
  table: [
    { metric: '淨利率', current: '21.9%', industry: '18.5%', diff: '+3.4pp', positive: true },
    { metric: '資產周轉率', current: '0.68', industry: '0.72', diff: '-0.04', positive: false },
    { metric: '權益乘數', current: '1.64', industry: '1.58', diff: '+0.06', positive: true },
    { metric: 'ROE', current: '18.5%', industry: '16.2%', diff: '+2.3pp', positive: true },
  ],
}

export const revCombined = [
  { q: '23Q1', r23: 18.2, r24: null as number | null, r25: null as number | null, ni: 3.8 },
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
