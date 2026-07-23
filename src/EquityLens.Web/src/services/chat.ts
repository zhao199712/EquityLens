import { http } from './http'

export interface ChatSession {
  id: string
  title: string | null
  createdAtUtc: string
  updatedAtUtc: string
  messageCount: number
}

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant' | 'tool'
  content: string | null
  toolName: string | null
  sequenceNumber: number | null
  createdAtUtc: string
  messageType: 'Text' | 'AgentRun'
  agentRunId: string | null
  runCard: ConversationRunCard | null
}

export interface ConversationRunCard {
  agentRunId: string
  researchRunId: string | null
  workflowType: string
  status: string
  currentStage: string | null
  currentStageDisplayName: string | null
  completedNodes: number
  totalNodes: number
  finalAnswer: string | null
  errorMessage: string | null
}

export async function createSession(title?: string): Promise<ChatSession> {
  const { data } = await http.post('/chat/sessions', { title })
  return data
}

export async function listSessions(): Promise<ChatSession[]> {
  const { data } = await http.get('/chat/sessions')
  return data
}

export async function getMessages(sessionId: string): Promise<ChatMessage[]> {
  const { data } = await http.get(`/chat/sessions/${sessionId}/messages`)
  return data
}

export async function getRunCard(sessionId: string, agentRunId: string): Promise<ConversationRunCard> {
  const { data } = await http.get(`/chat/sessions/${sessionId}/run-cards/${agentRunId}`)
  return data
}

export async function deleteSession(sessionId: string): Promise<void> {
  await http.delete(`/chat/sessions/${sessionId}`)
}

export function sendMessageStream(
  sessionId: string,
  content: string,
  token: string,
  onDelta: (text: string) => void,
  onToolStart: (tool: string, args: string) => void,
  onToolEnd: (tool: string, preview: string) => void,
  onAction: (action: string) => void,
  onRunCreated: (messageId: string, runCard: ConversationRunCard) => void,
  onDone: (model: string, promptTokens: number, completionTokens: number) => void,
  onError: (message: string) => void,
) {
  const baseURL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5034/api'

  fetch(`${baseURL}/chat/sessions/${sessionId}/messages`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ content, requestId: crypto.randomUUID() }),
  }).then(async (response) => {
    if (!response.ok) {
      onError(`HTTP ${response.status}`)
      return
    }

    const reader = response.body?.getReader()
    if (!reader) {
      onError('No response body')
      return
    }

    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() ?? ''

      for (const line of lines) {
        if (!line.startsWith('data: ')) continue
        const jsonStr = line.slice(6).trim()
        if (!jsonStr) continue

        try {
          const evt = JSON.parse(jsonStr)
          switch (evt.type) {
            case 'delta':
              onDelta(evt.content)
              break
            case 'tool_start':
              onToolStart(evt.tool, evt.args)
              break
            case 'tool_end':
              onToolEnd(evt.tool, evt.preview)
              break
            case 'conversation_action':
              onAction(evt.action)
              break
            case 'run_created':
              onRunCreated(evt.messageId, evt.runCard)
              break
            case 'clarification':
              onDelta(evt.content)
              break
            case 'done':
              onDone(evt.model, evt.promptTokens, evt.completionTokens)
              break
            case 'error':
              onError(evt.message)
              break
          }
        } catch {
          // skip malformed JSON
        }
      }
    }
  }).catch((err) => {
    onError(err instanceof Error ? err.message : 'Network error')
  })
}
