// File purpose: Defines the shared service contracts for analysis, AI advice, approvals, conflicts, migration bundles, and validation errors.
using CssMigration.Intelligence.Core;

namespace CssMigration.Intelligence.Service;

public sealed record CssPortfolioInput(
    string SchemaVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<CssPortfolioSource> Sources);

public sealed record CssPortfolioSource(
    string TenantKey,
    string DisplayName,
    string SourceId,
    string Version,
    DateTimeOffset UpdatedAt,
    string Css);

public sealed record CssAnalysisRequest(string? SourceId, string Css);

public sealed record CssAnalysisResponse(
    CssInventoryReport Inventory,
    CssCoverageReport Coverage);

public sealed record CssPortfolioAnalysisResponse(
    string SchemaVersion,
    string EvidenceBoundary,
    DateTimeOffset AnalyzedAt,
    int TenantCount,
    IReadOnlyList<MigrationModeCount> Modes,
    IReadOnlyList<CssCategoryCoverage> PortfolioCoverage,
    IReadOnlyList<MigrationCohort> Cohorts,
    IReadOnlyList<MigrationCommonPattern> CommonPatterns,
    DesignTokenPlan TokenPlan,
    IReadOnlyList<MigrationTenantSummary> Tenants,
    IReadOnlyList<MigrationExceptionItem> Exceptions,
    IReadOnlyList<MigrationFailedCheck> FailedChecks,
    IReadOnlyList<ModuleMigrationCoverage> Modules);

public sealed record ModuleMigrationCoverage(
    string Module,
    int TenantCount,
    int TotalDeclarationCount,
    int CandidateDeclarationCount,
    decimal CandidatePercentage,
    int ManualReviewCount);

public sealed record DesignTokenPlan(
    decimal TargetPercentage,
    int TotalDeclarationCount,
    int ExistingCustomPropertyDeclarationCount,
    int EligibleDeclarationCount,
    int SelectedDeclarationCount,
    decimal EligibleCoveragePercentage,
    decimal AllDeclarationCoveragePercentage,
    bool TargetReached,
    IReadOnlyList<DesignTokenCandidate> Candidates,
    IReadOnlyList<TenantTokenResidual> TenantResiduals);

public sealed record DesignTokenCandidate(
    string Name,
    string Category,
    string Scope,
    string CssProperty,
    int DeclarationCount,
    int TenantCount,
    decimal PortfolioSharePercentage,
    string Confidence,
    string Rationale,
    bool SelectedForTarget,
    IReadOnlyList<TenantTokenValue> Values,
    IReadOnlyList<string> ExampleSelectors);

public sealed record TenantTokenValue(
    string TenantKey,
    string Value,
    int DeclarationCount);

public sealed record TenantTokenResidual(
    string TenantKey,
    int TotalDeclarationCount,
    int EligibleDeclarationCount,
    int CoveredDeclarationCount,
    decimal EligibleCoveragePercentage,
    IReadOnlyList<TokenResidualItem> Items);

public sealed record TokenResidualItem(
    string Selector,
    string Property,
    string Value,
    int Line,
    string Reason,
    string SuggestedStrategy,
    bool AiSuggestionAllowed);

public sealed record AiRecommendationResponse(
    string Provider,
    string Status,
    DateTimeOffset GeneratedAt,
    string EvidenceBoundary,
    AiProposedTokenContract? ProposedContract,
    IReadOnlyList<AiMigrationRecommendation> Recommendations,
    string? Message = null);

public sealed record AiProposedTokenContract(
    int ProposedTokenCount,
    int AiConsolidatedTokenCount,
    int DeterministicRetainedTokenCount,
    int CoveredDeclarationCount,
    int EligibleDeclarationCount,
    decimal EligibleCoveragePercentage,
    decimal AllDeclarationCoveragePercentage,
    decimal TargetPercentage,
    bool TargetReached,
    IReadOnlyList<AiProposedToken> Tokens,
    IReadOnlyList<AiResidualStrategy> ResidualStrategies);

public sealed record AiProposedToken(
    string Name,
    string Category,
    string Purpose,
    string Rationale,
    string? SuggestedDefault,
    int DeclarationCount,
    int TenantCount,
    decimal EligibleCoveragePercentage,
    string Origin,
    IReadOnlyList<string> SourceCandidateNames);

public sealed record AiResidualStrategy(
    string Pattern,
    string RecommendedTreatment,
    string HumanDecisionRequired,
    IReadOnlyList<string> AffectedTenants);

public sealed record AiMigrationRecommendation(
    string Priority,
    string Title,
    string Recommendation,
    string Evidence,
    IReadOnlyList<string> AffectedTokens,
    IReadOnlyList<string> AffectedTenants,
    string HumanDecisionRequired);

