import { http } from './http'

export interface Security {
  id: string
  ticker: string
  exchange: string
  name: string
  assetType: string | null
  currency: string
  isin: string | null
  sector: string | null
  industry: string | null
}

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

export async function getSecurities(query?: string): Promise<Security[]> {
  const response = await http.get<Security[]>('/securities', {
    params: query ? { query } : {},
  })
  return response.data
}

export async function getSecurityPrices(
  securityId: string,
  from?: string,
  to?: string,
): Promise<MarketPrice[]> {
  const response = await http.get<MarketPrice[]>(`/securities/${securityId}/prices`, {
    params: { from, to },
  })
  return response.data
}

export async function syncSecurityPrices(
  securityId: string,
  days = 365,
  force = false,
): Promise<{
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
}> {
  const response = await http.post(`/securities/${securityId}/prices/sync`, null, {
    params: { days, force },
  })
  return response.data
}
