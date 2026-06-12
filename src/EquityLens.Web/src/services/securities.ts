import { http } from './http'

export interface SecuritySearchResult {
  securityId: string | null
  ticker: string
  exchange: string
  name: string
  assetType: string | null
  currency: string
  isin: string | null
  sector: string | null
  industry: string | null
  source: string
}

export interface ResolveSecurityRequest {
  securityId?: string | null
  ticker?: string | null
  exchange?: string | null
}

export interface ResolveSecurityResponse {
  securityId: string
  created: boolean
  ticker: string
  exchange: string
  name: string
  assetType: string | null
  currency: string
  isin: string | null
  sector: string | null
  industry: string | null
}

export async function searchSecurities(query: string): Promise<SecuritySearchResult[]> {
  const response = await http.get<SecuritySearchResult[]>('/securities/search', {
    params: { query },
  })
  return response.data
}

export async function resolveSecurity(
  request: ResolveSecurityRequest,
): Promise<ResolveSecurityResponse> {
  const response = await http.post<ResolveSecurityResponse>('/securities/resolve', request)
  return response.data
}
