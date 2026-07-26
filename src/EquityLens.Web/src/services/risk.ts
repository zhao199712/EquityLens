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
  industry: string
  componentVolatility: number
  componentRiskShare: number
  marginalVolatility: number
  incrementalVolatility: number
}

export interface PortfolioIndustryRisk {
  industry: string
  weight: number
  componentVolatility: number
  componentRiskShare: number
  marginalVolatility: number
  incrementalVolatility: number
  holdingCount: number
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
  dataAsOfDate: string | null
  concentrationHhi: number
  largestHoldingWeight: number
  horizons: RiskHorizonResult[]
  holdings: PortfolioHoldingRisk[]
  industries: PortfolioIndustryRisk[]
  riskSourceAnnualizedVolatility: number
  dailyLogReturns: number[]
}

export async function getPortfolios(): Promise<PortfolioListItem[]> {
  const response = await http.get<PortfolioListItem[]>('/portfolios')
  return response.data
}

export async function getPortfolio(id: string): Promise<PortfolioDetail> {
  const response = await http.get<PortfolioDetail>(`/portfolios/${id}`)
  return response.data
}

export interface AgentRunCreatedResponse {
  id: string
  workflowType: string
  agentType: string
  status: string
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  errorMessage: string | null
}

export async function createPortfolioDiagnosis(
  portfolioId: string,
  input: { from?: string; to?: string } = {},
): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>(
    `/portfolios/${portfolioId}/agent-diagnoses`,
    input,
  )
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

export interface PortfolioRiskBacktestPoint {
  date: string
  actualReturn: number
  predictedVaR: number
  predictedES: number
  breached: boolean
}
export interface PortfolioRiskBacktestModel {
  model: string
  confidenceLevel: number
  observationCount: number
  breachCount: number
  breachRate: number
  expectedBreachRate: number
  kupiecPValue: number | null
  christoffersenPValue: number | null
  tailObservationCount: number
  actualTailLossAverage: number | null
  predictedEsAverage: number | null
  esTailLossRatio: number | null
  esStatus: string
  status: string
  points: PortfolioRiskBacktestPoint[]
}
export interface PortfolioRiskBacktestResponse {
  portfolioId: string
  from: string
  to: string
  lookbackDays: number
  observationCount: number
  models: PortfolioRiskBacktestModel[]
}
export async function getPortfolioRiskBacktest(portfolioId: string, from: string, to: string): Promise<PortfolioRiskBacktestResponse> {
  const response = await http.get<PortfolioRiskBacktestResponse>(`/portfolios/${portfolioId}/risk/backtest`, { params: { from, to } })
  return response.data
}
export interface PortfolioRiskBacktestRun {
  id: string; portfolioId: string; jobId: string
  status: 'Queued' | 'Running' | 'FallbackRunning' | 'Completed' | 'Failed'; progressPercent: number
  from: string; to: string; lookbackDays: number; simulations: number; algorithmVersion: string
  requestedModel: string; selectedModel: string | null; inputHash: string | null
  fallbackReason: string | null; fallbackDepth: number
  createdAtUtc: string; startedAtUtc: string | null; completedAtUtc: string | null
  errorCode: string | null; errorMessage: string | null; result: PortfolioRiskBacktestResponse | null
}

