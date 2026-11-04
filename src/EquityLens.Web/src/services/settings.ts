import { http } from './http'

export type RiskPreference = 'Conservative' | 'Balanced' | 'Aggressive'
export interface RiskPreferenceProfile {
  value: RiskPreference
  label: string
  isDefault: boolean
  statement: string
  source?: 'Default' | 'Manual' | 'Questionnaire'
  capacityLevel?: RiskPreference | null
  willingnessLevel?: RiskPreference | null
  assessedAt?: string | null
  stale?: boolean
  questionnaireVersion?: string | null
  thresholdVersion?: string
}
export async function getRiskPreference(): Promise<RiskPreferenceProfile> {
  return (await http.get<RiskPreferenceProfile>('/settings/risk-preference')).data
}
export async function saveRiskPreference(value: RiskPreference): Promise<RiskPreferenceProfile> {
  return (await http.put<RiskPreferenceProfile>('/settings/risk-preference', { value })).data
}

export type RiskAnswers = { c1: number; c2: number; c3: number; w1: number; w2: number; w3: number }
export async function submitRiskQuestionnaire(answers: RiskAnswers): Promise<RiskPreferenceProfile> {
  return (await http.post<RiskPreferenceProfile>('/settings/risk-preference/questionnaire', answers)).data
}
export async function deleteRiskPreference(): Promise<RiskPreferenceProfile> {
  return (await http.delete<RiskPreferenceProfile>('/settings/risk-preference')).data
}
