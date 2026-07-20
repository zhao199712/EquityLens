import { http } from './http'

export interface MarketPrice {
  id: string
  securityId: string
  priceTime: string
  interval: string
  open: number
  high: number
  low: number
  close: number
  adjustedClose: number | null
  volume: number | null
  dataSource: string | null
}

export interface SyncMarketPricesResponse {
  securityId: string
  source: string | null
  synced: boolean
  skipped: boolean
  message: string | null
  from: string | null
  to: string | null
  importedCount: number
  insertedCount: number
  updatedCount: number
}

export async function syncSecurityPrices(
  securityId: string,
  days = 365,
  force = false,
): Promise<SyncMarketPricesResponse> {
  const response = await http.post<SyncMarketPricesResponse>(
    `/securities/${securityId}/prices/sync`,
    null,
    { params: { days, force } },
  )
  return response.data
}

export async function getSecurityPrices(
  securityId: string,
  params: { from?: string; to?: string } = {},
): Promise<MarketPrice[]> {
  const response = await http.get<MarketPrice[]>(`/securities/${securityId}/prices`, { params })
  return response.data
}

export interface MarketTickerEntry {
  code: string
  name: string
  close: number
  changePct: number | null
}

export async function getMarketTicker(): Promise<MarketTickerEntry[]> {
  const response = await http.get<MarketTickerEntry[]>('/market/ticker')
  return response.data
}
