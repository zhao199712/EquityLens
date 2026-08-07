using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Agents;

public sealed record SourceRetrievalSelection(bool UseLocal, bool UseWeb);

public static class SourcePolicyRules
{
    public static bool AllowsLocal(SourcePolicy policy) => policy != SourcePolicy.WebOnly;

    public static bool AllowsWeb(SourcePolicy policy) => policy != SourcePolicy.LocalOnly;

    public static bool RequiresWeb(SourcePolicy policy) =>
        policy is SourcePolicy.WebOnly or SourcePolicy.LocalAndWeb;

    public static SourceRetrievalSelection SelectRemediationSources(
        SourcePolicy policy,
        int iteration,
        bool webAlreadyUsed,
        bool freshnessSensitive)
    {
        var boundedIteration = Math.Max(1, iteration);
        return policy switch
        {
            SourcePolicy.LocalOnly => new(true, false),
            SourcePolicy.WebOnly => new(false, !webAlreadyUsed),
            SourcePolicy.LocalThenWeb => boundedIteration == 1
                ? new(true, false)
                : new(false, !webAlreadyUsed),
            SourcePolicy.LocalAndWeb => new(true, !webAlreadyUsed),
            _ when boundedIteration == 1 => new(true, freshnessSensitive && !webAlreadyUsed),
            _ => webAlreadyUsed ? new(true, false) : new(false, true)
        };
    }
}
