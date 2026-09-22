// File purpose: Defines the AI recommendation boundary and a clearly labelled deterministic fallback when Azure AI is unavailable.
namespace CssMigration.Intelligence.Service;

public interface IAiRecommendationProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<AiRecommendationResponse> RecommendAsync(DesignTokenPlan plan, CancellationToken cancellationToken = default);
}

public sealed class DisabledAiRecommendationProvider : IAiRecommendationProvider
{
    public string ProviderName => "deterministic";
    public bool IsConfigured => false;

    public Task<AiRecommendationResponse> RecommendAsync(DesignTokenPlan plan, CancellationToken cancellationToken = default)
    {
        var selected = plan.Candidates.Where(candidate => candidate.SelectedForTarget).ToArray();
        var tokens = selected.Select(candidate => new AiProposedToken(
            candidate.Name,
            candidate.Category,
            $"Review the measured {candidate.Scope} {candidate.CssProperty} decision.",
            "Selected by deterministic coverage rules; no AI model was called.",
            candidate.Values.OrderByDescending(value => value.DeclarationCount).FirstOrDefault()?.Value,
            candidate.DeclarationCount,
            candidate.TenantCount,
            plan.EligibleDeclarationCount == 0 ? 0m : Math.Round(candidate.DeclarationCount * 100m / plan.EligibleDeclarationCount, 2),
            "deterministic-retained",
            [candidate.Name])).ToArray();

        var contract = new AiProposedTokenContract(
            tokens.Length,
            0,
            tokens.Length,
            plan.SelectedDeclarationCount,
            plan.EligibleDeclarationCount,
            plan.EligibleCoveragePercentage,
            plan.AllDeclarationCoveragePercentage,
            plan.TargetPercentage,
            plan.TargetReached,
            tokens,
            []);

        return Task.FromResult(new AiRecommendationResponse(
            ProviderName,
            "completed-without-ai",
            DateTimeOffset.UtcNow,
            "synthetic-evidence-only-no-model-call",
            contract,
            [],
            "Azure AI is unavailable. This draft uses only measured token candidates; review and approve it before generating artifacts. No model was called."));
    }
}
