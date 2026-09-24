import { http } from './http'

export interface PromptVersion { id: string; versionNumber: number; status: string; requiredVariablesJson: string; responseFormat?: string | null; changeSummary?: string | null; contentHash: string; createdAtUtc: string; publishedAtUtc?: string | null; concurrencyToken: string }
export interface PromptTemplate { id: string; key: string; name: string; description?: string | null; status: string; updatedAtUtc: string; concurrencyToken: string; versions: PromptVersion[] }
export interface PromptBinding { id: string; usageKey: string; ownerType: string; ownerKey: string; promptTemplateId: string; promptTemplateKey: string; promptVersionId: string; promptVersionNumber: number; isActive: boolean; updatedAtUtc: string; concurrencyToken: string }
export interface PromptContent { id: string; systemPrompt: string; userPrompt?: string | null; requiredVariablesJson: string; responseFormat?: string | null; contentHash: string }

export async function listPromptTemplates() { return (await http.get<PromptTemplate[]>('/admin/prompts')).data }
export async function listPromptBindings() { return (await http.get<PromptBinding[]>('/admin/prompts/bindings')).data }
export async function getPromptContent(versionId: string) { return (await http.get<PromptContent>(`/admin/prompts/versions/${versionId}/content`)).data }
export async function createPromptTemplate(input: { key: string; name: string; description?: string; systemPrompt: string; requiredVariablesJson: string }) {
  return (await http.post<PromptTemplate>('/admin/prompts', { requestId: crypto.randomUUID(), userPrompt: null, responseFormat: null, changeSummary: 'Created in Admin', ...input })).data
}
export async function createDraft(templateId: string, input: { systemPrompt: string; requiredVariablesJson: string; changeSummary?: string }) {
  return (await http.post<PromptVersion>(`/admin/prompts/${templateId}/versions`, { requestId: crypto.randomUUID(), userPrompt: null, responseFormat: null, ...input })).data
}
export async function publishPrompt(versionId: string, concurrencyToken: string, activateUsageKeys: string[]) {
  return (await http.post<PromptVersion>(`/admin/prompts/versions/${versionId}/publish`, { requestId: crypto.randomUUID(), concurrencyToken, reason: null, activateUsageKeys })).data
}
