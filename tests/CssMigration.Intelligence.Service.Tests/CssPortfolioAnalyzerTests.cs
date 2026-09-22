// File purpose: Verifies portfolio validation, token discovery, AI-contract checks, migration compilation, conflicts, and theme projection.
using CssMigration.Intelligence.Api;
using CssMigration.Intelligence.Service;

namespace CssMigration.Intelligence.Service.Tests;

public sealed class CssPortfolioAnalyzerTests
{
    [Fact]
    public void AzureContractValidator_ResolvesDuplicateCandidateAssignmentsWithoutDiscardingReview()
    {
        var candidates = new[]
        {
            Candidate("--radius-card-corner", "card", 4),
            Candidate("--radius-action-primary-corner", "action-primary", 3)
        };
        var plan = new DesignTokenPlan(80m, 7, 0, 7, 7, 100m, 100m, true, candidates, []);
        var payload = new AzureOpenAiRecommendationProvider.AiPayload(
        [
            new("--radius-surface", "radius", "Surface radius", ["--radius-card-corner"], "Rename card radius.", "12px"),
            new("--radius-action", "radius", "Action radius", ["--radius-card-corner", "--radius-action-primary-corner"], "Keep action radius distinct.", "8px")
        ],
        [],
        []);

        var contract = AzureOpenAiRecommendationProvider.ValidateAndMeasureContract(payload, plan);

        Assert.Equal(2, contract.ProposedTokenCount);
        Assert.Equal(7, contract.CoveredDeclarationCount);
        Assert.Equal(["--radius-card-corner"], contract.Tokens.Single(token => token.Name == "--radius-surface").SourceCandidateNames);
        Assert.Equal(["--radius-action-primary-corner"], contract.Tokens.Single(token => token.Name == "--radius-action").SourceCandidateNames);
    }

    [Fact]
    public void AzureContractParser_NormalizesBooleanHumanDecisionFlags()
    {
        var payload = AzureOpenAiRecommendationProvider.ParsePayload("""
            {
              "recommendedTokens": [],
              "residualStrategies": [{
                "pattern": "layout",
                "recommendedTreatment": "Create a typed variant.",
                "humanDecisionRequired": true,
                "affectedTenants": ["one"]
              }],
              "recommendations": []
            }
            """);
        var plan = new DesignTokenPlan(80m, 0, 0, 0, 0, 0m, 0m, false, [], []);

        var contract = AzureOpenAiRecommendationProvider.ValidateAndMeasureContract(payload, plan);

        Assert.Equal("Human review is required.", Assert.Single(contract.ResidualStrategies).HumanDecisionRequired);
    }

    [Fact]
    public void AzureContractValidator_KeepsLargestCompatiblePropertyGroupAndRetainsTheRemainder()
    {
        var candidates = new[]
        {
            Candidate("--space-card-block", "card", 8, "margin"),
            Candidate("--space-card-top", "card", 3, "margin-top"),
            Candidate("--space-card-bottom", "card", 2, "margin-bottom")
        };
        var plan = new DesignTokenPlan(80m, 13, 0, 13, 13, 100m, 100m, true, candidates, []);
        var payload = new AzureOpenAiRecommendationProvider.AiPayload(
        [
            new("--space-stack-sm", "spacing", "Stack spacing", candidates.Select(candidate => candidate.Name).ToArray(), "Consolidate similar spacing.", "8px")
        ],
        [],
        []);

        var contract = AzureOpenAiRecommendationProvider.ValidateAndMeasureContract(payload, plan);

        var aiToken = contract.Tokens.Single(token => token.Name == "--space-stack-sm");
        Assert.Equal(["--space-card-block"], aiToken.SourceCandidateNames);
        Assert.Contains(contract.Tokens, token => token.Name == "--space-card-top" && token.Origin == "deterministic-retained");
        Assert.Contains(contract.Tokens, token => token.Name == "--space-card-bottom" && token.Origin == "deterministic-retained");
        Assert.Equal(13, contract.CoveredDeclarationCount);
    }

