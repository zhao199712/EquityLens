namespace EquityLens.Api.Services.AgentRuns;

/// <summary>
/// CriticReview v0 workflow definition and blackboard helpers.
/// </summary>
public static class CriticReviewWorkflow
{
    /// <summary>
    /// Static workflow DAG definition for CriticReview v0.
    /// </summary>
    public static readonly object Definition = new
    {
        workflowType = "CriticReview",
        version = 1,
        nodes = new object[]
        {
            new { id = "loadResearchRun", type = "LoadResearchRun", required = true },
            new { id = "checkEvidence", type = "CheckEvidence", required = true },
            new { id = "critiqueAnswer", type = "CritiqueAnswer", required = true },
            new { id = "finalizeCriticReport", type = "FinalizeCriticReport", required = true }
        },
        edges = new object[]
        {
            new { @from = "loadResearchRun", to = "checkEvidence" },
            new { @from = "checkEvidence", to = "critiqueAnswer" },
            new { @from = "critiqueAnswer", to = "finalizeCriticReport" }
        }
    };

    /// <summary>
    /// Returns the initial blackboard state for a CriticReview run.
    /// </summary>
    public static object InitialBlackboard(Guid researchRunId) => new
    {
        ticker = (string?)null,
        question = (string?)null,
        researchRunId = researchRunId.ToString(),
        researchRun = (object?)null,
        answer = (string?)null,
        citations = Array.Empty<object>(),
        steps = Array.Empty<object>(),
        candidates = Array.Empty<object>(),
        evidenceChecks = new
        {
            citationCount = 0,
            missingCitationClaims = Array.Empty<object>(),
            weakEvidenceClaims = Array.Empty<object>(),
            unsupportedClaims = Array.Empty<object>()
        },
        criticFindings = Array.Empty<object>(),
        supervisorDecisions = Array.Empty<object>(),
        finalOutput = (object?)null
    };
}
