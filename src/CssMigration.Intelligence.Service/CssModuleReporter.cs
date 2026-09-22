// File purpose: Measures proposed and approved declaration coverage by module across tenants.
using CssMigration.Intelligence.Core;

namespace CssMigration.Intelligence.Service;

public sealed class CssModuleReporter
{
    private readonly CssInventoryEngine _inventory = new();

    public IReadOnlyList<ModuleMigrationCoverage> CandidateCoverage(
        CssPortfolioInput input, DesignTokenPlan plan)
    {
        var selected = plan.Candidates.Where(item => item.SelectedForTarget)
            .Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var residuals = plan.TenantResiduals.ToDictionary(item => item.TenantKey, StringComparer.Ordinal);
        return Measure(input, (tenant, rule, declaration) =>
        {
            var name = DesignTokenDiscovery.CandidateNameFor(rule, declaration);
            return name is not null && selected.Contains(name)
                && !residuals[tenant].Items.Any(item => item.Selector == rule.SelectorText
                    && item.Property == declaration.Property && item.Line == declaration.Location.Line);
        });
    }

    public IReadOnlyList<ModuleMigrationCoverage> ApprovedCoverage(
        CssPortfolioInput input, IReadOnlyList<TenantMigrationArtifact> tenants)
    {
        var residuals = tenants.ToDictionary(tenant => tenant.TenantKey,
            tenant => tenant.Residuals.Select(item => $"{item.Selector}\n{item.Property}\n{item.Line}")
                .ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        return Measure(input, (tenant, rule, declaration) =>
            !residuals[tenant].Contains($"{rule.SelectorText}\n{declaration.Property}\n{declaration.Location.Line}"));
    }

    private IReadOnlyList<ModuleMigrationCoverage> Measure(
        CssPortfolioInput input, Func<string, CssRuleInventory, CssDeclarationInventory, bool> covered)
    {
        var rows = input.Sources.SelectMany(source => _inventory.Analyze([new CssSource(source.SourceId, source.Css)])
            .Sources.SelectMany(inventory => inventory.Rules.SelectMany(rule => rule.Declarations.Select(declaration => new
            {
                source.TenantKey,
                Module = CssModuleClassifier.Classify(rule.SelectorText),
                Covered = covered(source.TenantKey, rule, declaration)
            })))).ToArray();

        return CssModuleClassifier.Names.Select(module =>
        {
            var items = rows.Where(item => item.Module == module).ToArray();
            var count = items.Count(item => item.Covered);
            return new ModuleMigrationCoverage(module,
                items.Select(item => item.TenantKey).Distinct(StringComparer.Ordinal).Count(),
                items.Length,
                count,
                items.Length == 0 ? 0 : Math.Round(count * 100m / items.Length, 2),
                items.Length - count);
        }).ToArray();
    }
}
