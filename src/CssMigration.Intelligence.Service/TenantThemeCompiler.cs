// File purpose: Compiles friendly theme settings into runtime tokens and projects migrated tenant evidence into Theme Studio.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CssMigration.Intelligence.Service;

public sealed partial class TenantThemeCompiler
{
    private static readonly ThemePreset[] PresetCatalog =
    [
        new("clean", "Clean and minimal", "Neutral surfaces with a clear blue action color.",
            new("#3157d5", "#7257ff", "#f7f8fa", "#ffffff", "#18211e", "Inter, sans-serif"),
            new("soft", "comfortable"), new("elevated", "light", "solid")),
        new("warm", "Warm and friendly", "Warm neutrals, coral actions and generous shapes.",
            new("#d74f3f", "#e7a641", "#fff8f1", "#ffffff", "#30231f", "Inter, sans-serif"),
            new("rounded", "comfortable"), new("elevated", "brand", "solid")),
        new("bold", "Bold and colorful", "High-energy purple branding with strong component contrast.",
            new("#6b3fd4", "#ff5630", "#f5f1ff", "#ffffff", "#241a38", "Inter, sans-serif"),
            new("soft", "spacious"), new("outlined", "brand", "pill")),
        new("high-contrast", "High contrast", "Accessible black, white and yellow foundation.",
            new("#111111", "#f2c500", "#ffffff", "#ffffff", "#111111", "Arial, sans-serif"),
            new("square", "comfortable"), new("outlined", "dark", "solid"))
    ];

    public IReadOnlyList<ThemePreset> Presets() => PresetCatalog;

    public TenantThemeConfiguration NewFromPreset(string presetId, string tenantKey = "new-tenant", string displayName = "New tenant")
    {
        var preset = PresetCatalog.FirstOrDefault(item => item.Id == presetId) ?? PresetCatalog[0];
        return new TenantThemeConfiguration(
            "1.0", tenantKey, displayName, "new-client", preset.Id,
            preset.Brand, preset.Appearance, preset.Components, []);
    }

    public ThemeCompilationResult Compile(TenantThemeConfiguration input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var diagnostics = Validate(input);
        var radius = input.Appearance.CornerStyle switch
        {
            "square" => "0px",
            "rounded" => "18px",
            _ => "10px"
        };
        var buttonRadius = input.Components.ButtonVariant == "pill" ? "999px" : radius;
        var spacing = input.Appearance.Density switch
        {
            "compact" => ("10px", "8px 12px"),
            "spacious" => ("22px", "14px 22px"),
            _ => ("16px", "11px 18px")
        };
        var tokens = new List<ThemeTokenValue>
        {
            new("--color-brand-primary", input.Brand.PrimaryColor, "brand.primaryColor"),
            new("--color-brand-accent", input.Brand.AccentColor, "brand.accentColor"),
            new("--color-global-background", input.Brand.BackgroundColor, "brand.backgroundColor"),
            new("--color-global-foreground", input.Brand.TextColor, "brand.textColor"),
            new("--color-surface-default", input.Brand.SurfaceColor, "brand.surfaceColor"),
            new("--color-action-primary-background", input.Brand.PrimaryColor, "brand.primaryColor"),
            new("--color-action-primary-foreground", BestForeground(input.Brand.PrimaryColor), "derived.contrast"),
            new("--color-navigation-background", input.Components.NavigationVariant == "dark" ? "#10221d" : input.Components.NavigationVariant == "brand" ? input.Brand.PrimaryColor : input.Brand.SurfaceColor, "components.navigationVariant"),
            new("--color-navigation-foreground", input.Components.NavigationVariant is "dark" or "brand" ? "#ffffff" : input.Brand.TextColor, "components.navigationVariant"),
            new("--font-global-family", input.Brand.FontFamily, "brand.fontFamily"),
            new("--radius-card-corner", radius, "appearance.cornerStyle"),
            new("--radius-action-primary-corner", buttonRadius, "components.buttonVariant"),
            new("--space-card-padding", spacing.Item1, "appearance.density"),
            new("--space-action-primary-padding", spacing.Item2, "appearance.density"),
            new("--shadow-card-elevation", input.Components.CardVariant == "elevated" ? "0 8px 24px rgba(16,34,29,.14)" : "none", "components.cardVariant"),
            new("--color-card-border", input.Components.CardVariant == "outlined" ? input.Brand.PrimaryColor : "transparent", "components.cardVariant")
        };
        var css = new StringBuilder($"/* Runtime theme · schema {input.SchemaVersion} · tenant {input.TenantKey} */\n:root {{\n");
        foreach (var token in tokens)
            css.AppendLine($"  {token.Name}: {token.Value};");
        css.Append('}');
        var residualCount = input.LegacyExceptions.Sum(item => item.DeclarationCount);
        return new ThemeCompilationResult(
            input,
            tokens,
            css.ToString(),
            diagnostics,
            diagnostics.All(item => item.Severity != "error"),
            residualCount);
    }

