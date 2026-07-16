import { http } from './http'

export interface PortfolioCashFlow {
  id: string
  flowType: 'Deposit' | 'Withdrawal' | 'Fee' | 'Dividend' | 'DividendTax'
  amount: number
  currency: string
  effectiveDate: string
  status: string
  isUserAdjusted: boolean
  note: string | null
  securityId: string | null
  isSystemDerived: boolean
}

export interface UpsertCashFlowRequest {
  flowType: 'Deposit' | 'Withdrawal' | 'Fee' | 'DividendTax'
  amount: number
  effectiveDate: string
  note?: string | null
}

export async function getPortfolioCashFlows(portfolioId: string): Promise<PortfolioCashFlow[]> {
  const response = await http.get<PortfolioCashFlow[]>(`/portfolios/${portfolioId}/cash-flows`)
  return response.data
}

export async function createPortfolioCashFlow(portfolioId: string, request: UpsertCashFlowRequest): Promise<PortfolioCashFlow> {
  const response = await http.post<PortfolioCashFlow>(`/portfolios/${portfolioId}/cash-flows`, request)
  return response.data
}

export async function updatePortfolioCashFlow(portfolioId: string, id: string, request: UpsertCashFlowRequest): Promise<PortfolioCashFlow> {
  const response = await http.put<PortfolioCashFlow>(`/portfolios/${portfolioId}/cash-flows/${id}`, request)
  return response.data
}

export async function deletePortfolioCashFlow(portfolioId: string, id: string): Promise<void> {
  await http.delete(`/portfolios/${portfolioId}/cash-flows/${id}`)
}
