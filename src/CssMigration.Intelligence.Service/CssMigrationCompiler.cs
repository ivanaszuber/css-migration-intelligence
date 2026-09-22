// File purpose: Converts approved token and conflict decisions into non-destructive per-tenant token, migrated, residual, and manifest artifacts.
using System.Text;
using System.Text.RegularExpressions;
using CssMigration.Intelligence.Core;

namespace CssMigration.Intelligence.Service;

public sealed partial class CssMigrationCompiler
{
    private readonly CssPortfolioAnalyzer _portfolioAnalyzer = new();
    private readonly CssInventoryEngine _inventoryEngine = new();
    private readonly CssModuleReporter _moduleReporter = new();

    public CssMigrationBundle Compile(CssPortfolioInput input, CssMigrationBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(request);

        var analysis = _portfolioAnalyzer.AnalyzePortfolio(input);
        var approvedTokens = ValidateContract(request, analysis.TokenPlan);
        var conflictResolutions = ValidateConflictResolutions(request, input, approvedTokens);
        var candidateLookup = analysis.TokenPlan.Candidates.ToDictionary(candidate => candidate.Name, StringComparer.Ordinal);
        var residualLookup = analysis.TokenPlan.TenantResiduals.ToDictionary(
            residual => residual.TenantKey,
            residual => residual.Items.ToDictionary(
                item => ResidualKey(item.Selector, item.Property, item.Line),
                item => item,
                StringComparer.Ordinal),
            StringComparer.Ordinal);

        var tenants = input.Sources
            .OrderBy(source => source.TenantKey, StringComparer.Ordinal)
            .Select(source => CompileTenant(
                source,
                request.ContractVersion.Trim(),
                approvedTokens,
                conflictResolutions.Where(resolution => resolution.TenantKey == source.TenantKey).ToArray(),
                candidateLookup,
                residualLookup.GetValueOrDefault(source.TenantKey)))
            .ToArray();
        var totalDeclarations = tenants.Sum(tenant => tenant.TotalDeclarationCount);
        var tokenizedDeclarations = tenants.Sum(tenant => tenant.TokenizedDeclarationCount);

        return new CssMigrationBundle(
            "1.0",
            request.ContractVersion.Trim(),
            DateTimeOffset.UtcNow,
            "synthetic-or-explicitly-approved-input-only-no-source-write",
            new MigrationBundleSummary(
                tenants.Length,
                approvedTokens.Length,
                totalDeclarations,
                tokenizedDeclarations,
                totalDeclarations - tokenizedDeclarations,
                Percentage(tokenizedDeclarations, totalDeclarations),
                tenants.Sum(tenant => tenant.ValueConflicts.Count)),
            approvedTokens,
            tenants,
            _moduleReporter.ApprovedCoverage(input, tenants),
            SharedUiKitPrototype.Css);
    }

