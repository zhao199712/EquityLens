import { http } from './http'

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
