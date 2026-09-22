// File purpose: Provides an explicit, conservative module taxonomy for the learning portfolio.
namespace CssMigration.Intelligence.Service;

public static class CssModuleClassifier
{
    public const string Shared = "Shared app";
    public const string Home = "Home & communications";
    public const string Learning = "Learning";
    public const string Operations = "Operations";

    public static readonly IReadOnlyList<string> Names = [Home, Learning, Operations, Shared];

    public static string Classify(string selector)
    {
        var value = selector.ToLowerInvariant();
        // Explicit synthetic module prefixes are authoritative. A mixed selector is shared,
        // because a rule spanning modules cannot safely be assigned to just one PM.
        var matches = new[]
        {
            (Name: Home, Prefix: ".module-home"),
            (Name: Learning, Prefix: ".module-learning"),
            (Name: Operations, Prefix: ".module-operations")
        }.Where(item => value.Contains(item.Prefix, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0].Name : Shared;
    }
}
