import { http } from './http'

export interface InvestmentResearchContextEnvelope {
  market: string
  asset: string
  depth: string
  horizon: string | null
  currency: string | null
  language: string
}

export interface AgentWorkflowQueryCreated {
  agentRunId: string
  researchRunId: string | null
  workflowType: 'PortfolioDiagnosis' | 'ResearchInvestigation'
  status: string
  routingReason: string
  routingModel: string
  leadSkill: string
  leadSkillDisplayName: string
  routingConfidence: string
  objective: string
  contextEnvelope: InvestmentResearchContextEnvelope
  inferredFields: string[]
  clarifyingQuestions: string[]
}

export async function createAgentWorkflowQuery(question: string): Promise<AgentWorkflowQueryCreated> {
  const response = await http.post<AgentWorkflowQueryCreated>('/agent-queries', { question })
  return response.data
}