    private TenantMigrationArtifact CompileTenant(
        CssPortfolioSource source,
        string contractVersion,
        IReadOnlyList<ApprovedTokenSelection> approvedTokens,
        IReadOnlyList<TenantConflictResolution> conflictResolutions,
        IReadOnlyDictionary<string, DesignTokenCandidate> candidateLookup,
        IReadOnlyDictionary<string, TokenResidualItem>? residualLookup)
    {
        var inventory = _inventoryEngine.Analyze([new CssSource(source.SourceId, source.Css)]).Sources.Single();
        var definitions = new List<TenantTokenDefinition>();
        var conflicts = new List<TenantValueConflict>();
        var appliedResolutions = new List<AppliedTenantConflictResolution>();
        var candidateToToken = new Dictionary<string, string>(StringComparer.Ordinal);
        var splitCandidateValueToToken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rawConflictCandidates = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var token in approvedTokens)
        {
            var explicitValue = token.TenantValues?.FirstOrDefault(value => value.TenantKey == source.TenantKey)?.Value;
            var measuredValues = token.SourceCandidateNames
                .SelectMany(candidateName => candidateLookup[candidateName].Values
                    .Where(value => value.TenantKey == source.TenantKey)
                    .Select(value => value.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var value = !string.IsNullOrWhiteSpace(explicitValue)
                ? explicitValue.Trim()
                : measuredValues.Length == 1
                    ? measuredValues[0]
                    : measuredValues.Length == 0 && !string.IsNullOrWhiteSpace(token.ApprovedDefault)
                        ? token.ApprovedDefault!.Trim()
                        : null;

            if (value is null && measuredValues.Length > 1)
            {
                var resolution = conflictResolutions.FirstOrDefault(item => item.TokenName == token.Name);
                if (resolution is not null)
                {
                    if (resolution.Strategy == "retain-raw")
                    {
                        foreach (var candidateName in token.SourceCandidateNames)
                            rawConflictCandidates[candidateName] = token.Name;
                        appliedResolutions.Add(new AppliedTenantConflictResolution(
                            token.Name,
                            resolution.Strategy,
                            "Conflicting declarations were intentionally retained as raw CSS.",
                            null,
                            [],
                            token.SourceCandidateNames));
                        continue;
                    }

                    if (resolution.Strategy == "merge")
                    {
                        var selectedValue = resolution.SelectedValue?.Trim();
                        if (string.IsNullOrWhiteSpace(selectedValue) || !measuredValues.Contains(selectedValue, StringComparer.OrdinalIgnoreCase))
                            throw ResolutionError(source.TenantKey, token.Name, "Merge must select one of the measured tenant values.");
                        value = measuredValues.First(item => string.Equals(item, selectedValue, StringComparison.OrdinalIgnoreCase));
                        appliedResolutions.Add(new AppliedTenantConflictResolution(
                            token.Name,
                            resolution.Strategy,
                            $"All matching declarations now use {value} through {token.Name}.",
                            value,
                            [],
                            token.SourceCandidateNames));
                    }

                    if (resolution.Strategy == "split")
                    {
                        var splitValues = ValidateSplitResolution(source.TenantKey, token, measuredValues, resolution, definitions);
                        foreach (var split in splitValues)
                        {
                            definitions.Add(new TenantTokenDefinition(
                                split.TokenName,
                                split.OriginalValue,
                                "explicit-conflict-split",
                                token.SourceCandidateNames));
                            foreach (var candidateName in token.SourceCandidateNames)
                                splitCandidateValueToToken[CandidateValueKey(candidateName, split.OriginalValue)] = split.TokenName;
                        }
                        appliedResolutions.Add(new AppliedTenantConflictResolution(
                            token.Name,
                            resolution.Strategy,
                            $"The conflicting values were separated into {splitValues.Length} tenant tokens.",
                            null,
                            splitValues,
                            token.SourceCandidateNames));
                        continue;
                    }
                }
            }

            if (value is null)
            {
                if (measuredValues.Length > 1)
                    conflicts.Add(new TenantValueConflict(
                        token.Name,
                        measuredValues,
                        token.SourceCandidateNames,
                        "Choose an explicit tenant value or split the token before migration."));
                continue;
            }

            if (definitions.Any(definition => definition.Name == token.Name))
                throw ResolutionError(source.TenantKey, token.Name, "A generated split token name collides with an approved token name.");
            definitions.Add(new TenantTokenDefinition(
                token.Name,
                value,
                !string.IsNullOrWhiteSpace(explicitValue)
                    ? "explicit-tenant-override"
                    : measuredValues.Length > 1
                        ? "explicit-conflict-merge"
                        : measuredValues.Length == 1
                            ? "measured-tenant-value"
                            : "approved-default",
                token.SourceCandidateNames));
            foreach (var candidateName in token.SourceCandidateNames)
                candidateToToken[candidateName] = token.Name;
        }

        var replacements = new List<ValueReplacement>();
        var residuals = new List<MigrationResidualDeclaration>();
        foreach (var rule in inventory.Rules)
        foreach (var declaration in rule.Declarations)
        {
            var candidateName = DesignTokenDiscovery.CandidateNameFor(rule, declaration);
            string? approvedName = null;
            if (candidateName is not null)
            {
                splitCandidateValueToToken.TryGetValue(CandidateValueKey(candidateName, declaration.Value), out approvedName);
                if (approvedName is null)
                    candidateToToken.TryGetValue(candidateName, out approvedName);
            }
            if (approvedName is not null)
            {
                var range = FindValueRange(source.Css, declaration);
                if (range is not null)
                {
                    replacements.Add(new ValueReplacement(range.Value.Start, range.Value.Length, $"var({approvedName})"));
                    continue;
                }
            }

            var unsafeDeclaration = rule.Flags.Concat(declaration.Flags)
                .Any(flag => flag.StartsWith("unsafe:", StringComparison.Ordinal));
            var key = ResidualKey(rule.SelectorText, declaration.Property, declaration.Location.Line);
            TokenResidualItem? advice = null;
            residualLookup?.TryGetValue(key, out advice);
            var conflictedToken = candidateName is null
                ? null
                : conflicts.FirstOrDefault(conflict => conflict.SourceCandidateNames.Contains(candidateName, StringComparer.Ordinal));
            var retainedRawToken = candidateName is not null && rawConflictCandidates.TryGetValue(candidateName, out var rawTokenName)
                ? rawTokenName
                : null;
            residuals.Add(new MigrationResidualDeclaration(
                rule.SelectorText,
                declaration.Property,
                declaration.Value,
                declaration.Location.Line,
                conflictedToken is null
                    ? retainedRawToken is null
                        ? advice?.Reason ?? "This declaration is outside the approved token contract."
                        : $"Human decision retained conflicting token {retainedRawToken} as raw CSS."
                    : $"Approved token {conflictedToken.TokenName} has conflicting tenant values.",
                conflictedToken?.Resolution
                    ?? (retainedRawToken is null ? advice?.SuggestedStrategy : "Recorded retain-raw decision; no manual source edit is required.")
                    ?? "Review and classify this declaration manually.",
                unsafeDeclaration));
        }

        var tokensCss = RenderTokensCss(contractVersion, definitions);
        var transformedSource = ApplyReplacements(source.Css, replacements);
        var migratedCss = $"/* Generated migration copy. Original source remains unchanged. Contract {contractVersion}. */\n{tokensCss}\n\n{transformedSource}";
        var tokenizedCount = replacements.Count;
        var totalCount = inventory.Rules.Sum(rule => rule.Declarations.Count);
        var status = residuals.Count == 0
            ? "ready"
            : conflicts.Count > 0
                ? "value-conflict"
                : appliedResolutions.Any(resolution => resolution.Strategy == "retain-raw")
                    ? "residual-review"
                : tokenizedCount == 0
                    ? "not-started"
                    : "residual-review";

        return new TenantMigrationArtifact(
            source.TenantKey,
            source.DisplayName,
            source.SourceId,
            source.Version,
            contractVersion,
            status,
            totalCount,
            tokenizedCount,
            totalCount - tokenizedCount,
            residuals.Count(residual => residual.Unsafe),
            Percentage(tokenizedCount, totalCount),
            source.Css,
            tokensCss,
            migratedCss,
            RenderResidualCss(residuals),
            definitions,
            conflicts,
            appliedResolutions,
            residuals);
    }

    private static TenantConflictResolution[] ValidateConflictResolutions(
        CssMigrationBundleRequest request,
        CssPortfolioInput input,
        IReadOnlyList<ApprovedTokenSelection> approvedTokens)
    {
        var errors = new List<PortfolioValidationError>();
        var tenantKeys = input.Sources.Select(source => source.TenantKey).ToHashSet(StringComparer.Ordinal);
        var tokenNames = approvedTokens.Select(token => token.Name).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var resolutions = (request.ConflictResolutions ?? [])
            .Select(resolution => resolution with
            {
                TenantKey = resolution.TenantKey?.Trim() ?? string.Empty,
                TokenName = resolution.TokenName?.Trim() ?? string.Empty,
                Strategy = resolution.Strategy?.Trim().ToLowerInvariant() ?? string.Empty
            })
            .ToArray();

        foreach (var resolution in resolutions)
        {
            if (!tenantKeys.Contains(resolution.TenantKey))
                errors.Add(new("migration.resolution.tenant.unknown", $"Conflict resolution cites unknown tenant '{resolution.TenantKey}'."));
            if (!tokenNames.Contains(resolution.TokenName))
                errors.Add(new("migration.resolution.token.unknown", $"Conflict resolution cites unapproved token '{resolution.TokenName}'."));
            if (!seen.Add($"{resolution.TenantKey}\n{resolution.TokenName}"))
                errors.Add(new("migration.resolution.duplicate", $"Conflict resolution for tenant '{resolution.TenantKey}' and token '{resolution.TokenName}' is duplicated."));
            if (resolution.Strategy is not ("merge" or "split" or "retain-raw"))
                errors.Add(new("migration.resolution.strategy.invalid", $"Resolution strategy '{resolution.Strategy}' must be merge, split, or retain-raw."));
        }

        if (errors.Count > 0) throw new PortfolioValidationException(errors);
        return resolutions;
    }

    private static SplitTokenValue[] ValidateSplitResolution(
        string tenantKey,
        ApprovedTokenSelection token,
        IReadOnlyList<string> measuredValues,
        TenantConflictResolution resolution,
        IReadOnlyList<TenantTokenDefinition> existingDefinitions)
    {
        var splitValues = (resolution.SplitValues ?? [])
            .Select(item => new SplitTokenValue(item.OriginalValue?.Trim() ?? string.Empty, item.TokenName?.Trim() ?? string.Empty))
            .ToArray();
        if (splitValues.Length != measuredValues.Count
            || splitValues.Select(item => item.OriginalValue).Distinct(StringComparer.OrdinalIgnoreCase).Count() != measuredValues.Count
            || measuredValues.Any(value => !splitValues.Any(item => string.Equals(item.OriginalValue, value, StringComparison.OrdinalIgnoreCase))))
            throw ResolutionError(tenantKey, token.Name, "Split must map every measured value exactly once.");
        if (splitValues.Any(item => !CustomPropertyNameRegex().IsMatch(item.TokenName)))
            throw ResolutionError(tenantKey, token.Name, "Every split token must have a valid CSS custom-property name.");
        if (splitValues.Select(item => item.TokenName).Distinct(StringComparer.Ordinal).Count() != splitValues.Length)
            throw ResolutionError(tenantKey, token.Name, "Split token names must be unique for the tenant.");
        if (splitValues.Any(item => existingDefinitions.Any(definition => definition.Name == item.TokenName)))
            throw ResolutionError(tenantKey, token.Name, "A split token name collides with another generated tenant token.");
        return splitValues;
    }

    private static PortfolioValidationException ResolutionError(string tenantKey, string tokenName, string message)
        => new([new("migration.resolution.invalid", $"Tenant '{tenantKey}', token '{tokenName}': {message}")]);

    private static ApprovedTokenSelection[] ValidateContract(CssMigrationBundleRequest request, DesignTokenPlan plan)
    {
        var errors = new List<PortfolioValidationError>();
        if (string.IsNullOrWhiteSpace(request.ContractVersion))
            errors.Add(new("migration.contract.version.required", "Contract version is required."));
        var candidateLookup = plan.Candidates
            .Where(candidate => candidate.SelectedForTarget)
            .ToDictionary(candidate => candidate.Name, StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var claimedCandidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in request.Tokens ?? [])
        {
            var normalizedName = token.Name?.Trim() ?? string.Empty;
            if (!CustomPropertyNameRegex().IsMatch(normalizedName))
                errors.Add(new("migration.token.name.invalid", $"'{token.Name}' is not a valid CSS custom-property name."));
            else if (!names.Add(normalizedName))
                errors.Add(new("migration.token.name.duplicate", $"Token name '{token.Name}' is duplicated."));
            if (token.SourceCandidateNames is null || token.SourceCandidateNames.Count == 0)
                errors.Add(new("migration.token.sources.required", $"Token '{token.Name}' must cite at least one deterministic candidate."));
            else
            {
                foreach (var candidateName in token.SourceCandidateNames.Distinct(StringComparer.Ordinal))
                {
                    if (!candidateLookup.ContainsKey(candidateName))
                        errors.Add(new("migration.token.source.unknown", $"Token '{token.Name}' cites unknown or unselected candidate '{candidateName}'."));
                    else if (!claimedCandidates.Add(candidateName))
                        errors.Add(new("migration.token.source.duplicate", $"Candidate '{candidateName}' is assigned to more than one approved token."));
                }
            }
        }

        if (errors.Count > 0) throw new PortfolioValidationException(errors);
        return (request.Tokens ?? [])
            .Select(token => token with
            {
                Name = token.Name.Trim(),
                Purpose = token.Purpose?.Trim() ?? string.Empty,
                SourceCandidateNames = token.SourceCandidateNames.Distinct(StringComparer.Ordinal).ToArray()
            })
            .OrderBy(token => token.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static (int Start, int Length)? FindValueRange(string css, CssDeclarationInventory declaration)
    {
        var colon = css.IndexOf(':', declaration.Location.Offset);
        if (colon < 0) return null;
        var searchLength = Math.Min(css.Length - colon - 1, declaration.Value.Length + 80);
        if (searchLength <= 0) return null;
        var valueOffset = css.IndexOf(declaration.Value, colon + 1, searchLength, StringComparison.Ordinal);
        return valueOffset < 0 ? null : (valueOffset, declaration.Value.Length);
    }

    private static string ApplyReplacements(string css, IReadOnlyList<ValueReplacement> replacements)
    {
        var output = new StringBuilder(css);
        foreach (var replacement in replacements.OrderByDescending(item => item.Start))
        {
            output.Remove(replacement.Start, replacement.Length);
            output.Insert(replacement.Start, replacement.Value);
        }
        return output.ToString();
    }

    private static string RenderTokensCss(string contractVersion, IReadOnlyList<TenantTokenDefinition> definitions)
    {
        var output = new StringBuilder();
        output.AppendLine($"/* Approved tenant token values · contract {contractVersion} */");
        output.AppendLine(":root {");
        foreach (var definition in definitions.OrderBy(item => item.Name, StringComparer.Ordinal))
            output.AppendLine($"  {definition.Name}: {definition.Value};");
        output.Append('}');
        return output.ToString();
    }

    private static string RenderResidualCss(IReadOnlyList<MigrationResidualDeclaration> residuals)
    {
        var output = new StringBuilder("/* Residual declarations requiring review. */\n");
        foreach (var group in residuals.GroupBy(item => item.Selector))
        {
            output.AppendLine($"{group.Key} {{");
            foreach (var declaration in group)
                output.AppendLine($"  {declaration.Property}: {declaration.Value}; /* line {declaration.Line}: {declaration.Reason} */");
            output.AppendLine("}");
        }
        return output.ToString().TrimEnd();
    }

    private static string ResidualKey(string selector, string property, int line) => $"{selector}\n{property}\n{line}";
    private static string CandidateValueKey(string candidateName, string value) => $"{candidateName}\n{value.Trim()}";
    private static decimal Percentage(int count, int total)
        => total == 0 ? 0m : Math.Round(count * 100m / total, 2, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"^--[a-zA-Z_][a-zA-Z0-9_-]*$")]
    private static partial Regex CustomPropertyNameRegex();

    private sealed record ValueReplacement(int Start, int Length, string Value);
}
