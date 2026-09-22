// File purpose: Defines the immutable contracts for coverage categories, assessments, summaries, and unassessed diagnostics.
namespace CssMigration.Intelligence.Core;

public enum CssMigrationCategory
{
    GlobalToken,
    ComponentToken,
    SupportedLayoutVariant,
    StructuredOverride,
    CompatibilityCss,
    UnsupportedOrUnsafe,
    Obsolete
}

public sealed record CssCoverageReport(
    string SchemaVersion,
    string InventorySchemaVersion,
    string PolicyVersion,
    CssCoverageSummary Summary,
    IReadOnlyList<CssCoverageAssessment> Assessments,
    IReadOnlyList<CssUnassessedDiagnostic> UnassessedDiagnostics);

public sealed record CssCoverageSummary(
    int EvidenceUnitCount,
    int AssessedEvidenceUnitCount,
    int ParseErrorCount,
    IReadOnlyList<CssCategoryCoverage> Categories);

public sealed record CssCategoryCoverage(
    string Category,
    int Count,
    decimal Percentage);

public sealed record CssCoverageAssessment(
    string EvidenceId,
    string SourceId,
    string EvidenceKind,
    string EvidenceText,
    string Category,
    string Reason,
    IReadOnlyList<string> SuggestedTargets,
    bool RequiresSemanticReview,
    bool AiSuggestionAllowed,
    IReadOnlyList<string> SourceFlags,
    CssSourceLocation Location);

public sealed record CssUnassessedDiagnostic(
    string SourceId,
    string Code,
    string Severity,
    string Message,
    CssSourceLocation Location);
