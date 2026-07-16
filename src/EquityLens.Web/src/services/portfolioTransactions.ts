import { http } from './http'

export interface CreateTransactionRequest {
  securityId: string
  transactionType: 'Buy' | 'Sell'
  quantity: number
  price: number
  fee?: number | null
  transactionDate: string
  note?: string | null
}

export interface TransactionResponse {
  id: string
  securityId: string
  ticker: string
  exchange: string
  securityName: string
  transactionType: string
  quantity: number
  price: number
  fee: number
  transactionDate: string
  note: string | null
  createdAtUtc: string
  netProceeds: number | null
  fifoCost: number | null
  realizedPnl: number | null
}

export async function createTransaction(
  portfolioId: string,
  request: CreateTransactionRequest,
): Promise<TransactionResponse> {
  const response = await http.post<TransactionResponse>(
    `/portfolios/${portfolioId}/transactions`,
    request,
  )
  return response.data
}

export async function getTransactions(
  portfolioId: string,
  securityId?: string,
): Promise<TransactionResponse[]> {
  const response = await http.get<TransactionResponse[]>(
    `/portfolios/${portfolioId}/transactions`,
    { params: securityId ? { securityId } : undefined },
  )
  return response.data
}

export async function deleteTransaction(
  portfolioId: string,
  transactionId: string,
): Promise<void> {
  await http.delete(`/portfolios/${portfolioId}/transactions/${transactionId}`)
}
