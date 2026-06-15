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
