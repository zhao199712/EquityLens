import { mount, flushPromises } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
vi.mock("../../../services/http", () => ({ http: {} }))
import PortfolioDetailView from '../PortfolioDetailView.vue'
import { getPortfolioRisk } from '../../../services/risk'
vi.mock('vue-router', () => ({ useRoute: () => ({ params: { id: 'p' } }), useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../../services/risk', () => ({
  getPortfolio: vi.fn().mockResolvedValue({ id: 'p', name: 'Empty', baseCurrency: 'TWD', holdings: [] }),
  getPortfolioValuation: vi.fn().mockResolvedValue({ holdings: [], totalMarketValue: 0, totalCostValue: 0, currency: 'TWD' }),
  getPortfolioValuationHistory: vi.fn().mockResolvedValue({ points: [], dailyValues: [], dailyReturns: [] }),
  getPortfolioRisk: vi.fn(), createPortfolioDiagnosis: vi.fn(),
}))
vi.mock('../../../services/portfolioTransactions', () => ({ getTransactions: vi.fn().mockResolvedValue([]), createTransaction: vi.fn(), deleteTransaction: vi.fn() }))
vi.mock('../../../services/portfolioDividends', () => ({ getDividendCashFlows: vi.fn().mockResolvedValue([]), getLatestDividends: vi.fn().mockResolvedValue([]) }))
vi.mock('../../../services/portfolioCashFlows', () => ({ getPortfolioCashFlows: vi.fn().mockResolvedValue([]), createPortfolioCashFlow: vi.fn(), updatePortfolioCashFlow: vi.fn(), deletePortfolioCashFlow: vi.fn() }))
it('does not fetch risk for an empty portfolio', async () => {
  const wrapper = mount(PortfolioDetailView, { global: { stubs: { ScrollReveal: { template: '<div><slot /></div>' }, LineChart: true, DonutChart: true, DataTable: true, NPagination: true } } })
  await flushPromises()
  expect(getPortfolioRisk).not.toHaveBeenCalled()
  expect(wrapper.text()).toContain('尚無持倉')
  wrapper.unmount()
})

vi.mock('../../../services/securities', () => ({ searchSecurities: vi.fn(), resolveSecurity: vi.fn() }))
import { searchSecurities } from '../../../services/securities'
import { createTransaction } from '../../../services/portfolioTransactions'
const security = (ticker: string) => ({ securityId: ticker, ticker, exchange: 'TWSE', name: ticker, source: 'Local', currency: 'TWD', assetType: null, isin: null, sector: null, industry: null })
const openForm = async () => {
  const w = mount(PortfolioDetailView, { global: { stubs: { ScrollReveal: { template: '<div><slot /></div>' }, LineChart: true, DonutChart: true, DataTable: true, NPagination: true } } })
  await flushPromises()
  await w.findAll('button').find(b => b.text() === '+ ADD STOCK')!.trigger('click')
  return w
}
it('clears a selected stock as soon as the search changes and never submits an ambiguous result', async () => {
  vi.mocked(searchSecurities).mockResolvedValueOnce([security('2330')]).mockResolvedValueOnce([security('2317'), security('2317A')])
  const w = await openForm()
  const input = w.find('input[placeholder^="搜尋股票代號"]')
  await input.setValue('2330'); await input.trigger('keydown', { key: 'Enter' }); await flushPromises()
  expect(w.text()).toContain('已選擇：2330')
  await input.setValue('2317')
  expect(w.text()).not.toContain('已選擇：2330')
  await input.trigger('keydown', { key: 'Enter' }); await flushPromises()
  expect(w.findAll('button').find(b => b.text() === 'ADD')!.attributes('disabled')).toBeDefined()
  expect(createTransaction).not.toHaveBeenCalled()
  w.unmount()
})
it('ignores an older search response arriving after the newer search', async () => {
  let finish!: (v: ReturnType<typeof security>[]) => void
  vi.mocked(searchSecurities).mockImplementationOnce(() => new Promise(r => { finish = r }))
    .mockResolvedValueOnce([security('2317')])
  const w = await openForm(); const input = w.find('input[placeholder^="搜尋股票代號"]')
  await input.setValue('2330'); await input.trigger('keydown', { key: 'Enter' })
  await input.setValue('2317'); await input.trigger('keydown', { key: 'Enter' }); await flushPromises()
  finish([security('2330')]); await flushPromises()
  expect(w.text()).toContain('已選擇：2317'); expect(w.text()).not.toContain('已選擇：2330')
  w.unmount()
})
