import { http } from './http'

export interface ResearchAskRequest {
  ticker: string
  question: string
  retrievalMode?: string
  documentType?: string
  sourcePolicy?: string
  topK?: number
  temperature?: number
  debug?: boolean
}

export interface ResearchCitation {
  index: number
  sourceType: string
  documentChunkId: string | null
  documentId: string | null
  title: string
  documentType: string | null
  sourceRole: string
  pageNumber: number | null
  url: string | null
  publishedAt: string | null
  retrievedAt: string | null
  quoteText: string
  relevanceScore: number
}

export interface ResearchRetrievalSearch {
  documentType: string | null
  sourceRole: string
  query: string
  topK: number
  reason: string
}

export interface ResearchRetrievalStrategy {
  mode: string
  searches: ResearchRetrievalSearch[]
}

export interface ResearchAskResponse {
  question: string
  answer: string
  model: string
  retrievalStrategy: ResearchRetrievalStrategy
  citations: ResearchCitation[]
  trace: Record<string, unknown> | null
  status: string
  researchRunId: string | null
}
export interface ResearchInvestigationCreated {
  agentRunId: string
  researchRunId: string
  workflowType: string
  status: string
}

export interface ResearchRunSummary {
  id: string
  ticker: string
  question: string
  status: string
  citationCount: number
  retrievalMode: string
  latencyMs: number
  createdAtUtc: string
}

export interface ResearchRunStep {
  id: string
  stepType: string
  inputJson: string | null
  outputJson: string | null
  durationMs: number | null
  startedAtUtc: string
  completedAtUtc: string | null
  errorMessage: string | null
}

export interface ResearchRunCandidate {
  id: string
  documentChunkId: string
  documentId: string | null
  title: string | null
  documentType: string | null
  sourceRole: string | null
  pageNumber: number | null
  relevanceScore: number
  adjustedScore: number | null
  rankBeforeRerank: number | null
  rankAfterRerank: number | null
  decision: string
  discardReason: string | null
  contentPreview: string | null
}

export interface ResearchRunCitation {
  id: string
  citationIndex: number
  sourceType: string
  documentChunkId: string | null
  documentId: string | null
  title: string | null
  documentType: string | null
  sourceRole: string | null
  pageNumber: number | null
  quoteText: string
  relevanceScore: number
}

export interface ResearchRunDetail {
  run: ResearchRunSummary
  answer: string
  steps: ResearchRunStep[]
  candidates: ResearchRunCandidate[]
  citations: ResearchRunCitation[]
}

export async function askResearch(request: ResearchAskRequest): Promise<ResearchAskResponse> {
  const response = await http.post<ResearchAskResponse>('/research/ask', request)
  return response.data
}
export async function createResearchInvestigation(request: ResearchAskRequest): Promise<ResearchInvestigationCreated> {
  const response = await http.post<ResearchInvestigationCreated>('/research/investigations', request)
  return response.data
}

export async function listResearchRuns(params?: {
  limit?: number
  ticker?: string
  status?: string
}): Promise<ResearchRunSummary[]> {
  const response = await http.get<ResearchRunSummary[]>('/research/runs', { params })
  return response.data
}

export async function getResearchRun(id: string): Promise<ResearchRunDetail> {
  const response = await http.get<ResearchRunDetail>(`/research/runs/${id}`)
  return response.data
}