    [Fact]
    public void AnalyzePortfolio_ProducesEvidenceLinkedPortfolioWithoutPersistence()
    {
        var input = new CssPortfolioInput("1.0", DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
        [
            new("synthetic-one", "Synthetic One", "one.css", "1", DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
                ":root { --client-primary: #0057b8; } .mobile-nav { display: grid; }"),
            new("synthetic-two", "Synthetic Two", "two.css", "2", DateTimeOffset.Parse("2026-08-01T11:00:00Z"),
                ".special-shape { clip-path: circle(50%); }")
        ]);

        var result = new CssPortfolioAnalyzer().AnalyzePortfolio(input);

        Assert.Equal(2, result.TenantCount);
        Assert.Equal("synthetic-or-explicitly-approved-input-only", result.EvidenceBoundary);
        Assert.Equal(100m, result.PortfolioCoverage.Sum(item => item.Percentage));
        Assert.Contains(result.Tenants, item => item.MigrationMode == "tokenized");
        Assert.Contains(result.Tenants, item => item.MigrationMode == "raw");
        Assert.Contains(result.Exceptions, item => item.Category == "compatibility-css" && item.AiSuggestionAllowed);
    }

    [Fact]
    public void AnalyzePortfolio_RejectsDuplicateSourceIdentity()
    {
        var input = new CssPortfolioInput("1.0", DateTimeOffset.UtcNow,
        [
            new("same", "One", "theme.css", "1", DateTimeOffset.UtcNow, ".one { color: red; }"),
            new("same", "Two", "theme.css", "2", DateTimeOffset.UtcNow, ".two { color: blue; }")
        ]);

        var exception = Assert.Throws<PortfolioValidationException>(() => new CssPortfolioAnalyzer().AnalyzePortfolio(input));

        Assert.Contains(exception.Errors, error => error.Code.EndsWith("identity.duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void MigrationCompiler_GeneratesTokenizedCopyAndPreservesResidualCss()
    {
        var input = new CssPortfolioInput("1.0", DateTimeOffset.UtcNow,
        [
            new("one", "One", "one.css", "1", DateTimeOffset.UtcNow,
                ".card { color: #111; display: grid; } .card-title { color: #111; }"),
            new("two", "Two", "two.css", "1", DateTimeOffset.UtcNow,
                ".card { color: #222; display: flex; } .card-title { color: #222; }")
        ]);
        var request = new CssMigrationBundleRequest(
            input,
            "1.0",
            [new("--color-surface-foreground", "Readable card text", null, ["--color-card-foreground"])]);

        var bundle = new CssMigrationCompiler().Compile(input, request);

        var tenant = bundle.Tenants.Single(item => item.TenantKey == "one");
        Assert.Equal(2, tenant.TokenizedDeclarationCount);
        Assert.Equal(1, tenant.ResidualDeclarationCount);
        Assert.Contains("--color-surface-foreground: #111", tenant.TokensCss);
        Assert.Contains("color: var(--color-surface-foreground)", tenant.MigratedCss);
        Assert.Contains("display: grid", tenant.MigratedCss);
        Assert.Contains("display: grid", tenant.ResidualCss);
        Assert.Equal(input.Sources[0].Css, tenant.OriginalCss);
        Assert.Contains("Illustrative target UI-kit CSS", bundle.SharedAppCss);
    }

    [Fact]
    public void MigrationCompiler_LeavesConflictingConsolidationsRawUntilTenantValueIsChosen()
    {
        var input = new CssPortfolioInput("1.0", DateTimeOffset.UtcNow,
        [
            new("one", "One", "one.css", "1", DateTimeOffset.UtcNow,
                ".card { color: #111; } .input { color: #333; }"),
            new("two", "Two", "two.css", "1", DateTimeOffset.UtcNow,
                ".card { color: #222; } .input { color: #444; }")
        ]);
        var request = new CssMigrationBundleRequest(
            input,
            "1.0",
            [new("--color-foreground-base", "Shared foreground", null, ["--color-card-foreground", "--color-input-foreground"])]);

        var bundle = new CssMigrationCompiler().Compile(input, request);

        Assert.All(bundle.Tenants, tenant =>
        {
            Assert.Equal("value-conflict", tenant.Status);
            Assert.Equal(0, tenant.TokenizedDeclarationCount);
            Assert.Single(tenant.ValueConflicts);
            Assert.Equal(2, tenant.ResidualDeclarationCount);
        });
    }

    [Fact]
    public void MigrationCompiler_MergeResolutionStandardizesTheTenantOnTheSelectedMeasuredValue()
    {
        var input = ConflictingTenantInput();
        var request = ConflictRequest(input,
            new("one", "--color-foreground-base", "merge", "#111"));

        var tenant = new CssMigrationCompiler().Compile(input, request).Tenants.Single(item => item.TenantKey == "one");

        Assert.Empty(tenant.ValueConflicts);
        Assert.Equal(2, tenant.TokenizedDeclarationCount);
        Assert.Contains("--color-foreground-base: #111", tenant.TokensCss);
        Assert.Equal(2, CountOccurrences(tenant.MigratedCss, "var(--color-foreground-base)"));
        var resolution = Assert.Single(tenant.AppliedConflictResolutions);
        Assert.Equal("merge", resolution.Strategy);
        Assert.Equal("#111", resolution.SelectedValue);
    }

    [Fact]
    public void MigrationCompiler_SplitResolutionCreatesOneTokenForEachMeasuredValue()
    {
        var input = ConflictingTenantInput();
        var request = ConflictRequest(input,
            new("one", "--color-foreground-base", "split", null,
            [
                new("#111", "--color-foreground-card"),
                new("#333", "--color-foreground-input")
            ]));

        var tenant = new CssMigrationCompiler().Compile(input, request).Tenants.Single(item => item.TenantKey == "one");

        Assert.Empty(tenant.ValueConflicts);
        Assert.Equal(2, tenant.TokenizedDeclarationCount);
        Assert.Contains("--color-foreground-card: #111", tenant.TokensCss);
        Assert.Contains("--color-foreground-input: #333", tenant.TokensCss);
        Assert.Contains(".card { color: var(--color-foreground-card); }", tenant.MigratedCss);
        Assert.Contains(".input { color: var(--color-foreground-input); }", tenant.MigratedCss);
        Assert.Equal(2, Assert.Single(tenant.AppliedConflictResolutions).SplitValues.Count);
    }

    [Fact]
    public void MigrationCompiler_RetainRawResolutionRecordsTheDecisionWithoutChangingTheCss()
    {
        var input = ConflictingTenantInput();
        var request = ConflictRequest(input,
            new("one", "--color-foreground-base", "retain-raw"));

        var tenant = new CssMigrationCompiler().Compile(input, request).Tenants.Single(item => item.TenantKey == "one");

        Assert.Empty(tenant.ValueConflicts);
        Assert.Equal("residual-review", tenant.Status);
        Assert.Equal(0, tenant.TokenizedDeclarationCount);
        Assert.Equal(2, tenant.ResidualDeclarationCount);
        Assert.Contains(input.Sources[0].Css, tenant.MigratedCss);
        Assert.DoesNotContain("var(--", tenant.MigratedCss);
        Assert.All(tenant.Residuals, residual => Assert.Contains("Human decision retained", residual.Reason));
        Assert.Equal("retain-raw", Assert.Single(tenant.AppliedConflictResolutions).Strategy);
    }

    [Fact]
    public void ThemeCompiler_ProducesTheSameRuntimeContractForTypedNewClientConfiguration()
    {
        var compiler = new TenantThemeCompiler();
        var configuration = compiler.NewFromPreset("warm", "new-one", "New One");

        var result = compiler.Compile(configuration);

        Assert.True(result.Publishable);
        Assert.Contains(result.Tokens, token => token.Name == "--color-action-primary-background" && token.Value == configuration.Brand.PrimaryColor);
        Assert.Contains("--radius-card-corner: 18px", result.TokensCss);
        Assert.Equal("new-client", result.Configuration.Source);
        Assert.Equal(0, result.LegacyResidualDeclarationCount);
    }

    [Fact]
    public void ThemeProjection_MapsMigratedEvidenceIntoTypedConfigurationAndQuarantinesResiduals()
    {
        var request = new MigratedThemeProjectionRequest(
            "existing-one",
            "Existing One",
            [
                new("--approved-primary", "#7257ff", "measured", ["--color-action-primary-background"]),
                new("--approved-background", "#faf8f5", "measured", ["--color-global-background"]),
                new("--approved-text", "#18231f", "measured", ["--color-global-foreground"]),
                new("--approved-radius", "18px", "measured", ["--radius-card-corner"])
            ],
            [new(".special", "clip-path", "circle(50%)", 12, "Unsupported shape", "Retain temporarily", false)]);

        var result = new TenantThemeCompiler().Project(request);

        Assert.Equal("migrated-css", result.Configuration.Source);
        Assert.Equal("#7257ff", result.Configuration.Brand.PrimaryColor);
        Assert.Equal("rounded", result.Configuration.Appearance.CornerStyle);
        Assert.Equal(1, result.Compilation.LegacyResidualDeclarationCount);
        Assert.Equal(4, result.ProjectionSummary.ResolvedTokenCount);
        Assert.Equal(4, result.ProjectionSummary.MappedTokenCount);
        Assert.Equal(0, result.ProjectionSummary.UnmappedTokenCount);
        Assert.Equal(1, result.ProjectionSummary.ResidualDeclarationCount);
        Assert.Contains(result.ProjectionSummary.Mappings, mapping => mapping.TargetPath == "brand.primaryColor" && mapping.Status == "mapped");
        Assert.Contains(result.ProjectionSummary.Mappings, mapping => mapping.TargetPath == "components.navigationVariant" && mapping.Status == "default-used");
        Assert.Contains(result.ProjectionNotes, note => note.Code == "projection.residuals.quarantined");
        Assert.Contains(result.Compilation.Tokens, token => token.Name == "--color-action-primary-background" && token.Value == "#7257ff");
    }

    [Fact]
    public void AnalyzePortfolio_DiscoversExactSemanticTokensAndTenantResiduals()
    {
        var input = new CssPortfolioInput("1.0", DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
        [
            new("synthetic-one", "Synthetic One", "one.css", "1", DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
                "body { color: #202020; background: #f7f7f7; } .content-card { background: #fff; border-radius: 12px; padding: 16px; } .primary-button { background: #0057b8; color: #fff; border-radius: 8px; padding: 12px 16px; }"),
            new("synthetic-two", "Synthetic Two", "two.css", "2", DateTimeOffset.Parse("2026-08-01T11:00:00Z"),
                "body { color: #18231f; background: #faf8f5; } .content-card { background: #fffdf8; border-radius: 10px; padding: 18px; } .primary-button { background: #174b3f; color: #fff; border-radius: 8px; padding: 12px 18px; } .special-grid { grid-template-columns: 2fr 1fr; }")
        ]);

        var result = new CssPortfolioAnalyzer().AnalyzePortfolio(input);

        Assert.NotEmpty(result.TokenPlan.Candidates);
        Assert.Contains(result.TokenPlan.Candidates, item => item.Name == "--color-page-foreground" && item.TenantCount == 2);
        Assert.Contains(result.TokenPlan.Candidates, item => item.Name == "--radius-card-corner");
        Assert.Contains(result.TokenPlan.Candidates, item => item.Name == "--color-action-primary-background");
        Assert.Contains(result.TokenPlan.TenantResiduals.Single(item => item.TenantKey == "synthetic-two").Items,
            item => item.Property == "grid-template-columns" && item.AiSuggestionAllowed);
    }

    [Fact]
    public void DesignTokenTarget_IsMeasuredAgainstEligibleDeclarationsAndNeverFabricated()
    {
        var input = new CssPortfolioInput("1.0", DateTimeOffset.UtcNow,
        [
            new("one", "One", "one.css", "1", DateTimeOffset.UtcNow,
                ".only-a { color: red; } .only-b { margin: 3px; } .layout { display: grid; }"),
            new("two", "Two", "two.css", "1", DateTimeOffset.UtcNow,
                ".different-a { color: blue; } .different-b { padding: 7px; } .layout { position: fixed; }")
        ]);

        var result = new CssPortfolioAnalyzer().AnalyzePortfolio(input);

        Assert.False(result.TokenPlan.TargetReached);
        Assert.Equal(0m, result.TokenPlan.EligibleCoveragePercentage);
        Assert.Equal(4, result.TokenPlan.EligibleDeclarationCount);
        Assert.All(result.TokenPlan.TenantResiduals, item => Assert.NotEmpty(item.Items));
    }

    [Fact]
    public async Task JsonProvider_LoadsTheVersionedContract()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                {"schemaVersion":"1.0","exportedAt":"2026-08-01T12:00:00Z","sources":[{"tenantKey":"synthetic","displayName":"Synthetic","sourceId":"theme.css","version":"1","updatedAt":"2026-08-01T11:00:00Z","css":".card { color: red; }"}]}
                """);

            var input = await new JsonCssSourceProvider(path).LoadAsync();

            Assert.Equal("1.0", input.SchemaVersion);
            Assert.Single(input.Sources);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DisabledAiProvider_ReturnsOnlyMeasuredCandidatesWithoutClaimingAi()
    {
        var candidate = new DesignTokenCandidate("--color-action", "color", "action", "color", 6, 1, 60m,
            "high", "Repeated measured value", true, [new TenantTokenValue("demo", "#123456", 6)], [".button"]);
        var plan = new DesignTokenPlan(80m, 10, 0, 8, 6, 75m, 60m, false, [candidate], []);

        var result = await new DisabledAiRecommendationProvider().RecommendAsync(plan);

        Assert.Equal("deterministic", result.Provider);
        Assert.Equal("completed-without-ai", result.Status);
        Assert.Empty(result.Recommendations);
        Assert.NotNull(result.ProposedContract);
        Assert.Equal(0, result.ProposedContract.AiConsolidatedTokenCount);
        Assert.Equal("--color-action", Assert.Single(result.ProposedContract.Tokens).Name);
        Assert.Contains("No model was called", result.Message);
    }

    [Fact]
    public void AnalyzePortfolio_ReportsExistingCustomPropertiesSeparately()
    {
        var input = new CssPortfolioInput("1.0", DateTimeOffset.UtcNow,
        [
            new("one", "One", "one.css", "1", DateTimeOffset.UtcNow,
                ":root { --old-brand: red; } .button { color: red; padding: 12px; }")
        ]);

        var result = new CssPortfolioAnalyzer().AnalyzePortfolio(input);

        Assert.Equal(1, result.TokenPlan.ExistingCustomPropertyDeclarationCount);
        Assert.DoesNotContain(result.TokenPlan.Candidates, candidate => candidate.Name == "--old-brand");
    }

    [Fact]
    public async Task ManifestProvider_LoadsComplexCssFilesAndRejectsPathTraversal()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var cssPath = Path.Combine(directory.FullName, "tenant.css");
            var manifestPath = Path.Combine(directory.FullName, "portfolio.manifest.json");
            await File.WriteAllTextAsync(cssPath, ".card { color: red; padding: 12px; }");
            await File.WriteAllTextAsync(manifestPath, """
                {"schemaVersion":"1.0","exportedAt":"2026-08-01T12:00:00Z","sources":[{"tenantKey":"synthetic","displayName":"Synthetic","sourceId":"theme.css","version":"1","updatedAt":"2026-08-01T11:00:00Z","cssPath":"tenant.css"}]}
                """);

            var input = await new FileManifestCssSourceProvider(manifestPath).LoadAsync();

            Assert.Single(input.Sources);
            Assert.Contains("padding", input.Sources[0].Css);

            await File.WriteAllTextAsync(manifestPath, """
                {"schemaVersion":"1.0","exportedAt":"2026-08-01T12:00:00Z","sources":[{"tenantKey":"synthetic","displayName":"Synthetic","sourceId":"theme.css","version":"1","updatedAt":"2026-08-01T11:00:00Z","cssPath":"../outside.css"}]}
                """);
            var exception = await Assert.ThrowsAsync<PortfolioValidationException>(() => new FileManifestCssSourceProvider(manifestPath).LoadAsync());
            Assert.Contains(exception.Errors, error => error.Code == "portfolio.css_path.invalid");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static DesignTokenCandidate Candidate(
        string name,
        string scope,
        int declarationCount,
        string cssProperty = "border-radius",
        string category = "radius")
        => new(
            name,
            category,
            scope,
            cssProperty,
            declarationCount,
            2,
            50m,
            "medium",
            "Synthetic test evidence.",
            true,
            [new("one", "8px", declarationCount)],
            [$".{scope}"]);

    private static CssPortfolioInput ConflictingTenantInput()
        => new("1.0", DateTimeOffset.UtcNow,
        [
            new("one", "One", "one.css", "1", DateTimeOffset.UtcNow,
                ".card { color: #111; } .input { color: #333; }"),
            new("two", "Two", "two.css", "1", DateTimeOffset.UtcNow,
                ".card { color: #222; } .input { color: #444; }")
        ]);

    private static CssMigrationBundleRequest ConflictRequest(CssPortfolioInput input, TenantConflictResolution resolution)
        => new(
            input,
            "1.0",
            [new("--color-foreground-base", "Shared foreground", null, ["--color-card-foreground", "--color-input-foreground"])],
            [resolution]);

    private static int CountOccurrences(string value, string expected)
        => value.Split(expected, StringSplitOptions.None).Length - 1;
}