    public MigratedThemeProjectionResult Project(MigratedThemeProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var preset = NewFromPreset("clean", request.TenantKey, request.DisplayName);
        var notes = new List<ThemeDiagnostic>();
        var mappings = new List<ThemeProjectionMapping>();
        var mappedDefinitionNames = new HashSet<string>(StringComparer.Ordinal);
        string Pick(string fallback, string targetPath, string friendlyField, params string[] candidates)
        {
            var definition = request.TokenDefinitions.FirstOrDefault(item =>
                item.SourceCandidateNames.Any(candidate => candidates.Contains(candidate, StringComparer.Ordinal)));
            if (definition is null)
            {
                notes.Add(new("info", "projection.default-used", $"{friendlyField} was not found in migrated evidence; the clean preset default was used."));
                mappings.Add(new(targetPath, friendlyField, "default-used", fallback, null, null,
                    "No resolved tenant token matched this ThemeConfiguration v1 field, so the governed default remains."));
                return fallback;
            }
            if (friendlyField.Contains("color", StringComparison.OrdinalIgnoreCase) && !HexColorRegex().IsMatch(definition.Value))
            {
                notes.Add(new("warning", "projection.color.unsupported", $"{friendlyField} used '{definition.Value}', which is not editable by the simple color picker; the preset default was used and the source remains evidenced."));
                mappings.Add(new(targetPath, friendlyField, "unsupported-value", fallback, definition.Name, definition.Value,
                    "A matching token exists, but its value cannot be represented by the current control. The governed default remains and the token stays unmapped."));
                return fallback;
            }
            mappedDefinitionNames.Add(definition.Name);
            mappings.Add(new(targetPath, friendlyField, "mapped", definition.Value, definition.Name, definition.Value,
                $"Mapped from {string.Join(", ", definition.SourceCandidateNames)}."));
            notes.Add(new("info", "projection.evidence-mapped", $"{friendlyField} was mapped from {string.Join(", ", definition.SourceCandidateNames)}."));
            return definition.Value;
        }

        void RecordDiscreteMapping(TenantTokenDefinition? definition, string targetPath, string label, string appliedValue)
        {
            if (definition is null)
            {
                mappings.Add(new(targetPath, label, "default-used", appliedValue, null, null,
                    "ThemeConfiguration v1 has this field, but no resolved tenant token matched its mapping rule."));
                return;
            }
            mappedDefinitionNames.Add(definition.Name);
            mappings.Add(new(targetPath, label, "mapped", appliedValue, definition.Name, definition.Value,
                $"The measured value '{definition.Value}' was translated into the governed option '{appliedValue}'."));
        }

        var brand = preset.Brand with
        {
            PrimaryColor = Pick(preset.Brand.PrimaryColor, "brand.primaryColor", "Primary color", "--color-action-primary-background"),
            AccentColor = Pick(preset.Brand.AccentColor, "brand.accentColor", "Accent color", "--color-floating-action-background", "--color-action-secondary-background"),
            BackgroundColor = Pick(preset.Brand.BackgroundColor, "brand.backgroundColor", "Background color", "--color-global-background", "--color-page-background"),
            SurfaceColor = Pick(preset.Brand.SurfaceColor, "brand.surfaceColor", "Surface color", "--color-card-background"),
            TextColor = Pick(preset.Brand.TextColor, "brand.textColor", "Text color", "--color-global-foreground", "--color-page-foreground"),
            FontFamily = Pick(preset.Brand.FontFamily, "brand.fontFamily", "Font family", "--font-global-font-family")
        };
        var cardRadius = FindDefinition(request, "--radius-card-corner");
        var cornerStyle = ParsePixels(cardRadius?.Value) switch
        {
            <= 2 => "square",
            >= 16 => "rounded",
            _ => "soft"
        };
        var cardPadding = FindDefinition(request, "--space-card-padding");
        var density = ParsePixels(cardPadding?.Value) switch
        {
            <= 12 => "compact",
            >= 20 => "spacious",
            _ => "comfortable"
        };
        var cardVariant = FindDefinition(request, "--shadow-card-elevation") is not null
            ? "elevated"
            : FindDefinition(request, "--color-card-border") is not null ? "outlined" : "flat";
        RecordDiscreteMapping(cardRadius, "appearance.cornerStyle", "Corner style", cornerStyle);
        RecordDiscreteMapping(cardPadding, "appearance.density", "Content density", density);
        RecordDiscreteMapping(
            FindDefinition(request, "--shadow-card-elevation") ?? FindDefinition(request, "--color-card-border"),
            "components.cardVariant", "Card variant", cardVariant);
        mappings.Add(new("components.navigationVariant", "Navigation variant", "default-used", preset.Components.NavigationVariant, null, null,
            "No ThemeConfiguration v1 mapping rule currently converts resolved navigation tokens into this controlled option."));
        mappings.Add(new("components.buttonVariant", "Button variant", "default-used", preset.Components.ButtonVariant, null, null,
            "No ThemeConfiguration v1 mapping rule currently converts resolved button-shape tokens into this controlled option."));
        var exceptions = request.Residuals.Count == 0
            ? []
            : new ThemeLegacyException[]
            {
                new("migration-residual", request.Residuals.Count, "Quarantine until promoted to a typed variant or explicitly retired.", "support-and-design-system")
            };
        var configuration = preset with
        {
            Source = "migrated-css",
            BasePreset = "migrated-draft",
            Brand = brand,
            Appearance = new(cornerStyle, density),
            Components = preset.Components with { CardVariant = cardVariant },
            LegacyExceptions = exceptions
        };
        if (request.Residuals.Count > 0)
            notes.Add(new("warning", "projection.residuals.quarantined", $"{request.Residuals.Count} declarations do not fit the friendly theme schema and remain in the legacy exception layer."));
        var compilation = Compile(configuration);
        var unmappedTokens = request.TokenDefinitions
            .Where(item => !mappedDefinitionNames.Contains(item.Name))
            .ToArray();
        var projectionSummary = new ThemeProjectionSummary(
            request.TokenDefinitions.Count,
            mappedDefinitionNames.Count,
            unmappedTokens.Length,
            request.Residuals.Count,
            mappings,
            unmappedTokens);
        return new MigratedThemeProjectionResult(configuration, notes, compilation, projectionSummary);
    }

