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
