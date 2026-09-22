// File purpose: Classifies parsed CSS evidence into migration paths and calculates stable portfolio coverage percentages.
namespace CssMigration.Intelligence.Core;

public sealed class CssCoverageAnalyzer
{
    public CssCoverageReport Analyze(CssInventoryReport inventory, CssCoveragePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        policy ??= CssCoveragePolicy.ThemeConfigurationV1();

        var assessments = inventory.Sources
            .SelectMany(source => AssessSource(source, policy))
            .OrderBy(item => item.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.Location.Offset)
            .ThenBy(item => item.EvidenceId, StringComparer.Ordinal)
            .ToArray();

        var categoryCounts = Enum.GetValues<CssMigrationCategory>()
            .ToDictionary(category => category, category => assessments.Count(item => item.Category == CategoryName(category)));
        var percentages = AllocatePercentages(categoryCounts, assessments.Length);
        var categories = Enum.GetValues<CssMigrationCategory>()
            .Select(category => new CssCategoryCoverage(CategoryName(category), categoryCounts[category], percentages[category]))
            .ToArray();

        var diagnostics = inventory.Sources
            .SelectMany(source => source.Diagnostics.Select(diagnostic => new CssUnassessedDiagnostic(
                source.SourceId,
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Message,
                diagnostic.Location)))
            .OrderBy(item => item.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.Location.Offset)
            .ToArray();

        return new CssCoverageReport(
            "1.0.0",
            inventory.SchemaVersion,
            policy.Version,
            new CssCoverageSummary(
                assessments.Length,
                assessments.Length,
                diagnostics.Count(item => item.Severity == "error"),
                categories),
            assessments,
            diagnostics);
    }

    private static IEnumerable<CssCoverageAssessment> AssessSource(CssSourceInventory source, CssCoveragePolicy policy)
    {
        foreach (var rule in source.Rules)
            yield return AssessRule(source.SourceId, rule, policy);

        foreach (var atRule in source.AtRules.Where(rule => !rule.HasBlock))
            yield return AssessStatementAtRule(source.SourceId, atRule);
    }

