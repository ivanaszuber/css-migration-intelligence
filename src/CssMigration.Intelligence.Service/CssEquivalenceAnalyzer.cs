// File purpose: Canonicalizes CSS implementations, finds cross-tenant equivalence, and defines browser render probes.
using System.Globalization;
using System.Text.RegularExpressions;
using CssMigration.Intelligence.Core;

namespace CssMigration.Intelligence.Service;

public sealed partial class CssEquivalenceAnalyzer
{
    private static readonly HashSet<string> SupportedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "background", "background-color", "border-color", "outline-color", "fill", "stroke",
        "font-family", "font-size", "font-weight", "line-height", "letter-spacing", "text-transform",
        "gap", "row-gap", "column-gap", "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left", "border-radius", "box-shadow", "text-shadow",
        "display", "flex-direction", "align-items", "justify-content", "grid-template-columns", "width", "height"
    };

    private static readonly HashSet<string> LayoutProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "display", "flex-direction", "align-items", "justify-content", "grid-template-columns", "width", "height"
    };

    public CssEquivalenceAnalysis Analyze(
        IReadOnlyList<(CssPortfolioSource Source, CssInventoryReport Inventory)> analyses)
    {
        var declarations = analyses.SelectMany(item => item.Inventory.Sources.SelectMany(source =>
            source.Rules.SelectMany(rule => rule.Declarations
                .Where(declaration => SupportedProperties.Contains(declaration.Property))
                .SelectMany(declaration => CssCanonicalizer.Canonicalize(declaration.Property, declaration.Value)
                    .Select(canonical => new CanonicalEvidence(
                        item.Source.TenantKey,
                        rule.SelectorText,
                        declaration.Property,
                        declaration.Value,
                        canonical.Property,
                        canonical.Value,
                        ComponentFor(rule.SelectorText),
                        declaration.Location.Line)))))).ToArray();

        var exactClusters = declarations
            .GroupBy(item => new { item.Component, item.CanonicalProperty, item.CanonicalValue })
            .Where(group => group.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).Count() >= 2)
            .Where(group => group.Select(item => $"{item.Selector}\n{item.RawProperty}\n{item.RawValue}").Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .Select(group => BuildCluster(group.Key.Component, group.Key.CanonicalProperty, group.Key.CanonicalValue, group.ToArray()))
            .OrderBy(cluster => cluster.Confidence == "high" ? 0 : 1)
            .ThenByDescending(cluster => cluster.TenantKeys.Count)
            .ThenBy(cluster => cluster.Component, StringComparer.Ordinal)
            .ThenBy(cluster => cluster.CanonicalProperty, StringComparer.Ordinal)
            .ToArray();

        var probes = declarations
            .Where(item => TargetSelectorFor(item.Component) is not null)
            .GroupBy(item => item.Component, StringComparer.Ordinal)
            .Select(group => new CssRenderProbe(
                StableId("probe", group.Key),
                group.Key,
                TargetSelectorFor(group.Key)!,
                group.Select(item => item.CanonicalProperty).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                group.Select(item => item.Selector).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Take(8).ToArray()))
            .OrderBy(probe => probe.Component, StringComparer.Ordinal)
            .Take(24)
            .ToArray();

        return new CssEquivalenceAnalysis(
            declarations.Length,
            exactClusters.Count(cluster => cluster.Confidence == "high"),
            exactClusters.Count(cluster => cluster.Confidence != "high"),
            exactClusters,
            probes,
            "Static canonical matches are proposals. Layout and behaviour remain review-only until browser evidence and human approval exist.");
    }

    public CssRenderedEvidenceReport CompareRendered(CssRenderedEvidenceRequest request)
    {
        var rows = request.Measurements.SelectMany(measurement => measurement.ComputedStyles.Select(style => new
        {
            measurement.TenantKey,
            measurement.ProbeId,
            measurement.Component,
            Property = style.Key.Trim().ToLowerInvariant(),
            Value = style.Value.Trim().ToLowerInvariant()
        })).Where(item => item.Property.Length > 0 && item.Value.Length > 0).ToArray();

        var styleClusters = rows.GroupBy(item => new { item.ProbeId, item.Component, item.Property, item.Value })
            .Where(group => group.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).Count() >= 2)
            .Select(group =>
            {
                var tenants = group.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                var layout = LayoutProperties.Contains(group.Key.Property);
                return new CssRenderedEquivalenceCluster(
                    group.Key.ProbeId,
                    group.Key.Component,
                    group.Key.Property,
                    group.Key.Value,
                    layout ? "medium" : "high",
                    layout ? "human-review-required" : "render-confirmed-token-candidate",
                    tenants,
                    layout
                        ? "The browser measured the same computed layout value, but equivalent layout behaviour still depends on DOM structure, responsive states and interaction."
                        : "The browser measured the same computed visual value for this canonical component across tenants. Static source evidence must still be retained for approval.");
            })
            .OrderBy(cluster => cluster.Confidence == "high" ? 0 : 1)
            .ThenByDescending(cluster => cluster.TenantKeys.Count)
            .ThenBy(cluster => cluster.Component, StringComparer.Ordinal)
            .ThenBy(cluster => cluster.Property, StringComparer.Ordinal)
            .ToArray();

        var geometryClusters = request.Measurements
            .Where(item => item.Geometry.Width > 0 && item.Geometry.Height > 0)
            .GroupBy(item => new
            {
                item.ProbeId,
                item.Component,
                Geometry = $"x:{item.Geometry.X:0.0};y:{item.Geometry.Y:0.0};w:{item.Geometry.Width:0.0};h:{item.Geometry.Height:0.0}"
            })
            .Where(group => group.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).Count() >= 2)
            .Select(group => new CssRenderedEquivalenceCluster(
                group.Key.ProbeId,
                group.Key.Component,
                "@geometry",
                group.Key.Geometry,
                "medium",
                "visual-geometry-review",
                group.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                "The fixture produced the same element geometry. This is supporting visual evidence only; responsive breakpoints, content variation and interaction states still require testing."))
            .ToArray();

        var clusters = styleClusters.Concat(geometryClusters)
            .OrderBy(cluster => cluster.Confidence == "high" ? 0 : 1)
            .ThenByDescending(cluster => cluster.TenantKeys.Count)
            .ThenBy(cluster => cluster.Component, StringComparer.Ordinal)
            .ThenBy(cluster => cluster.Property, StringComparer.Ordinal)
            .Take(140)
            .ToArray();

        return new(DateTimeOffset.UtcNow, "Browser-computed evidence from an isolated synthetic fixture; not production visual parity.", clusters);
    }

    private static CssEquivalenceCluster BuildCluster(
        string component,
        string property,
        string value,
        IReadOnlyList<CanonicalEvidence> evidence)
    {
        var layout = LayoutProperties.Contains(property);
        var implementations = evidence.Select(item => new CssImplementationEvidence(
                item.TenantKey, item.Selector, item.RawProperty, item.RawValue,
                item.CanonicalProperty, item.CanonicalValue, item.Line))
            .OrderBy(item => item.TenantKey, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ToArray();
        var tenants = implementations.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        return new CssEquivalenceCluster(
            StableId("equivalence", $"{component}|{property}|{value}"),
            component,
            property,
            value,
            layout ? "medium" : "high",
            layout ? "browser-and-human-review" : "safe-token-proposal",
            layout
                ? "Different source implementations canonicalize to the same layout declaration. Browser states and DOM structure must be checked before consolidation."
                : "Different source spellings or selectors canonicalize to the same reusable visual decision.",
            tenants,
            implementations);
    }

    internal static string ComponentFor(string selector)
    {
        var value = selector.ToLowerInvariant();
        var module = value.Contains("learning") || value.Contains("academy") ? "learning"
            : value.Contains("operation") || value.Contains("shift") || value.Contains("task") ? "operations"
            : value.Contains("home") || value.Contains("communication") ? "home"
            : "shared";
        var role = value.Contains("hero") || value.Contains("banner") ? "hero"
            : value.Contains("card") || value.Contains("feed-item") || value.Contains("tile") ? "card"
            : value.Contains("primary") && (value.Contains("button") || value.Contains("cta") || value.Contains("action")) ? "action-primary"
            : value.Contains("button") || value.Contains("cta") || value.Contains("action") ? "action"
            : value.Contains("active") || value.Contains("selected") ? "navigation-active"
            : value.Contains("nav") || value.Contains("menu") || value.Contains("tab") ? "navigation"
            : value.Contains("header") || value.Contains("topbar") ? "header"
            : value.Contains("input") || value.Contains("textarea") || value.Contains("select") ? "input"
            : value.Contains("title") || value.Contains("heading") || HeadingRegex().IsMatch(value) ? "heading"
            : value.Contains("body") || value.Contains("app-shell") || value.Contains(":root") ? "page"
            : "component";
        return $"{module}/{role}";
    }

    private static string? TargetSelectorFor(string component) => component switch
    {
        "shared/page" => ".app-shell",
        "shared/header" => ".app-header",
        "shared/navigation" => ".mobile-nav__item",
        "shared/navigation-active" => ".mobile-nav__item.is-active",
        "shared/card" => ".content-card",
        "shared/action" => ".action-button",
        "shared/action-primary" => ".primary-button",
        "shared/input" => ".form-field",
        "shared/heading" => ".page-heading",
        "home/hero" => ".module-home__hero",
        "home/card" => ".module-home__card",
        "home/action" or "home/action-primary" => ".module-home__action",
        "learning/hero" => ".module-learning__hero",
        "learning/card" => ".module-learning__card",
        "learning/action" or "learning/action-primary" => ".module-learning__action",
        "operations/hero" => ".module-operations__hero",
        "operations/card" => ".module-operations__card",
        "operations/action" or "operations/action-primary" => ".module-operations__action",
        _ => null
    };

    private static string StableId(string prefix, string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    [GeneratedRegex(@"\bh[1-6]\b", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    private sealed record CanonicalEvidence(
        string TenantKey,
        string Selector,
        string RawProperty,
        string RawValue,
        string CanonicalProperty,
        string CanonicalValue,
        string Component,
        int Line);
}

public static partial class CssCanonicalizer
{
    public sealed record CanonicalDeclaration(string Property, string Value);

    public static IReadOnlyList<CanonicalDeclaration> Canonicalize(string property, string value)
    {
        var normalizedProperty = property.Trim().ToLowerInvariant();
        var normalizedValue = WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");
        if (normalizedProperty is "padding" or "margin")
            return ExpandBox(normalizedProperty, normalizedValue);
        if (normalizedProperty == "background" && TryNormalizeColor(normalizedValue, out var backgroundColor))
            return [new("background-color", backgroundColor)];
        if (normalizedProperty is "color" or "background-color" or "border-color" or "outline-color" or "fill" or "stroke")
            normalizedValue = TryNormalizeColor(normalizedValue, out var color) ? color : normalizedValue;
        if (normalizedProperty == "font-weight")
            normalizedValue = normalizedValue switch { "normal" => "400", "bold" => "700", _ => normalizedValue };
        normalizedValue = ZeroUnitRegex().Replace(normalizedValue, "0");
        return [new(normalizedProperty, normalizedValue)];
    }

    private static IReadOnlyList<CanonicalDeclaration> ExpandBox(string property, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4 || parts.Any(part => part.Contains('(')))
            return [new(property, ZeroUnitRegex().Replace(value, "0"))];
        var top = parts[0];
        var right = parts.Length > 1 ? parts[1] : top;
        var bottom = parts.Length > 2 ? parts[2] : top;
        var left = parts.Length > 3 ? parts[3] : right;
        return new[] { top, right, bottom, left }.Select((part, index) =>
            new CanonicalDeclaration($"{property}-{new[] { "top", "right", "bottom", "left" }[index]}", ZeroUnitRegex().Replace(part, "0"))).ToArray();
    }

    private static bool TryNormalizeColor(string value, out string normalized)
    {
        normalized = value;
        if (value == "white") { normalized = "#ffffff"; return true; }
        if (value == "black") { normalized = "#000000"; return true; }
        var hex = HexColorRegex().Match(value);
        if (hex.Success)
        {
            var raw = hex.Groups[1].Value.ToLowerInvariant();
            normalized = raw.Length == 3
                ? $"#{raw[0]}{raw[0]}{raw[1]}{raw[1]}{raw[2]}{raw[2]}"
                : $"#{raw}";
            return true;
        }
        var rgb = RgbColorRegex().Match(value);
        if (!rgb.Success) return false;
        var channels = new[] { rgb.Groups[1].Value, rgb.Groups[2].Value, rgb.Groups[3].Value };
        if (channels.Any(channel => !byte.TryParse(channel, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return false;
        normalized = "#" + string.Concat(channels.Select(channel => byte.Parse(channel, CultureInfo.InvariantCulture).ToString("x2", CultureInfo.InvariantCulture)));
        return true;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<![a-z0-9_-])0(?:px|rem|em|%|vh|vw|vmin|vmax)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ZeroUnitRegex();

    [GeneratedRegex(@"^#([0-9a-f]{3}|[0-9a-f]{6})$", RegexOptions.IgnoreCase)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"^rgb\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbColorRegex();
}