public sealed record MigrationModeCount(string Mode, int Count);

public sealed record MigrationCohort(
    string Id,
    string Label,
    string Description,
    int TenantCount,
    IReadOnlyList<string> TenantKeys,
    bool RequiresTenantInspection);

public sealed record MigrationCommonPattern(
    string Target,
    string Category,
    int TenantCount,
    IReadOnlyList<string> TenantKeys);

public sealed record MigrationTenantSummary(
    string TenantKey,
    string DisplayName,
    string SourceId,
    string SourceVersion,
    DateTimeOffset SourceUpdatedAt,
    string MigrationMode,
    string CohortId,
    int EvidenceUnitCount,
    decimal GovernedCoveragePercentage,
    int ReviewItemCount,
    int UnsafeItemCount,
    string ValidationStatus,
    IReadOnlyList<CssCategoryCoverage> Coverage);

public sealed record MigrationExceptionItem(
    string TenantKey,
    string EvidenceId,
    string Category,
    string EvidenceText,
    string Reason,
    string SourceId,
    int Line,
    bool AiSuggestionAllowed);

public sealed record MigrationFailedCheck(
    string TenantKey,
    string SourceId,
    string Code,
    string Message,
    int Line);

public sealed record CssMigrationBundleRequest(
    CssPortfolioInput? Portfolio,
    string ContractVersion,
    IReadOnlyList<ApprovedTokenSelection> Tokens,
    IReadOnlyList<TenantConflictResolution>? ConflictResolutions = null);

public sealed record ApprovedTokenSelection(
    string Name,
    string Purpose,
    string? ApprovedDefault,
    IReadOnlyList<string> SourceCandidateNames,
    IReadOnlyList<ApprovedTenantTokenValue>? TenantValues = null);

public sealed record ApprovedTenantTokenValue(string TenantKey, string Value);

public sealed record TenantConflictResolution(
    string TenantKey,
    string TokenName,
    string Strategy,
    string? SelectedValue = null,
    IReadOnlyList<SplitTokenValue>? SplitValues = null);

public sealed record SplitTokenValue(string OriginalValue, string TokenName);

public sealed record CssMigrationBundle(
    string SchemaVersion,
    string ContractVersion,
    DateTimeOffset GeneratedAt,
    string EvidenceBoundary,
    MigrationBundleSummary Summary,
    IReadOnlyList<ApprovedTokenSelection> ApprovedTokens,
    IReadOnlyList<TenantMigrationArtifact> Tenants,
    IReadOnlyList<ModuleMigrationCoverage> Modules,
    string SharedAppCss);

public sealed record MigrationBundleSummary(
    int TenantCount,
    int ApprovedTokenCount,
    int TotalDeclarationCount,
    int TokenizedDeclarationCount,
    int ResidualDeclarationCount,
    decimal TokenizedPercentage,
    int TenantValueConflictCount);

public sealed record TenantMigrationArtifact(
    string TenantKey,
    string DisplayName,
    string SourceId,
    string SourceVersion,
    string ContractVersion,
    string Status,
    int TotalDeclarationCount,
    int TokenizedDeclarationCount,
    int ResidualDeclarationCount,
    int UnsafeDeclarationCount,
    decimal TokenizedPercentage,
    string OriginalCss,
    string TokensCss,
    string MigratedCss,
    string ResidualCss,
    IReadOnlyList<TenantTokenDefinition> TokenDefinitions,
    IReadOnlyList<TenantValueConflict> ValueConflicts,
    IReadOnlyList<AppliedTenantConflictResolution> AppliedConflictResolutions,
    IReadOnlyList<MigrationResidualDeclaration> Residuals);

public sealed record TenantTokenDefinition(
    string Name,
    string Value,
    string Source,
    IReadOnlyList<string> SourceCandidateNames);

public sealed record TenantValueConflict(
    string TokenName,
    IReadOnlyList<string> Values,
    IReadOnlyList<string> SourceCandidateNames,
    string Resolution);

public sealed record AppliedTenantConflictResolution(
    string TokenName,
    string Strategy,
    string Outcome,
    string? SelectedValue,
    IReadOnlyList<SplitTokenValue> SplitValues,
    IReadOnlyList<string> SourceCandidateNames);

public sealed record MigrationResidualDeclaration(
    string Selector,
    string Property,
    string Value,
    int Line,
    string Reason,
    string SuggestedStrategy,
    bool Unsafe);

public sealed record PortfolioValidationError(string Code, string Message);

public sealed class PortfolioValidationException(IReadOnlyList<PortfolioValidationError> errors)
    : Exception("The CSS portfolio input is invalid.")
{
    public IReadOnlyList<PortfolioValidationError> Errors { get; } = errors;
}
