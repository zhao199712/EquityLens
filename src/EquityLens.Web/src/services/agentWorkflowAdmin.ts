import { http } from './http'

export interface WorkflowAdmin { workflowType:string; displayName:string; description:string; agentType:string; isEnabled:boolean; nodeTypes:string[]; edges:{from:string;to:string}[] }
export interface NodeAdmin { nodeType:string; displayName:string; description:string; stage:string; sideEffectLevel:string; isEnabled:boolean; timeoutSeconds:number; maxRetryCount:number; metadata:Record<string, unknown>|null; requiredBlackboardKeys:string[]; producedBlackboardKeys:string[]; allowedNextNodeTypes:string[] }
export const listWorkflows = async () => (await http.get<WorkflowAdmin[]>('/admin/agent-workflows')).data
export const saveWorkflow = async (x:WorkflowAdmin) => (await http.put<WorkflowAdmin>(`/admin/agent-workflows/${x.workflowType}`, x)).data
export const listNodes = async () => (await http.get<NodeAdmin[]>('/admin/agent-nodes')).data
export const saveNode = async (x:NodeAdmin) => (await http.put<NodeAdmin>(`/admin/agent-nodes/${x.nodeType}`, x)).data
