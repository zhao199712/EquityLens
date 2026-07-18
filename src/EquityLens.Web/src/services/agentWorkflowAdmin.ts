import { http } from './http'

export interface WorkflowAdmin { workflowType:string; displayName:string; description:string; agentType:string; isEnabled:boolean; nodeTypes:string[]; edges:{from:string;to:string}[] }
export interface UpdateWorkflowSetting { isEnabled:boolean; displayName:string; description:string }
export interface NodeContract { nodeType:string; version:number; displayName:string; description:string; stage:string; sideEffectLevel:string; inputSchema:string; outputSchema:string; requiredBlackboardKeys:string[]; optionalBlackboardKeys:string[]; producedBlackboardKeys:string[]; allowedPreviousNodeTypes:string[]; allowedNextNodeTypes:string[]; defaultPolicy:{timeoutSeconds:number;maxRetryCount:number}; isIdempotent:boolean; supportsLoop:boolean; requiresHumanInput:boolean }
export interface NodeAdmin { nodeType:string; displayName:string; description:string; stage:string; sideEffectLevel:string; isEnabled:boolean; timeoutSeconds:number; maxRetryCount:number; metadata:Record<string, unknown>|null; contract:NodeContract; requiredBlackboardKeys:string[]; producedBlackboardKeys:string[]; allowedNextNodeTypes:string[] }
export interface UpdateNodeSetting { isEnabled:boolean; displayName:string; description:string; timeoutSeconds:number; maxRetryCount:number; metadata:Record<string, unknown>|null }
export const listWorkflows = async () => (await http.get<WorkflowAdmin[]>('/admin/agent-workflows')).data
export const saveWorkflow = async (workflowType:string, setting:UpdateWorkflowSetting) => (await http.put<WorkflowAdmin>(`/admin/agent-workflows/${workflowType}`, setting)).data
export const listNodes = async () => (await http.get<NodeAdmin[]>('/admin/agent-nodes')).data
export const saveNode = async (nodeType:string, setting:UpdateNodeSetting) => (await http.put<NodeAdmin>(`/admin/agent-nodes/${nodeType}`, setting)).data
