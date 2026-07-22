import { http } from './http'

export interface WorkflowSkill {
  id: string
  description: string
  capabilities: string[]
}

export interface NodeCapability {
  id: string
  nodeType: string
  description: string
  argumentSchema: string
  requiredKeys: string[]
  producedKeys: string[]
  sideEffectLevel: string
  idempotent: boolean
  supportsLoop: boolean
  maxOccurrences: number
  parametersSchema?: Record<string, unknown> | null
  inputMode?: string
}

export interface AgentRegistry {
  skills: WorkflowSkill[]
  capabilities: NodeCapability[]
}

export async function getAgentRegistry(): Promise<AgentRegistry> {
  return (await http.get<AgentRegistry>('/admin/agent-registry')).data
}
