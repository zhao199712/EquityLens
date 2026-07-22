import { http } from './http'

export interface AgentWorkflowQueryCreated {
  agentRunId: string
  researchRunId: string | null
  workflowType: 'PortfolioDiagnosis' | 'ResearchInvestigation'
  status: string
  routingReason: string
  routingModel: string
}

export async function createAgentWorkflowQuery(question: string): Promise<AgentWorkflowQueryCreated> {
  const response = await http.post<AgentWorkflowQueryCreated>('/agent-queries', { question })
  return response.data
}
