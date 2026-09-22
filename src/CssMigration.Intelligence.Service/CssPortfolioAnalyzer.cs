// File purpose: Orchestrates inventory, coverage, token discovery, migration cohorts, and portfolio-level summaries.
using CssMigration.Intelligence.Core;

namespace CssMigration.Intelligence.Service;

public sealed class CssPortfolioAnalyzer
{
    private static readonly IReadOnlyList<string> CategoryNames =
    [
        "global-token", "component-token", "supported-layout-variant", "structured-override",
        "compatibility-css", "unsupported-or-unsafe", "obsolete"
    ];

    private readonly CssInventoryEngine _inventoryEngine = new();
    private readonly CssCoverageAnalyzer _coverageAnalyzer = new();
    private readonly CssPortfolioValidator _validator = new();
    private readonly DesignTokenDiscovery _tokenDiscovery = new();
    private readonly CssModuleReporter _moduleReporter = new();

    public CssAnalysisResponse AnalyzeSingle(string? sourceId, string css)
    {
        if (string.IsNullOrWhiteSpace(css))
            throw new PortfolioValidationException([new("css.required", "Paste at least one CSS rule to analyse.")]);
        if (css.Length > CssPortfolioValidator.MaximumCssLength)
            throw new PortfolioValidationException([new("css.too_large", $"CSS must not exceed {CssPortfolioValidator.MaximumCssLength:N0} characters.")]);

        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceId) ? "pasted-synthetic.css" : sourceId.Trim();
        var inventory = _inventoryEngine.Analyze([new CssSource(normalizedSourceId, css)]);
        return new(inventory, _coverageAnalyzer.Analyze(inventory));
    }

    public CssPortfolioAnalysisResponse AnalyzePortfolio(CssPortfolioInput input)
    {
        _validator.Validate(input);
        var analyses = input.Sources.Select(source =>
        {
            var result = AnalyzeSingle(source.SourceId, source.Css);
            var mode = DetermineMode(result.Coverage);
            var cohort = DetermineCohort(result.Coverage);
            return new TenantAnalysis(source, result, mode, cohort, GovernedCoverage(result.Coverage));
        }).ToArray();

        var tenants = analyses.Select(item => new MigrationTenantSummary(
            item.Source.TenantKey,
            item.Source.DisplayName,
            item.Source.SourceId,
            item.Source.Version,
            item.Source.UpdatedAt,
            item.Mode,
            item.Cohort.Id,
            item.Result.Coverage.Summary.EvidenceUnitCount,
            item.GovernedCoverage,
            item.Result.Coverage.Assessments.Count(assessment => assessment.RequiresSemanticReview),
            item.Result.Coverage.Assessments.Count(assessment => assessment.Category == "unsupported-or-unsafe"),
            item.Result.Coverage.Summary.ParseErrorCount == 0 ? "passed" : "failed",
            item.Result.Coverage.Summary.Categories)).ToArray();

        var cohorts = analyses.GroupBy(item => item.Cohort.Id, StringComparer.Ordinal)
            .Select(group => new MigrationCohort(
                group.Key,
                group.First().Cohort.Label,
                group.First().Cohort.Description,
                group.Count(),
                group.Select(item => item.Source.TenantKey).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                group.First().Cohort.RequiresTenantInspection))
            .OrderByDescending(item => item.TenantCount)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var exceptions = analyses.SelectMany(item => item.Result.Coverage.Assessments
                .Where(assessment => assessment.RequiresSemanticReview || assessment.Category == "unsupported-or-unsafe")
                .Select(assessment => new MigrationExceptionItem(
                    item.Source.TenantKey,
                    assessment.EvidenceId,
                    assessment.Category,
                    assessment.EvidenceText,
                    assessment.Reason,
                    assessment.SourceId,
                    assessment.Location.Line,
                    assessment.AiSuggestionAllowed)))
            .OrderBy(item => item.TenantKey, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ToArray();

        var failedChecks = analyses.SelectMany(item => item.Result.Coverage.UnassessedDiagnostics
                .Select(diagnostic => new MigrationFailedCheck(
                    item.Source.TenantKey,
                    diagnostic.SourceId,
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location.Line)))
            .OrderBy(item => item.TenantKey, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ToArray();

        var tokenPlan = _tokenDiscovery.Discover(analyses.Select(item => (item.Source, item.Result.Inventory)).ToArray());
        return new(
            "1.0",
            "synthetic-or-explicitly-approved-input-only",
            DateTimeOffset.UtcNow,
            tenants.Length,
            new[] { "raw", "hybrid", "tokenized" }.Select(mode => new MigrationModeCount(mode, tenants.Count(item => item.MigrationMode == mode))).ToArray(),
            AggregateCoverage(analyses),
            cohorts,
            CommonPatterns(analyses),
            tokenPlan,
            tenants,
            exceptions,
            failedChecks,
            _moduleReporter.CandidateCoverage(input, tokenPlan));
    }

    private static IReadOnlyList<CssCategoryCoverage> AggregateCoverage(IReadOnlyList<TenantAnalysis> analyses)
    {
        var counts = analyses.SelectMany(item => item.Result.Coverage.Assessments)
            .GroupBy(item => item.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var total = counts.Values.Sum();
        var coverage = CategoryNames.Select(category =>
        {
            counts.TryGetValue(category, out var count);
            var percentage = total == 0 ? 0m : Math.Round(count * 100m / total, 2, MidpointRounding.AwayFromZero);
            return new CssCategoryCoverage(category, count, percentage);
        }).ToArray();

        if (total == 0) return coverage;
        var difference = 100m - coverage.Sum(item => item.Percentage);
        var adjustmentIndex = Array.FindIndex(coverage, item => item.Count == coverage.Max(candidate => candidate.Count));
        coverage[adjustmentIndex] = coverage[adjustmentIndex] with { Percentage = coverage[adjustmentIndex].Percentage + difference };
        return coverage;
    }

    private static IReadOnlyList<MigrationCommonPattern> CommonPatterns(IReadOnlyList<TenantAnalysis> analyses)
        => analyses.SelectMany(item => item.Result.Coverage.Assessments.SelectMany(assessment =>
                assessment.SuggestedTargets.Select(target => new { item.Source.TenantKey, assessment.Category, Target = target })))
            .GroupBy(item => new { item.Target, item.Category })
            .Select(group => new MigrationCommonPattern(
                group.Key.Target,
                group.Key.Category,
                group.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).Count(),
                group.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray()))
            .Where(item => item.TenantCount > 1)
            .OrderByDescending(item => item.TenantCount)
            .ThenBy(item => item.Target, StringComparer.Ordinal)
            .ToArray();

    private static string DetermineMode(CssCoverageReport coverage)
    {
        if (coverage.Summary.ParseErrorCount > 0 || HasCategory(coverage, "unsupported-or-unsafe") || HasCategory(coverage, "compatibility-css")) return "raw";
        if (HasCategory(coverage, "component-token") || HasCategory(coverage, "structured-override")) return "hybrid";
        return "tokenized";
    }

    private static CohortDefinition DetermineCohort(CssCoverageReport coverage)
    {
        if (coverage.Summary.ParseErrorCount > 0 || HasCategory(coverage, "unsupported-or-unsafe"))
            return new("blocked", "Blocked", "Unsafe constructs or failed parsing require manual inspection.", true);
        if (HasCategory(coverage, "compatibility-css"))
            return new("compatibility-review", "Compatibility review", "Valid CSS remains outside the governed token and variant model.", true);
        if (HasCategory(coverage, "component-token") || HasCategory(coverage, "structured-override"))
            return new("governed-review", "Governed review", "Typed tokens or structured overrides can replace current rules after review.", true);
        return new("token-ready", "Token ready", "Current evidence maps to governed tokens and supported variants.", false);
    }

    private static decimal GovernedCoverage(CssCoverageReport coverage)
        => coverage.Summary.Categories.Where(item => item.Category is "global-token" or "component-token" or "supported-layout-variant" or "structured-override" or "obsolete").Sum(item => item.Percentage);

    private static bool HasCategory(CssCoverageReport coverage, string category)
        => coverage.Summary.Categories.Any(item => item.Category == category && item.Count > 0);

    private sealed record CohortDefinition(string Id, string Label, string Description, bool RequiresTenantInspection);
    private sealed record TenantAnalysis(CssPortfolioSource Source, CssAnalysisResponse Result, string Mode, CohortDefinition Cohort, decimal GovernedCoverage);
}