    private static CssCoverageAssessment AssessRule(string sourceId, CssRuleInventory rule, CssCoveragePolicy policy)
    {
        var flags = rule.Flags
            .Concat(rule.Declarations.SelectMany(declaration => declaration.Flags))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(flag => flag, StringComparer.Ordinal)
            .ToArray();

        if (flags.Any(IsUnsafe))
            return RuleAssessment(sourceId, rule, CssMigrationCategory.UnsupportedOrUnsafe,
                "The rule contains a mechanically detected unsafe construct and cannot enter an automated migration path.",
                ["manual-security-review"], false, false, flags);

        if (policy.ObsoleteSelectorMarkers.Any(marker => rule.SelectorText.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return RuleAssessment(sourceId, rule, CssMigrationCategory.Obsolete,
                "The selector is explicitly marked as legacy, obsolete, or deprecated in the synthetic policy.",
                ["remove-after-visual-verification"], true, true, flags);

        if (IsMappedGlobalTokenRule(rule, policy, out var globalTargets))
            return RuleAssessment(sourceId, rule, CssMigrationCategory.GlobalToken,
                "All declarations are known global custom properties with direct Theme Configuration v1 targets.",
                globalTargets, false, false, flags);

        if (IsSupportedLayoutRule(rule, policy))
            return RuleAssessment(sourceId, rule, CssMigrationCategory.SupportedLayoutVariant,
                "The rule affects a supported navigation component and contains only layout or positioning declarations.",
                ["navigation.position", "navigation.destinations", "layout.density"], false, false, flags);

        if (IsComponentTokenRule(rule, policy))
            return RuleAssessment(sourceId, rule, CssMigrationCategory.ComponentToken,
                "The rule contains only visual properties that can be represented as governed component-level tokens.",
                ComponentTargets(rule), true, true, flags);

        if (IsStructuredOverrideRule(rule))
            return RuleAssessment(sourceId, rule, CssMigrationCategory.StructuredOverride,
                "The rule is safe and structurally parseable, but its component-specific layout values need a typed override rather than a global token.",
                [$"structuredOverrides.{PrimaryComponent(rule)}.layout"], true, true, flags);

        return RuleAssessment(sourceId, rule, CssMigrationCategory.CompatibilityCss,
            "The rule is valid and not unsafe, but Theme Configuration v1 has no deterministic token, variant, or structured override mapping for it.",
            ["compatibilityCss"], true, true, flags);
    }

    private static CssCoverageAssessment AssessStatementAtRule(string sourceId, CssAtRuleInventory atRule)
    {
        var flags = atRule.Flags.Distinct(StringComparer.Ordinal).OrderBy(flag => flag, StringComparer.Ordinal).ToArray();
        var category = flags.Any(IsUnsafe)
            ? CssMigrationCategory.UnsupportedOrUnsafe
            : CssMigrationCategory.CompatibilityCss;
        var reason = category == CssMigrationCategory.UnsupportedOrUnsafe
            ? "The standalone at-rule contains a mechanically detected unsafe or externally coupled construct."
            : "The standalone at-rule has no direct Theme Configuration v1 representation.";

        return new CssCoverageAssessment(
            $"{sourceId}:A{atRule.Location.Offset}",
            sourceId,
            "statement-at-rule",
            $"@{atRule.Name}{(string.IsNullOrWhiteSpace(atRule.Prelude) ? string.Empty : $" {atRule.Prelude}")}",
            CategoryName(category),
            reason,
            category == CssMigrationCategory.UnsupportedOrUnsafe ? ["manual-security-review"] : ["compatibilityCss"],
            category == CssMigrationCategory.CompatibilityCss,
            category == CssMigrationCategory.CompatibilityCss,
            flags,
            atRule.Location);
    }

    private static CssCoverageAssessment RuleAssessment(
        string sourceId,
        CssRuleInventory rule,
        CssMigrationCategory category,
        string reason,
        IReadOnlyList<string> suggestedTargets,
        bool requiresSemanticReview,
        bool aiSuggestionAllowed,
        IReadOnlyList<string> flags)
        => new(
            rule.RuleId,
            sourceId,
            "style-rule",
            rule.SelectorText,
            CategoryName(category),
            reason,
            suggestedTargets,
            requiresSemanticReview,
            aiSuggestionAllowed,
            flags,
            rule.Location);

    private static bool IsMappedGlobalTokenRule(
        CssRuleInventory rule,
        CssCoveragePolicy policy,
        out IReadOnlyList<string> targets)
    {
        targets = [];
        if (!rule.Selectors.Any(selector => selector.Text.Equals(":root", StringComparison.OrdinalIgnoreCase))) return false;
        if (rule.Declarations.Count == 0) return false;
        var mapped = rule.Declarations
            .Select(declaration => policy.GlobalCustomPropertyTargets.TryGetValue(declaration.Property, out var target) ? target : null)
            .ToArray();
        if (mapped.Any(target => target is null)) return false;
        targets = mapped.Select(target => target!).Distinct(StringComparer.Ordinal).OrderBy(target => target, StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool IsSupportedLayoutRule(CssRuleInventory rule, CssCoveragePolicy policy)
        => rule.Declarations.Count > 0
            && rule.Declarations.All(declaration => declaration.Category == "layout-or-positioning")
            && rule.AffectedComponents.Any(policy.SupportedLayoutComponents.Contains);

    private static bool IsComponentTokenRule(CssRuleInventory rule, CssCoveragePolicy policy)
        => rule.Declarations.Count > 0
            && rule.AffectedComponents.Count > 0
            && rule.Declarations.All(declaration => policy.ComponentTokenProperties.Contains(declaration.Property));

    private static bool IsStructuredOverrideRule(CssRuleInventory rule)
        => rule.Declarations.Count > 0
            && rule.AffectedComponents.Count > 0
            && rule.Declarations.All(declaration => declaration.Category == "layout-or-positioning");

    private static IReadOnlyList<string> ComponentTargets(CssRuleInventory rule)
        => MeaningfulComponents(rule)
            .SelectMany(component => rule.Declarations.Select(declaration => $"components.{component}.{NormalizeProperty(declaration.Property)}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();

    private static string PrimaryComponent(CssRuleInventory rule)
        => MeaningfulComponents(rule).FirstOrDefault()?.Replace('#', '_') ?? "unknown";

    private static IEnumerable<string> MeaningfulComponents(CssRuleInventory rule)
        => rule.AffectedComponents.Where(component =>
            !component.StartsWith('#')
            && !component.StartsWith('[')
            && !component.StartsWith("is-", StringComparison.OrdinalIgnoreCase)
            && component is not ("html" or "body"));

    private static string NormalizeProperty(string property)
    {
        var parts = property.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? property
            : parts[0] + string.Concat(parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static bool IsUnsafe(string flag) => flag.StartsWith("unsafe:", StringComparison.Ordinal);

    private static Dictionary<CssMigrationCategory, decimal> AllocatePercentages(
        IReadOnlyDictionary<CssMigrationCategory, int> counts,
        int total)
    {
        var result = Enum.GetValues<CssMigrationCategory>().ToDictionary(category => category, _ => 0m);
        if (total == 0) return result;

        var basisPoints = new Dictionary<CssMigrationCategory, int>();
        var remainders = new List<(CssMigrationCategory Category, int Remainder)>();
        foreach (var category in Enum.GetValues<CssMigrationCategory>())
        {
            var numerator = counts[category] * 10_000;
            basisPoints[category] = numerator / total;
            remainders.Add((category, numerator % total));
        }

        var undistributed = 10_000 - basisPoints.Values.Sum();
        foreach (var item in remainders.OrderByDescending(item => item.Remainder).ThenBy(item => item.Category).Take(undistributed))
            basisPoints[item.Category]++;

        foreach (var category in Enum.GetValues<CssMigrationCategory>())
            result[category] = basisPoints[category] / 100m;
        return result;
    }

    private static string CategoryName(CssMigrationCategory category) => category switch
    {
        CssMigrationCategory.GlobalToken => "global-token",
        CssMigrationCategory.ComponentToken => "component-token",
        CssMigrationCategory.SupportedLayoutVariant => "supported-layout-variant",
        CssMigrationCategory.StructuredOverride => "structured-override",
        CssMigrationCategory.CompatibilityCss => "compatibility-css",
        CssMigrationCategory.UnsupportedOrUnsafe => "unsupported-or-unsafe",
        CssMigrationCategory.Obsolete => "obsolete",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };
}
