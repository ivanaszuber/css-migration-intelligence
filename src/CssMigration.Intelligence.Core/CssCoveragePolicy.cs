// File purpose: Defines the versioned deterministic rules for tokens, supported layouts, and obsolete selectors.
namespace CssMigration.Intelligence.Core;

public sealed class CssCoveragePolicy
{
    private CssCoveragePolicy() { }

    public string Version { get; } = "theme-configuration-v1.0.0";

    public IReadOnlyDictionary<string, string> GlobalCustomPropertyTargets { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["--client-primary"] = "brand.primaryColor",
            ["--client-surface"] = "brand.surfaceColor",
            ["--client-on-primary"] = "brand.onPrimaryColor",
            ["--client-on-surface"] = "brand.onSurfaceColor"
        };

    public IReadOnlySet<string> ComponentTokenProperties { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "color", "background", "background-color", "border-color", "font-family", "font-size", "font-weight", "border-radius"
        };

    public IReadOnlySet<string> SupportedLayoutComponents { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mobile-nav", "mobile-nav__item"
        };

    public IReadOnlySet<string> ObsoleteSelectorMarkers { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "legacy-ie", "obsolete", "deprecated"
        };

    public static CssCoveragePolicy ThemeConfigurationV1() => new();
}
