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
}

export interface AgentRunNodeDto {
  id: string
  nodeKey: string
  templateNodeKey: string
  iteration: number
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

export interface AgentFeedbackDto {
  id: string
  agentRunNodeId: string | null
  feedbackType: string
  status: string
  prompt: string
  responseJson: Record<string, unknown> | null
  createdAtUtc: string
  respondedAtUtc: string | null
}

export interface AgentRunDetail {
  run: AgentRunListItem
  nodes: AgentRunNodeDto[]
  events: AgentRunEventDto[]
  toolCalls: AgentToolCallDto[]
  feedback: AgentFeedbackDto[]
  blackboardJson: Record<string, unknown>
  outputJson: Record<string, unknown> | null
  workflowDefinitionJson: Record<string, unknown>
}

export interface AgentRunCreatedResponse extends AgentRunListItem {}

interface RawAgentRunDetail {
  run: AgentRunListItem
  nodes: Array<Omit<AgentRunNodeDto, 'inputJson' | 'outputJson'> & { inputJson: string | null; outputJson: string | null }>
  events: Array<Omit<AgentRunEventDto, 'payloadJson'> & { payloadJson: string | null }>
  toolCalls: Array<Omit<AgentToolCallDto, 'argumentsJson' | 'resultJson'> & { argumentsJson: string; resultJson: string | null }>
  feedback: Array<Omit<AgentFeedbackDto, 'responseJson'> & { responseJson: string | null }>
  blackboardJson: string
  outputJson: string | null
  workflowDefinitionJson: string
}

export async function listAgentRuns(params?: {
  limit?: number
  workflowType?: string
  status?: string
  researchRunId?: string
}): Promise<AgentRunListItem[]> {
  const response = await http.get<AgentRunListItem[]>('/agent-runs', { params })
  return response.data
}

export async function getAgentRun(id: string): Promise<AgentRunDetail> {
  const response = await http.get<RawAgentRunDetail>(`/agent-runs/${id}`)
  return normalizeDetail(response.data)
}

export async function createCriticReview(researchRunId: string): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>('/agent-runs/critic-review', { researchRunId })
  return response.data
}

export async function createDraftRevision(criticReviewRunId: string): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>('/agent-runs/draft-revision', { criticReviewRunId })
  return response.data
}

export async function createEvidenceRemediation(criticReviewRunId: string): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>('/agent-runs/evidence-remediation', { criticReviewRunId })
  return response.data
}

export async function retryAgentRun(id: string): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>(`/agent-runs/${id}/retry`)
  return response.data
}

export async function cancelAgentRun(id: string): Promise<AgentRunCreatedResponse> {
  const response = await http.post<AgentRunCreatedResponse>(`/agent-runs/${id}/cancel`)
  return response.data
}

function normalizeDetail(raw: RawAgentRunDetail): AgentRunDetail {
  return {
    run: raw.run,
    nodes: raw.nodes.map((node) => ({
      ...node,
      inputJson: parseJsonObject(node.inputJson),
      outputJson: parseJsonObject(node.outputJson),
    })),
    events: raw.events.map((event) => ({ ...event, payloadJson: parseJsonObject(event.payloadJson) })),
    toolCalls: raw.toolCalls.map((toolCall) => ({
      ...toolCall,
      argumentsJson: parseJsonObject(toolCall.argumentsJson) ?? {},
      resultJson: parseJsonObject(toolCall.resultJson),
    })),
    feedback: raw.feedback.map((item) => ({ ...item, responseJson: parseJsonObject(item.responseJson) })),
    blackboardJson: parseJsonObject(raw.blackboardJson) ?? {},
    outputJson: parseJsonObject(raw.outputJson),
    workflowDefinitionJson: parseJsonObject(raw.workflowDefinitionJson) ?? {},
  }
}

function parseJsonObject(value: string | null): Record<string, unknown> | null {
  if (!value) return null
  try {
    const parsed = JSON.parse(value)
    return typeof parsed === 'object' && parsed !== null ? parsed as Record<string, unknown> : { value: parsed }
  } catch {
    return { raw: value }
  }
}
