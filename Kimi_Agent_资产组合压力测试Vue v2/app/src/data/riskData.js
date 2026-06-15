export const varResults = [
    { method: '歷史模擬法', var95: -4.82, var99: -7.65, description: '基於過去250個交易日實際報酬分布' },
    { method: '參數法（變異數-共變異數）', var95: -4.55, var99: -6.92, description: '假設報酬服從常態分布' },
    { method: '蒙地卡羅模擬', var95: -4.91, var99: -7.88, description: '10,000次隨機模擬路徑' },
    { method: '指數加權移動平均', var95: -4.68, var99: -7.21, description: 'EWMA λ=0.94 加權近期波動' },
];
export const varHistogram = [
    { bin: '<-6%', count: 8, isTail99: true, isTail95: false },
    { bin: '-6~-5%', count: 12, isTail99: true, isTail95: false },
    { bin: '-5~-4%', count: 15, isTail99: false, isTail95: true },
    { bin: '-4~-3%', count: 22, isTail99: false, isTail95: true },
    { bin: '-3~-2%', count: 35, isTail99: false, isTail95: false },
    { bin: '-2~-1%', count: 48, isTail99: false, isTail95: false },
    { bin: '-1~0%', count: 42, isTail99: false, isTail95: false },
    { bin: '0~1%', count: 38, isTail99: false, isTail95: false },
    { bin: '1~2%', count: 18, isTail99: false, isTail95: false },
    { bin: '2~3%', count: 8, isTail99: false, isTail95: false },
    { bin: '>3%', count: 4, isTail99: false, isTail95: false },
];
export const esResults = [
    { method: '歷史模擬法', es95: -6.12, es99: -9.35 },
    { method: '參數法', es95: -5.78, es99: -8.42 },
    { method: '蒙地卡羅模擬', es95: -6.28, es99: -9.68 },
    { method: 'EWMA', es95: -5.92, es99: -8.95 },
];
export const varEsComparison = [
    { confidence: '90%', var: -3.42, es: -4.85 },
    { confidence: '95%', var: -4.82, es: -6.12 },
    { confidence: '97.5%', var: -5.88, es: -7.35 },
    { confidence: '99%', var: -7.65, es: -9.35 },
    { confidence: '99.5%', var: -8.92, es: -10.68 },
];
export const stressScenarios = [
    {
        id: 's1',
        name: '2008 金融危機重演',
        description: '全球信用緊縮、流動性凍結、系統性風險爆發',
        impact: -42.5,
        severity: 'extreme',
        affectedSectors: ['金融', '科技', '房地產'],
        details: [
            { metric: '投組最大損失', value: '-42.5%' },
            { metric: '恢復期間（估計）', value: '18-24 個月' },
            { metric: '流動性衝擊', value: '嚴重' },
            { metric: '相關性趨近', value: '1.0（全面拋售）' },
        ],
    },
    {
        id: 's2',
        name: '地緣政治衝突升級',
        description: '台海緊張局勢、半導體供應鏈中斷',
        impact: -28.3,
        severity: 'high',
        affectedSectors: ['半導體', '電子製造', '航運'],
        details: [
            { metric: '投組最大損失', value: '-28.3%' },
            { metric: '恢復期間（估計）', value: '12-18 個月' },
            { metric: '供應鏈衝擊', value: '嚴重' },
            { metric: '台積電單股衝擊', value: '-35.2%' },
        ],
    },
    {
        id: 's3',
        name: '全球通膨失控',
        description: '央行急劇升息、資產價格重估、經濟衰退',
        impact: -22.8,
        severity: 'high',
        affectedSectors: ['科技', '不動產', '消費'],
        details: [
            { metric: '投組最大損失', value: '-22.8%' },
            { metric: '利率衝擊', value: '+300 bps' },
            { metric: '債券部位損失', value: '-12.5%' },
            { metric: '股票部位損失', value: '-28.1%' },
        ],
    },
    {
        id: 's4',
        name: 'AI 泡沫破裂',
        description: 'AI 投資過熱修正、科技股大幅回調',
        impact: -18.5,
        severity: 'medium',
        affectedSectors: ['科技', '半導體', '雲端'],
        details: [
            { metric: '投組最大損失', value: '-18.5%' },
            { metric: '台積電衝擊', value: '-22.8%' },
            { metric: '恢復期間（估計）', value: '6-12 個月' },
            { metric: '非科技股衝擊', value: '-5.2%' },
        ],
    },
    {
        id: 's5',
        name: '美國債務違約',
        description: '美債信用評等下調、全球避險潮、美元貶值',
        impact: -15.2,
        severity: 'medium',
        affectedSectors: ['金融', '債券', '匯率'],
        details: [
            { metric: '投組最大損失', value: '-15.2%' },
            { metric: '美債20年衝擊', value: '-18.5%' },
            { metric: '新台幣波動', value: '+8.2%' },
            { metric: '黃金避險需求', value: '+12.5%' },
        ],
    },
    {
        id: 's6',
        name: '新冠疫情級封控',
        description: '新型傳染病爆發、全球封控、需求萎縮',
        impact: -32.1,
        severity: 'high',
        affectedSectors: ['消費', '航運', '製造'],
        details: [
            { metric: '投組最大損失', value: '-32.1%' },
            { metric: '恢復期間（估計）', value: '9-15 個月' },
            { metric: 'VIX 峰值', value: '65+' },
            { metric: '流動性衝擊', value: '中等' },
        ],
    },
];
// ========== Risk Dashboard KPI ==========
export const riskKPIData = [
    { label: 'DAILY VaR (95%)', value: '-4.82%', sub: 'NT$606,556', color: '#FF6B00' },
    { label: 'DAILY ES (95%)', value: '-6.12%', sub: '預期損失 NT$769,896', color: '#8B1A2B' },
    { label: 'STRESS VaR', value: '-18.52%', sub: 'AI泡沫情境', color: '#8B1A2B' },
    { label: 'MAX DRAWDOWN', value: '-8.20%', sub: '歷史最大回撤', color: '#666666' },
    { label: 'CURRENT DRAWDOWN', value: '-2.15%', sub: '當前回撤水平', color: '#666666' },
];
// ========== Backtest Data ==========
export const backtestData = [
    { date: '2025-01', actual: 2.1, var95: -4.82, breached: false },
    { date: '2025-02', actual: -1.5, var95: -4.82, breached: false },
    { date: '2025-03', actual: -5.8, var95: -4.82, breached: true },
    { date: '2025-04', actual: 1.8, var95: -4.82, breached: false },
    { date: '2025-05', actual: -0.5, var95: -4.82, breached: false },
    { date: '2025-06', actual: 2.4, var95: -4.82, breached: false },
    { date: '2025-07', actual: 4.1, var95: -4.82, breached: false },
    { date: '2025-08', actual: -2.3, var95: -4.82, breached: false },
    { date: '2025-09', actual: 1.2, var95: -4.82, breached: false },
    { date: '2025-10', actual: 0.8, var95: -4.82, breached: false },
    { date: '2025-11', actual: 3.5, var95: -4.82, breached: false },
    { date: '2025-12', actual: -6.2, var95: -4.82, breached: true },
];
// ========== Monte Carlo Simulation Data ==========
export const monteCarloPaths = Array.from({ length: 20 }, (_, pathIdx) => {
    const path = [0];
    let value = 0;
    for (let i = 1; i <= 252; i++) {
        const drift = 0.0003;
        const shock = (Math.sin(i * 0.1 + pathIdx) * 0.015) + ((Math.random() - 0.5) * 0.02);
        value += drift + shock;
        path.push(value);
    }
    return path;
});
export const monteCarloPercentiles = {
    p1: Array.from({ length: 253 }, (_, i) => {
        const val = -0.15 * Math.sqrt(i / 252) - 0.02 * (i / 252);
        return val;
    }),
    p5: Array.from({ length: 253 }, (_, i) => {
        const val = -0.10 * Math.sqrt(i / 252) - 0.01 * (i / 252);
        return val;
    }),
    p50: Array.from({ length: 253 }, (_, i) => {
        const val = 0.05 * (i / 252);
        return val;
    }),
    p95: Array.from({ length: 253 }, (_, i) => {
        const val = 0.12 * Math.sqrt(i / 252) + 0.03 * (i / 252);
        return val;
    }),
    p99: Array.from({ length: 253 }, (_, i) => {
        const val = 0.18 * Math.sqrt(i / 252) + 0.04 * (i / 252);
        return val;
    }),
};
