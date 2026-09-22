// File purpose: Defines the friendly tenant theme schema, presets, runtime tokens, diagnostics, and governed legacy exceptions.
namespace CssMigration.Intelligence.Service;

public sealed record TenantThemeConfiguration(
    string SchemaVersion,
    string TenantKey,
    string DisplayName,
    string Source,
    string BasePreset,
    ThemeBrandConfiguration Brand,
    ThemeAppearanceConfiguration Appearance,
    ThemeComponentConfiguration Components,
    IReadOnlyList<ThemeLegacyException> LegacyExceptions);

public sealed record ThemeBrandConfiguration(
    string PrimaryColor,
    string AccentColor,
    string BackgroundColor,
    string SurfaceColor,
    string TextColor,
    string FontFamily);

public sealed record ThemeAppearanceConfiguration(string CornerStyle, string Density);

public sealed record ThemeComponentConfiguration(
    string CardVariant,
    string NavigationVariant,
    string ButtonVariant);

public sealed record ThemeLegacyException(
    string Category,
    int DeclarationCount,
    string Treatment,
    string Owner);

public sealed record ThemeTokenValue(string Name, string Value, string SourceControl);

public sealed record ThemeDiagnostic(string Severity, string Code, string Message);

public sealed record ThemeCompilationResult(
    TenantThemeConfiguration Configuration,
    IReadOnlyList<ThemeTokenValue> Tokens,
    string TokensCss,
    IReadOnlyList<ThemeDiagnostic> Diagnostics,
    bool Publishable,
    int LegacyResidualDeclarationCount);

public sealed record ThemePreset(
    string Id,
    string Label,
    string Description,
    ThemeBrandConfiguration Brand,
    ThemeAppearanceConfiguration Appearance,
    ThemeComponentConfiguration Components);

public sealed record MigratedThemeProjectionRequest(
    string TenantKey,
    string DisplayName,
    IReadOnlyList<TenantTokenDefinition> TokenDefinitions,
    IReadOnlyList<MigrationResidualDeclaration> Residuals);

public sealed record MigratedThemeProjectionResult(
    TenantThemeConfiguration Configuration,
    IReadOnlyList<ThemeDiagnostic> ProjectionNotes,
    ThemeCompilationResult Compilation,
    ThemeProjectionSummary ProjectionSummary);

public sealed record ThemeProjectionSummary(
    int ResolvedTokenCount,
    int MappedTokenCount,
    int UnmappedTokenCount,
    int ResidualDeclarationCount,
    IReadOnlyList<ThemeProjectionMapping> Mappings,
    IReadOnlyList<TenantTokenDefinition> UnmappedTokens);

public sealed record ThemeProjectionMapping(
    string TargetPath,
    string Label,
    string Status,
    string AppliedValue,
    string? SourceTokenName,
    string? SourceValue,
    string Explanation);
