import { http } from './http'

export interface WorkflowSkill {
  id: string
  displayName: string
  description: string
  capabilities: string[]
  kind: 'Lead' | 'Supporting'
  supportedWorkflowTypes: string[]
  routable: boolean
  promptTemplateId?: string | null
  promptVersion?: number | null
  requiredInputs: string[]
  systemPrompt?: string | null
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
