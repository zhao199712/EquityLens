/** 歷史 run 唯讀顯示與篩選；這些名稱不能用於建立或重啟流程。 */
export const legacyWorkflow = { research: 'ResearchInvestigation', draft: 'DraftRevision', quality: 'ResearchQualityReview' } as const
export const currentWorkflowTypes = ['ResearchAgent', 'PortfolioDiagnosis', 'CriticReview'] as const
export const retiredWorkflowTypes = ['ResearchInvestigation', 'FeedbackRevision', 'DraftRevision', 'ResearchQualityReview', 'EvidenceRemediation', 'EvidenceReanalysis'] as const
export function isRetiredWorkflow(type: string) { return (retiredWorkflowTypes as readonly string[]).includes(type) }
export interface LegacyRevisionOutput {
  sourceAnswer?: string | null
  revisedAnswer?: string
  revisionSummary?: string
  revisionRequired?: boolean
  appliedRecommendation?: string | null
}
