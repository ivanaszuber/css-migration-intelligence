// File purpose: Rejects empty, duplicated, oversized, or malformed portfolio inputs before analysis begins.
namespace CssMigration.Intelligence.Service;

public sealed class CssPortfolioValidator
{
    public const int MaximumSourceCount = 500;
    public const int MaximumCssLength = 250_000;

    public void Validate(CssPortfolioInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new List<PortfolioValidationError>();

        if (!string.Equals(input.SchemaVersion, "1.0", StringComparison.Ordinal))
            errors.Add(new("portfolio.schema.unsupported", "schemaVersion must be '1.0'."));
        if (input.Sources is null || input.Sources.Count == 0)
            errors.Add(new("portfolio.sources.required", "At least one CSS source is required."));
        else if (input.Sources.Count > MaximumSourceCount)
            errors.Add(new("portfolio.sources.too_many", $"A portfolio may contain at most {MaximumSourceCount} sources."));

        if (input.Sources is not null)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < input.Sources.Count; index++)
            {
                var source = input.Sources[index];
                var prefix = $"sources[{index}]";
                if (string.IsNullOrWhiteSpace(source.TenantKey))
                    errors.Add(new($"{prefix}.tenant_key.required", "tenantKey is required."));
                if (string.IsNullOrWhiteSpace(source.SourceId))
                    errors.Add(new($"{prefix}.source_id.required", "sourceId is required."));
                if (string.IsNullOrWhiteSpace(source.Css))
                    errors.Add(new($"{prefix}.css.required", "css is required."));
                else if (source.Css.Length > MaximumCssLength)
                    errors.Add(new($"{prefix}.css.too_large", $"CSS must not exceed {MaximumCssLength:N0} characters."));

                if (!string.IsNullOrWhiteSpace(source.TenantKey) && !string.IsNullOrWhiteSpace(source.SourceId)
                    && !identities.Add($"{source.TenantKey}\u001f{source.SourceId}"))
                    errors.Add(new($"{prefix}.identity.duplicate", "tenantKey and sourceId must be unique within a portfolio."));
            }
        }

        if (errors.Count > 0) throw new PortfolioValidationException(errors);
    }
}
