using CssMigration.Intelligence.Service;

namespace CssMigration.Intelligence.Service.Tests;

public sealed class CssEquivalenceAnalyzerTests
{
    [Fact]
    public void AnalyzePortfolio_CanonicalizesEquivalentColorsAndBoxShorthands()
    {
        var now = DateTimeOffset.UtcNow;
        var input = new CssPortfolioInput("1.0", now,
        [
            new("north", "North", "north.css", "1", now,
                ".module-home__hero { background: #fff; padding: 8px 16px; }"),
            new("south", "South", "south.css", "1", now,
                ".home-banner { background-color: rgb(255, 255, 255); padding-top: 8px; padding-right: 16px; padding-bottom: 8px; padding-left: 16px; }")
        ]);

        var result = new CssPortfolioAnalyzer().AnalyzePortfolio(input);

        Assert.Contains(result.Equivalence.Clusters, cluster =>
            cluster.Component == "home/hero"
            && cluster.CanonicalProperty == "background-color"
            && cluster.CanonicalValue == "#ffffff"
            && cluster.Confidence == "high");
        Assert.Equal(4, result.Equivalence.Clusters.Count(cluster =>
            cluster.Component == "home/hero" && cluster.CanonicalProperty.StartsWith("padding-", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnalyzePortfolio_KeepsLayoutEquivalenceAtReviewConfidence()
    {
        var now = DateTimeOffset.UtcNow;
        var input = new CssPortfolioInput("1.0", now,
        [
            new("north", "North", "north.css", "1", now, ".content-card { display: grid; }"),
            new("south", "South", "south.css", "1", now, ".feed-item { display: grid; }")
        ]);

        var cluster = Assert.Single(new CssPortfolioAnalyzer().AnalyzePortfolio(input).Equivalence.Clusters);
        Assert.Equal("medium", cluster.Confidence);
        Assert.Equal("browser-and-human-review", cluster.Decision);
    }

    [Fact]
    public void CompareRendered_RequiresHumanReviewForLayoutProperties()
    {
        var request = new CssRenderedEvidenceRequest(
        [
            new("north", "probe-card", "shared/card", new Dictionary<string, string> { ["color"] = "rgb(1, 2, 3)", ["display"] = "grid" }, new(0, 0, 100, 40)),
            new("south", "probe-card", "shared/card", new Dictionary<string, string> { ["color"] = "rgb(1, 2, 3)", ["display"] = "grid" }, new(0, 0, 100, 40))
        ]);

        var report = new CssEquivalenceAnalyzer().CompareRendered(request);

        Assert.Contains(report.Clusters, cluster => cluster.Property == "color" && cluster.Confidence == "high");
        Assert.Contains(report.Clusters, cluster => cluster.Property == "display" && cluster.Confidence == "medium" && cluster.Decision == "human-review-required");
    }
}