export type RiskCalculationOperation = 'risk' | 'monte-carlo' | 'backtest' | 'scenario' | 'governance' | 'report'
export interface RiskCalculationRun {
  id:string; portfolioId:string; operation:RiskCalculationOperation
  status:'Queued'|'Running'|'FallbackRunning'|'Completed'|'Failed'; progressPercent:number
  requestedModel:string; selectedModel:string|null; algorithmVersion:string; inputHash:string|null
  dataFactorVersion:string|null; fallbackReason:string|null; fallbackDepth:number
  createdAtUtc:string; startedAtUtc:string|null; completedAtUtc:string|null
  errorCode:string|null; errorMessage:string|null; result:unknown|null
}
export async function createRiskCalculation(
  portfolioId:string,
  operation:RiskCalculationOperation,
  options:Record<string, unknown> = {},
): Promise<RiskCalculationRun> {
  const response = await http.post<RiskCalculationRun>(
    `/portfolios/${portfolioId}/risk/calculations`,
    { operation, ...options },
  )
  return response.data
}
export async function getRiskCalculation(portfolioId:string, runId:string): Promise<RiskCalculationRun> {
  const response = await http.get<RiskCalculationRun>(`/portfolios/${portfolioId}/risk/calculations/${runId}`)
  return response.data
}
export async function listRiskCalculations(portfolioId:string): Promise<RiskCalculationRun[]> {
  const response = await http.get<RiskCalculationRun[]>(`/portfolios/${portfolioId}/risk/calculations`)
  return response.data
}
export async function createPortfolioRiskBacktestRun(portfolioId: string, from: string, to: string): Promise<PortfolioRiskBacktestRun> {
  const response = await http.post<PortfolioRiskBacktestRun>(`/portfolios/${portfolioId}/risk/backtests`, undefined, { params: { from, to } })
  return response.data
}
export async function getPortfolioRiskBacktestRun(portfolioId: string, runId: string): Promise<PortfolioRiskBacktestRun> {
  const response = await http.get<PortfolioRiskBacktestRun>(`/portfolios/${portfolioId}/risk/backtests/${runId}`)
  return response.data
}
export async function getPortfolioRiskBacktestRuns(portfolioId: string): Promise<PortfolioRiskBacktestRun[]> {
  const response = await http.get<PortfolioRiskBacktestRun[]>(`/portfolios/${portfolioId}/risk/backtests`)
  return response.data
}
export interface PortfolioMonteCarloBandPoint { day:number; p1:number; p5:number; p50:number; p95:number; p99:number }
export interface PortfolioMonteCarloPath { pathIndex:number; cumulativeReturns:number[] }
export interface PortfolioMonteCarloDiagnostics {
  annualizedPortfolioVolatility:number; residualNormP99:number; maxResidualNorm:number
  p50FinalReturn:number; p95FinalReturn:number; p99FinalReturn:number; expectedMedianGap:number
  residualCapQuantile:number; cappedDrawRate:number
  rightSkewWarning:boolean; rightSkewMessage:string|null
}
export interface PortfolioMonteCarloResponse {
  portfolioId:string; status:string; message:string|null; dataAsOfDate:string|null; commonTradingDays:number
  horizonDays:number; simulations:number; model:string; ewmaLambda:number; shrinkageAlpha:number; residualCapQuantile:number; cappedDrawRate:number
  bands:PortfolioMonteCarloBandPoint[]; samplePaths:PortfolioMonteCarloPath[]
  positiveReturnProbability:number; expectedReturn:number; p5FinalReturn:number; p1FinalReturn:number
  diagnostics:PortfolioMonteCarloDiagnostics
}
export async function getPortfolioMonteCarlo(portfolioId:string, model = 'mvewma_fhs'): Promise<PortfolioMonteCarloResponse> {
  const response = await http.get<PortfolioMonteCarloResponse>(`/portfolios/${portfolioId}/risk/monte-carlo`, { params: { model } })
  return response.data
}
export interface PortfolioRiskAlert { code:string; status:'normal'|'warning'|'critical'; currentValue:number; warningThreshold:number; criticalThreshold:number; message:string }
export interface PortfolioRiskGovernanceResponse { portfolioId:string; dataStatus:string; dataAsOfDate:string|null; commonTradingDays:number; alerts:PortfolioRiskAlert[] }
export async function getPortfolioRiskGovernance(portfolioId:string): Promise<PortfolioRiskGovernanceResponse> {
  const response = await http.get<PortfolioRiskGovernanceResponse>(`/portfolios/${portfolioId}/risk/governance`)
  return response.data
}
export interface PortfolioRiskScenarioWeight { securityId:string; targetWeight:number }
export interface PortfolioRiskScenarioResponse { portfolioId:string; cashWeight:number; current:PortfolioRiskResponse; scenario:PortfolioRiskResponse; currentStress:PortfolioStressScenario[]; scenarioStress:PortfolioStressScenario[]; currentGovernance:PortfolioRiskGovernanceResponse; scenarioGovernance:PortfolioRiskGovernanceResponse }
export async function calculatePortfolioRiskScenario(portfolioId:string, targetWeights:PortfolioRiskScenarioWeight[]): Promise<PortfolioRiskScenarioResponse> {
  const response = await http.post<PortfolioRiskScenarioResponse>(`/portfolios/${portfolioId}/risk/scenario`, { targetWeights })
  return response.data
}
export interface PortfolioRiskReportSnapshotListItem {
  id:string; createdAtUtc:string; dataAsOfDate:string|null; model:string; thresholdVersion:string; overallStatus:string
}
export interface PortfolioRiskReportSnapshotDetail extends PortfolioRiskReportSnapshotListItem {
  portfolioId:string; snapshot:Record<string, unknown>
}
export async function createPortfolioRiskReportSnapshot(portfolioId:string): Promise<PortfolioRiskReportSnapshotDetail> {
  const response = await http.post<PortfolioRiskReportSnapshotDetail>(`/portfolios/${portfolioId}/risk/reports`)
  return response.data
}
export async function getPortfolioRiskReportSnapshots(portfolioId:string): Promise<PortfolioRiskReportSnapshotListItem[]> {
  const response = await http.get<PortfolioRiskReportSnapshotListItem[]>(`/portfolios/${portfolioId}/risk/reports`)
  return response.data
}
export async function getPortfolioRiskReportSnapshot(portfolioId:string, reportId:string): Promise<PortfolioRiskReportSnapshotDetail> {
  const response = await http.get<PortfolioRiskReportSnapshotDetail>(`/portfolios/${portfolioId}/risk/reports/${reportId}`)
  return response.data
}
export interface PortfolioStressHolding { ticker:string; securityName:string; industry:string; weight:number; basePrice:number; stressedPrice:number; shock:number; contribution:number }
export interface PortfolioStressIndustry { industry:string; weight:number; impact:number; contribution:number }
export interface PortfolioStressScenario { id:string; name:string; type:string; status:string; methodology:string; from:string|null; to:string|null; totalImpact:number; holdings:PortfolioStressHolding[]; industries:PortfolioStressIndustry[] }
export interface PortfolioStressTestResponse { portfolioId:string; dataAsOfDate:string|null; scenarios:PortfolioStressScenario[] }
export async function getPortfolioStressTest(portfolioId:string): Promise<PortfolioStressTestResponse> { const response=await http.get<PortfolioStressTestResponse>(`/portfolios/${portfolioId}/risk/stress`); return response.data }

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
  sector: string | null
  industry: string | null
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
  cashBalance: number
  totalAssetValue: number
  totalRealizedPnl: number
  todayPnl: number | null
  twr: number | null
  xirr: number | null
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
  cashBalance: number
  totalAssetValue: number
  totalRealizedPnl: number
  externalCashFlow: number
  dailyPnl: number | null
}

export interface PortfolioValuationHistoryResponse {
  portfolioId: string
  from: string
  to: string
  currency: string
  points: PortfolioValuationHistoryPoint[]
  twr: number | null
  xirr: number | null
  benchmark: { date: string; indexValue: number | null; normalizedValue: number | null }[] | null
  benchmarkReturn: number | null
  excessReturn: number | null
  beta: number | null
  jensenAlpha: number | null
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
