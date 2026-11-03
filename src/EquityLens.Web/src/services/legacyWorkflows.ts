/** 歷史 run 唯讀顯示與篩選；這些名稱不能用於建立或重啟流程。 */
export const legacyWorkflow = { research: 'ResearchInvestigation', draft: 'DraftRevision', quality: 'ResearchQualityReview' } as const
export const workflowReadFilters = ['ResearchAgent', 'PortfolioDiagnosis', 'CriticReview', 'ResearchInvestigation', 'FeedbackRevision', 'DraftRevision', 'ResearchQualityReview', 'EvidenceRemediation', 'EvidenceReanalysis']
export interface LegacyRevisionOutput {
  sourceAnswer?: string | null
  revisedAnswer?: string
  revisionSummary?: string
  revisionRequired?: boolean
  appliedRecommendation?: string | null
}
