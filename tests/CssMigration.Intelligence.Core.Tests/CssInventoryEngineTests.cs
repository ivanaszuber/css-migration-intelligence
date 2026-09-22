// File purpose: Verifies CSS parsing, nested-rule handling, safety diagnostics, source locations, specificity, and coverage classification.
using CssMigration.Intelligence.Core;

namespace CssMigration.Intelligence.Core.Tests;

public sealed class CssInventoryEngineTests
{
    [Fact]
    public void Analyze_RepresentsSelectorsDeclarationsSpecificityAndMediaContext()
    {
        const string css = """
            .app-header, #shell .nav-item:hover { color: #fff !important; display: grid; }
            @media (max-width: 430px) { .mobile-nav[data-mode="compact"] { gap: 4px; } }
            """;

        var report = Analyze(css);

        Assert.Equal(2, report.Summary.StyleRuleCount);
        Assert.Equal(3, report.Summary.DeclarationCount);
        Assert.Equal(1, report.Summary.ImportantDeclarationCount);
        var first = report.Sources[0].Rules[0];
        Assert.Equal(2, first.Selectors.Count);
        Assert.Equal(new CssSpecificity(1, 2, 0), first.Selectors[1].Specificity);
        Assert.Contains("app-header", first.AffectedComponents);
        Assert.Contains("inventory:layout-or-positioning", first.Flags);
        Assert.Equal("(max-width: 430px)", report.Sources[0].Rules[1].MediaQueries.Single());
    }

    [Fact]
    public void Analyze_FlagsUnsafeAndReviewableConstructsWithoutAi()
    {
        const string css = """
            @import url("https://example.invalid/client.css");
            body .floating { position: fixed; behavior: url(widget.htc); background: url("javascript:alert(1)"); }
            """;

        var report = Analyze(css);

        Assert.Contains("unsafe:external-stylesheet-import", report.Sources[0].AtRules[0].Flags);
        var declarations = report.Sources[0].Rules.Single().Declarations;
        Assert.Contains("review:fixed-positioning", declarations.Single(item => item.Property == "position").Flags);
        Assert.Contains("unsafe:legacy-executable-property", declarations.Single(item => item.Property == "behavior").Flags);
        Assert.Contains("unsafe:javascript-url", declarations.Single(item => item.Property == "background").Flags);
        Assert.True(report.Summary.UnsafeConstructCount >= 3);
    }

    [Fact]
    public void Analyze_EmitsSourceLevelErrorForUnclosedRule()
    {
        var report = Analyze(".card { color: red;");

        var diagnostic = Assert.Single(report.Sources[0].Diagnostics);
        Assert.Equal("CSS004", diagnostic.Code);
        Assert.Equal("error", diagnostic.Severity);
        Assert.Equal(1, diagnostic.Location.Line);
        Assert.Equal(1, report.Summary.ErrorCount);
    }

    [Fact]
    public void Analyze_AccountsForEverySyntheticRuleOrExplicitParseError()
    {
        var report = new CssInventoryEngine().Analyze([
            new CssSource("valid.css", ".one { color: red; } @media (min-width: 1px) { .two { display: flex; } }"),
            new CssSource("invalid.css", ".three { padding: 1rem;")
        ]);

        Assert.Equal(2, report.Summary.SourceCount);
        Assert.Equal(2, report.Summary.StyleRuleCount);
        Assert.Single(report.Sources.Single(source => source.SourceId == "invalid.css").Diagnostics);
        Assert.Equal(1, report.Summary.ErrorCount);
    }

    [Fact]
    public void Coverage_ReconcilesEveryEvidenceUnitToExactlyOneHundredPercent()
    {
        var inventory = new CssInventoryEngine().Analyze([
            new CssSource("coverage.css", """
                @import url("https://example.invalid/client.css");
                :root { --client-primary: #f00; }
                .app-header { color: #fff; }
                .mobile-nav { display: grid; gap: 4px; }
                .campaign-card { position: fixed; right: 3px; }
                .client-special-shape { clip-path: polygon(0 0, 100% 0, 100% 100%); }
                .legacy-ie-banner { color: red; }
                """)
        ]);

        var coverage = new CssCoverageAnalyzer().Analyze(inventory);

        Assert.Equal(7, coverage.Summary.EvidenceUnitCount);
        Assert.Equal(coverage.Summary.EvidenceUnitCount, coverage.Summary.AssessedEvidenceUnitCount);
        Assert.Equal(100m, coverage.Summary.Categories.Sum(category => category.Percentage));
        Assert.Equal(coverage.Summary.EvidenceUnitCount, coverage.Summary.Categories.Sum(category => category.Count));
        Assert.All(coverage.Assessments, assessment =>
        {
            Assert.Equal("coverage.css", assessment.SourceId);
            Assert.True(assessment.Location.Line > 0);
            Assert.False(string.IsNullOrWhiteSpace(assessment.EvidenceId));
        });
    }

    [Fact]
    public void Coverage_UsesDeterministicCategoriesAndKeepsAiAdvisory()
    {
        var inventory = new CssInventoryEngine().Analyze([
            new CssSource("mapping.css", """
                :root { --client-primary: #f00; }
                .app-header { color: #fff; }
                .mobile-nav { display: grid; }
                .campaign-card { right: 3px; }
                .special-shape { clip-path: circle(50%); }
                .obsolete-widget { color: red; }
                """)
        ]);

        var coverage = new CssCoverageAnalyzer().Analyze(inventory);

        Assert.Equal("global-token", CategoryFor(coverage, ":root").Category);
        Assert.Equal("component-token", CategoryFor(coverage, ".app-header").Category);
        Assert.Equal("supported-layout-variant", CategoryFor(coverage, ".mobile-nav").Category);
        Assert.Equal("structured-override", CategoryFor(coverage, ".campaign-card").Category);
        Assert.Contains("structuredOverrides.campaign-card.layout", CategoryFor(coverage, ".campaign-card").SuggestedTargets);
        Assert.Equal("compatibility-css", CategoryFor(coverage, ".special-shape").Category);
        Assert.Equal("obsolete", CategoryFor(coverage, ".obsolete-widget").Category);
        Assert.False(CategoryFor(coverage, ":root").AiSuggestionAllowed);
        Assert.True(CategoryFor(coverage, ".special-shape").AiSuggestionAllowed);
    }

    [Fact]
    public void Coverage_RetainsParseErrorsOutsideTheReconciledDenominator()
    {
        var inventory = Analyze(".valid { color: red; } .unfinished { padding: 1rem;");

        var coverage = new CssCoverageAnalyzer().Analyze(inventory);

        Assert.Equal(1, coverage.Summary.EvidenceUnitCount);
        Assert.Equal(1, coverage.Summary.ParseErrorCount);
        var error = Assert.Single(coverage.UnassessedDiagnostics);
        Assert.Equal("CSS004", error.Code);
        Assert.Equal("synthetic.css", error.SourceId);
        Assert.Equal(100m, coverage.Summary.Categories.Sum(category => category.Percentage));
    }

    private static CssInventoryReport Analyze(string css)
        => new CssInventoryEngine().Analyze([new CssSource("synthetic.css", css)]);

    private static CssCoverageAssessment CategoryFor(CssCoverageReport coverage, string evidenceText)
        => coverage.Assessments.Single(assessment => assessment.EvidenceText == evidenceText);
}
