import { http } from './http'

export type RiskPreference = 'Conservative' | 'Balanced' | 'Aggressive'
export interface RiskPreferenceProfile {
  value: RiskPreference
  label: string
  isDefault: boolean
  statement: string
}
export async function getRiskPreference(): Promise<RiskPreferenceProfile> {
  return (await http.get<RiskPreferenceProfile>('/settings/risk-preference')).data
}
export async function saveRiskPreference(value: RiskPreference): Promise<RiskPreferenceProfile> {
  return (await http.put<RiskPreferenceProfile>('/settings/risk-preference', { value })).data
}
