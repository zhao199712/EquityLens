import { http } from './http'

export interface LatestDividendResponse {
  securityId: string
  ticker: string
  securityName: string
  exDividendDate: string
  paymentDate: string | null
  cashAmountPerShare: number
  currency: string
}

export interface DividendCashFlow {
  id: string
  securityId: string
  ticker: string
  securityName: string
  exDividendDate: string
  paymentDate: string | null
  sharesEntitled: number
  cashAmountPerShare: number
  amount: number
  currency: string
  status: 'Scheduled' | 'Posted' | 'Skipped'
  isUserAdjusted: boolean
  note: string | null
}

export async function getLatestDividends(portfolioId: string): Promise<LatestDividendResponse[]> {
  const response = await http.get<LatestDividendResponse[]>(
    `/portfolios/${portfolioId}/dividends/latest`,
  )
  return response.data
}

export async function getDividendCashFlows(portfolioId: string): Promise<DividendCashFlow[]> {
  const response = await http.get<DividendCashFlow[]>(`/portfolios/${portfolioId}/dividends/cash-flows`)
  return response.data
}

export async function updateDividendCashFlow(
  portfolioId: string,
  cashFlowId: string,
  request: { amount?: number; skip?: boolean; note?: string | null },
): Promise<DividendCashFlow> {
  const response = await http.patch<DividendCashFlow>(
    `/portfolios/${portfolioId}/dividends/cash-flows/${cashFlowId}`,
    request,
  )
  return response.data
}
