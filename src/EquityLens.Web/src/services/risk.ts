import { http } from './http'

export interface PortfolioListItem {
  id: string
  name: string
  description: string | null
  baseCurrency: string
  holdingCount: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface PortfolioHolding {
  id: string
  securityId: string
  ticker: string
  exchange: string
  securityName: string
  quantity: number
  averageCost: number
  costCurrency: string
  note: string | null
  updatedAtUtc: string
}

export interface PortfolioDetail {
  id: string
  name: string
  description: string | null
  baseCurrency: string
  createdAtUtc: string
  updatedAtUtc: string
  holdings: PortfolioHolding[]
}

export interface RiskHorizonResult {
  horizonDays: number
  historicalVaR: number
  historicalES: number
  monteCarloVaR: number
  monteCarloES: number
  monteCarloMeanFinalValue: number
  monteCarloMedianFinalValue: number
  monteCarloWorstCaseFinalValue: number
  monteCarloBestCaseFinalValue: number
}

export interface PortfolioHoldingRisk {
  securityId: string
  ticker: string
  exchange: string
  securityName: string
  weight: number
  annualizedVolatility: number
  dataPointCount: number
}

export interface PortfolioRiskResponse {
  portfolioId: string
  from: string
  to: string
  baseCurrency: string
  holdingCount: number
  pricedHoldingCount: number
  alignedReturnCount: number
  totalMarketValue: number
  historicalAnnualizedVolatility: number
  maxDrawdown: number
  sharpeRatio: number
  confidenceLevel: number
  simulations: number
  volatilityMethod: string
  ewmaLambda: number
  driftAssumption: string
  covarianceMethod: string | null
  residualSampling: string | null
  commonTradingDays: number | null
  shrinkageAlpha: number | null
  supportedHorizons: number[]
  horizons: RiskHorizonResult[]
  holdings: PortfolioHoldingRisk[]
}

export async function getPortfolios(): Promise<PortfolioListItem[]> {
  const response = await http.get<PortfolioListItem[]>('/portfolios')
  return response.data
}

export async function getPortfolio(id: string): Promise<PortfolioDetail> {
  const response = await http.get<PortfolioDetail>(`/portfolios/${id}`)
  return response.data
}

export interface PortfolioRiskParams {
  from: string
  to: string
  horizonDays?: number
  confidenceLevel?: number
  simulations?: number
  model?: string
}

export async function getPortfolioRisk(
  portfolioId: string,
  params: PortfolioRiskParams,
): Promise<PortfolioRiskResponse> {
  const response = await http.get<PortfolioRiskResponse>(
    `/portfolios/${portfolioId}/risk`,
    { params },
  )
  return response.data
}

export interface CreatePortfolioRequest {
  name: string
  description?: string
  baseCurrency?: string
}

export async function createPortfolio(request: CreatePortfolioRequest): Promise<PortfolioDetail> {
  const response = await http.post<PortfolioDetail>('/portfolios', request)
  return response.data
}

export async function deletePortfolio(id: string): Promise<void> {
  await http.delete(`/portfolios/${id}`)
}

export interface HoldingValuation {
  holdingId: string
  securityId: string
  ticker: string
  exchange: string
  securityName: string
  quantity: number
  averageCost: number
  costCurrency: string
  latestPrice: number | null
  priceTime: string | null
  costValue: number
  marketValue: number | null
  unrealizedPnl: number | null
  unrealizedPnlPercent: number | null
  weight: number | null
  valuationStatus: string
}

export interface PortfolioValuationResponse {
  portfolioId: string
  asOfDate: string
  currency: string
  totalCostValue: number
  totalMarketValue: number
  totalUnrealizedPnl: number
  totalUnrealizedPnlPercent: number | null
  holdings: HoldingValuation[]
}

export async function getPortfolioValuation(portfolioId: string): Promise<PortfolioValuationResponse> {
  const response = await http.get<PortfolioValuationResponse>(`/portfolios/${portfolioId}/valuation`)
  return response.data
}

export interface PortfolioValuationHistoryPoint {
  date: string
  totalCostValue: number
  totalMarketValue: number
  totalUnrealizedPnl: number
  totalUnrealizedPnlPercent: number | null
  holdingCount: number
  pricedHoldingCount: number
}

export interface PortfolioValuationHistoryResponse {
  portfolioId: string
  from: string
  to: string
  currency: string
  points: PortfolioValuationHistoryPoint[]
}

export async function getPortfolioValuationHistory(
  portfolioId: string,
  params: { from: string; to: string },
): Promise<PortfolioValuationHistoryResponse> {
  const response = await http.get<PortfolioValuationHistoryResponse>(
    `/portfolios/${portfolioId}/valuation/history`,
    { params },
  )
  return response.data
}
