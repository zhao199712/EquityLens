import { http } from './http'

export interface AgentRunListItem {
  id: string
  workflowType: string
  agentType: string
  status: string
  errorMessage: string | null
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  nodeCount: number
  eventCount: number
  toolCallCount: number
}

export interface AgentRunNodeDto {
  id: string
  nodeKey: string
  nodeType: string
  status: string
  inputJson: Record<string, unknown> | null
  outputJson: Record<string, unknown> | null
  errorMessage: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
  durationMs: number | null
}

export interface AgentRunEventDto {
  id: string
  agentRunNodeId: string | null
  eventType: string
  message: string | null
  payloadJson: Record<string, unknown> | null
  createdAtUtc: string
}

export interface AgentToolCallDto {
  id: string
  agentRunNodeId: string | null
  toolName: string
  status: string
  argumentsJson: Record<string, unknown>
  resultPreview: string | null
  resultJson: Record<string, unknown> | null
  errorMessage: string | null
  startedAtUtc: string
  completedAtUtc: string | null
  durationMs: number | null
}

export interface AgentRunDetail {
  id: string
  workflowType: string
  agentType: string
  status: string
  inputJson: Record<string, unknown>
  outputJson: Record<string, unknown> | null
  blackboardJson: Record<string, unknown>
  workflowDefinitionJson: Record<string, unknown>
  errorMessage: string | null
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  nodes: AgentRunNodeDto[]
  events: AgentRunEventDto[]
  toolCalls: AgentToolCallDto[]
}

export interface AgentRunCreatedResponse {
  id: string
  status: string
}

export async function listAgentRuns(params?: {
  limit?: number
  workflowType?: string
  status?: string
}): Promise<AgentRunListItem[]> {
  const response = await http.get<AgentRunListItem[]>('/agent-runs', { params })
  return response.data
}

export async function getAgentRun(id: string): Promise<AgentRunDetail> {
  const response = await http.get<AgentRunDetail>(`/agent-runs/${id}`)
  return response.data
}

export async function createCriticReview(researchRunId: string): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>('/agent-runs/critic-review', {
    researchRunId,
  })
  return response.data
}

export async function retryAgentRun(id: string): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>(`/agent-runs/${id}/retry`)
  return response.data
}

export async function cancelAgentRun(id: string): Promise<void> {
  await http.post(`/agent-runs/${id}/cancel`)
}