    private static TenantTokenDefinition? FindDefinition(MigratedThemeProjectionRequest request, string candidate)
        => request.TokenDefinitions.FirstOrDefault(item => item.SourceCandidateNames.Contains(candidate, StringComparer.Ordinal));

    private static IReadOnlyList<ThemeDiagnostic> Validate(TenantThemeConfiguration input)
    {
        var diagnostics = new List<ThemeDiagnostic>();
        if (input.SchemaVersion != "1.0") diagnostics.Add(new("error", "theme.schema.unsupported", "Only theme schema 1.0 is supported."));
        if (string.IsNullOrWhiteSpace(input.TenantKey)) diagnostics.Add(new("error", "theme.tenant.required", "Tenant key is required."));
        foreach (var (field, value) in new[]
        {
            ("primary", input.Brand.PrimaryColor), ("accent", input.Brand.AccentColor),
            ("background", input.Brand.BackgroundColor), ("surface", input.Brand.SurfaceColor), ("text", input.Brand.TextColor)
        })
            if (!HexColorRegex().IsMatch(value)) diagnostics.Add(new("error", "theme.color.invalid", $"{field} must be a six-digit hexadecimal color."));
        if (HexColorRegex().IsMatch(input.Brand.BackgroundColor) && HexColorRegex().IsMatch(input.Brand.TextColor))
        {
            var ratio = ContrastRatio(input.Brand.BackgroundColor, input.Brand.TextColor);
            if (ratio < 4.5) diagnostics.Add(new("error", "theme.contrast.body", $"Background/text contrast is {ratio:0.00}:1; at least 4.5:1 is required for normal text."));
            else diagnostics.Add(new("info", "theme.contrast.body", $"Background/text contrast passes at {ratio:0.00}:1."));
        }
        if (input.LegacyExceptions.Count > 0)
            diagnostics.Add(new("warning", "theme.legacy-exceptions.present", $"This migrated theme still carries {input.LegacyExceptions.Sum(item => item.DeclarationCount)} quarantined legacy declarations."));
        return diagnostics;
    }

    private static string BestForeground(string background)
        => ContrastRatio(background, "#ffffff") >= ContrastRatio(background, "#111111") ? "#ffffff" : "#111111";

    private static double ContrastRatio(string first, string second)
    {
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(string color)
    {
        var channels = new[] { color[1..3], color[3..5], color[5..7] }
            .Select(value => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d)
            .Select(value => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4))
            .ToArray();
        return .2126 * channels[0] + .7152 * channels[1] + .0722 * channels[2];
    }

    private static int ParsePixels(string? value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+");
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : 10;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();
}
