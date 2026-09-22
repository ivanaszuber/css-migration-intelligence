// File purpose: Discovers recurring literal styles, proposes semantic token candidates, measures coverage, and preserves residual evidence.
using System.Text.RegularExpressions;
using CssMigration.Intelligence.Core;

namespace CssMigration.Intelligence.Service;

public sealed partial class DesignTokenDiscovery
{
    public const decimal DefaultTargetPercentage = 80m;

    private static readonly HashSet<string> ColorProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "background", "background-color", "border-color", "outline-color", "fill", "stroke"
    };

    private static readonly HashSet<string> TypographyProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "font-family", "font-size", "font-weight", "line-height", "letter-spacing", "text-transform"
    };

    private static readonly HashSet<string> SpacingProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "gap", "row-gap", "column-gap", "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left"
    };

    private static readonly HashSet<string> RadiusProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "border-radius"
    };

    private static readonly HashSet<string> ShadowProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "box-shadow", "text-shadow"
    };

    public DesignTokenPlan Discover(IReadOnlyList<(CssPortfolioSource Source, CssInventoryReport Inventory)> analyses)
    {
        var declarations = analyses.SelectMany(item => item.Inventory.Sources.SelectMany(inventorySource =>
            inventorySource.Rules.SelectMany(rule => rule.Declarations.Select(declaration =>
                CreateEvidence(item.Source, rule, declaration))))).ToArray();

        var eligible = declarations.Where(item => item.Eligible).ToArray();
        var candidateGroups = eligible
            .GroupBy(item => item.TokenName, StringComparer.Ordinal)
            .Select(group => BuildCandidate(group.Key!, group.ToArray(), eligible.Length))
            .Where(candidate => candidate.TenantCount >= 2 || candidate.DeclarationCount >= 3)
            .OrderByDescending(candidate => candidate.DeclarationCount)
            .ThenByDescending(candidate => candidate.TenantCount)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();

        var selectedNames = new HashSet<string>(StringComparer.Ordinal);
        var selectedCount = 0;
        foreach (var candidate in candidateGroups)
        {
            if (eligible.Length > 0 && selectedCount * 100m / eligible.Length >= DefaultTargetPercentage) break;
            selectedNames.Add(candidate.Name);
            selectedCount += candidate.DeclarationCount;
        }

        var candidates = candidateGroups.Select(candidate => candidate with
        {
            SelectedForTarget = selectedNames.Contains(candidate.Name)
        }).ToArray();

        var coveredEvidence = eligible.Where(item => selectedNames.Contains(item.TokenName!)).ToHashSet();
        var residuals = analyses.Select(item => BuildTenantResidual(
                item.Source,
                declarations.Where(declaration => declaration.TenantKey == item.Source.TenantKey).ToArray(),
                coveredEvidence))
            .OrderBy(item => item.TenantKey, StringComparer.Ordinal)
            .ToArray();

        var eligibleCoverage = Percentage(coveredEvidence.Count, eligible.Length);
        return new DesignTokenPlan(
            DefaultTargetPercentage,
            declarations.Length,
            declarations.Count(item => item.AlreadyToken),
            eligible.Length,
            coveredEvidence.Count,
            eligibleCoverage,
            Percentage(coveredEvidence.Count, declarations.Length),
            eligibleCoverage >= DefaultTargetPercentage,
            candidates,
            residuals);
    }

    private static DeclarationEvidence CreateEvidence(
        CssPortfolioSource source,
        CssRuleInventory rule,
        CssDeclarationInventory declaration)
    {
        var unsafeRule = IsUnsafe(rule, declaration);
        var category = TokenCategory(declaration.Property);
        var scope = SemanticScope(rule.SelectorText);
        var alreadyToken = declaration.Property.StartsWith("--", StringComparison.Ordinal);
        var eligible = !unsafeRule && !alreadyToken && category is not null && IsLiteralValue(declaration.Value);
        var tokenName = CandidateNameFor(rule, declaration);

        return new DeclarationEvidence(
            source.TenantKey,
            rule.SelectorText,
            declaration.Property.ToLowerInvariant(),
            NormalizeValue(declaration.Value),
            declaration.Location.Line,
            category,
            scope,
            tokenName,
            eligible,
            unsafeRule,
            alreadyToken);
    }

    public static string? CandidateNameFor(CssRuleInventory rule, CssDeclarationInventory declaration)
    {
        var category = TokenCategory(declaration.Property);
        if (IsUnsafe(rule, declaration)
            || declaration.Property.StartsWith("--", StringComparison.Ordinal)
            || category is null
            || !IsLiteralValue(declaration.Value))
            return null;

        return $"--{category}-{SemanticScope(rule.SelectorText)}-{PropertyRole(declaration.Property)}";
    }

    private static bool IsUnsafe(CssRuleInventory rule, CssDeclarationInventory declaration)
        => rule.Flags.Concat(declaration.Flags)
            .Any(flag => flag.StartsWith("unsafe:", StringComparison.Ordinal));

    private static DesignTokenCandidate BuildCandidate(
        string tokenName,
        IReadOnlyList<DeclarationEvidence> evidence,
        int eligibleCount)
    {
        var first = evidence[0];
        var tenantCount = evidence.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).Count();
        var confidence = tenantCount >= 4 ? "high" : tenantCount >= 2 ? "medium" : "low";
        var values = evidence.GroupBy(item => new { item.TenantKey, item.Value })
            .Select(group => new TenantTokenValue(group.Key.TenantKey, group.Key.Value, group.Count()))
            .OrderBy(item => item.TenantKey, StringComparer.Ordinal)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .ToArray();

        return new DesignTokenCandidate(
            tokenName,
            first.Category!,
            first.Scope,
            first.Property,
            evidence.Count,
            tenantCount,
            Percentage(evidence.Count, eligibleCount),
            confidence,
            $"The same semantic role appears in {tenantCount} synthetic tenant(s) across {evidence.Count} declaration(s); tenant-specific values remain configurable.",
            false,
            values,
            evidence.Select(item => item.Selector).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Take(5).ToArray());
    }

    private static TenantTokenResidual BuildTenantResidual(
        CssPortfolioSource source,
        IReadOnlyList<DeclarationEvidence> declarations,
        IReadOnlySet<DeclarationEvidence> covered)
    {
        var eligible = declarations.Where(item => item.Eligible).ToArray();
        var coveredCount = eligible.Count(covered.Contains);
        var items = declarations.Where(item => !covered.Contains(item)).Select(item =>
        {
            var (reason, strategy, aiAllowed) = ResidualAdvice(item);
            return new TokenResidualItem(item.Selector, item.Property, item.Value, item.Line, reason, strategy, aiAllowed);
        }).OrderBy(item => item.Line).ThenBy(item => item.Property, StringComparer.Ordinal).ToArray();

        return new TenantTokenResidual(
            source.TenantKey,
            declarations.Count,
            eligible.Length,
            coveredCount,
            Percentage(coveredCount, eligible.Length),
            items);
    }

    private static (string Reason, string Strategy, bool AiAllowed) ResidualAdvice(DeclarationEvidence item)
    {
        if (item.Unsafe)
            return ("Mechanically unsafe or externally coupled CSS cannot enter an automated token path.", "Manual security and dependency review.", false);
        if (item.AlreadyToken)
            return ("This declaration is already a CSS custom property.", "Map the existing property to the governed token contract.", false);
        if (!item.Eligible && item.Category is null)
            return ("The property changes structure or behaviour rather than a reusable visual value.", "Model it as a typed component variant or a bounded compatibility rule.", true);
        if (!item.Eligible)
            return ("The value is dynamic or references an existing variable and cannot be inferred as a new literal token.", "Resolve the variable chain or keep the existing governed reference.", true);
        return ("This otherwise token-eligible semantic role is not repeated enough to enter the 80% portfolio set.", "Review as a component token, merge with a nearby semantic role, or retain as a documented exception.", true);
    }

    private static string? TokenCategory(string property)
    {
        if (ColorProperties.Contains(property)) return "color";
        if (TypographyProperties.Contains(property)) return property.StartsWith("font", StringComparison.OrdinalIgnoreCase) ? "font" : "type";
        if (SpacingProperties.Contains(property)) return "space";
        if (RadiusProperties.Contains(property)) return "radius";
        if (ShadowProperties.Contains(property)) return "shadow";
        return null;
    }

    private static string SemanticScope(string selector)
    {
        var lower = selector.ToLowerInvariant();
        // Module identity must survive generic title/card/hero classification so a PM can
        // review the controls belonging to their own module rather than one global bucket.
        var moduleClass = ClassSelectorRegex().Match(lower);
        if (moduleClass.Success && moduleClass.Groups[1].Value.StartsWith("module-", StringComparison.Ordinal))
            return NormalizeIdentifier(moduleClass.Groups[1].Value);
        if (lower.Contains("body") || lower.Contains(".app-shell")) return "page";
        if (lower.Contains("header") || lower.Contains("topbar")) return "header";
        if (lower.Contains("nav") || lower.Contains("tab-bar")) return lower.Contains("active") ? "navigation-active" : "navigation";
        if (lower.Contains("primary") && (lower.Contains("button") || lower.Contains("cta"))) return "action-primary";
        if (lower.Contains("secondary") && (lower.Contains("button") || lower.Contains("cta"))) return "action-secondary";
        if (lower.Contains("button") || lower.Contains(".btn")) return "action";
        if (lower.Contains("input") || lower.Contains("textarea") || lower.Contains("select") || lower.Contains("form-field")) return "input";
        if (lower.Contains("card") || lower.Contains("post") || lower.Contains("feed-item")) return "card";
        if (lower.Contains("modal") || lower.Contains("sheet") || lower.Contains("dialog")) return "overlay";
        if (lower.Contains("avatar") || lower.Contains("profile-photo")) return "avatar";
        if (lower.Contains("title") || lower.Contains("heading") || Regex.IsMatch(lower, @"\bh[1-6]\b")) return "heading";
        var match = ClassSelectorRegex().Match(lower);
        return match.Success ? NormalizeIdentifier(match.Groups[1].Value) : "global";
    }

    private static string PropertyRole(string property) => property.ToLowerInvariant() switch
    {
        "background" or "background-color" => "background",
        "color" or "fill" or "stroke" => "foreground",
        "border-color" or "outline-color" => "border",
        "border-radius" => "corner",
        "box-shadow" or "text-shadow" => "elevation",
        var value => NormalizeIdentifier(value)
    };

    private static bool IsLiteralValue(string value)
        => !value.Contains("var(", StringComparison.OrdinalIgnoreCase)
           && !value.Contains("calc(", StringComparison.OrdinalIgnoreCase)
           && !value.Contains("url(", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(value.Trim(), "inherit", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(value.Trim(), "initial", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(value.Trim(), "unset", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeValue(string value)
        => WhitespaceRegex().Replace(value.Trim(), " ").ToLowerInvariant();

    private static string NormalizeIdentifier(string value)
        => HyphenRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');

    private static decimal Percentage(int count, int total)
        => total == 0 ? 0m : Math.Round(count * 100m / total, 2, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"\.([a-zA-Z_][a-zA-Z0-9_-]*)")]
    private static partial Regex ClassSelectorRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex HyphenRegex();

    private sealed record DeclarationEvidence(
        string TenantKey,
        string Selector,
        string Property,
        string Value,
        int Line,
        string? Category,
        string Scope,
        string? TokenName,
        bool Eligible,
        bool Unsafe,
        bool AlreadyToken);
}
