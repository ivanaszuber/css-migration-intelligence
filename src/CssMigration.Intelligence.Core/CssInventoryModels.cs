// File purpose: Defines the immutable report records produced by the CSS inventory parser.
namespace CssMigration.Intelligence.Core;

public sealed record CssSource(string SourceId, string Content);

public sealed record CssInventoryReport(
    string SchemaVersion,
    CssInventorySummary Summary,
    IReadOnlyList<CssSourceInventory> Sources);

public sealed record CssInventorySummary(
    int SourceCount,
    int StyleRuleCount,
    int AtRuleCount,
    int DeclarationCount,
    int ImportantDeclarationCount,
    int DiagnosticCount,
    int ErrorCount,
    int UnsafeConstructCount);

public sealed record CssSourceInventory(
    string SourceId,
    IReadOnlyList<CssRuleInventory> Rules,
    IReadOnlyList<CssAtRuleInventory> AtRules,
    IReadOnlyList<CssDiagnostic> Diagnostics);

public sealed record CssRuleInventory(
    string RuleId,
    string SelectorText,
    IReadOnlyList<CssSelectorInventory> Selectors,
    IReadOnlyList<CssDeclarationInventory> Declarations,
    IReadOnlyList<string> AtRuleContext,
    IReadOnlyList<string> MediaQueries,
    IReadOnlyList<string> AffectedComponents,
    IReadOnlyList<string> Flags,
    CssSourceLocation Location);

public sealed record CssSelectorInventory(
    string Text,
    CssSpecificity Specificity);

public sealed record CssSpecificity(int Ids, int Classes, int Elements)
{
    public int Score => (Ids * 100) + (Classes * 10) + Elements;
}

public sealed record CssDeclarationInventory(
    string Property,
    string Value,
    bool Important,
    string Category,
    IReadOnlyList<string> Flags,
    CssSourceLocation Location);

public sealed record CssAtRuleInventory(
    string Name,
    string Prelude,
    bool HasBlock,
    IReadOnlyList<string> Context,
    IReadOnlyList<string> Flags,
    CssSourceLocation Location);

public sealed record CssDiagnostic(
    string Code,
    string Severity,
    string Message,
    CssSourceLocation Location);

public sealed record CssSourceLocation(int Line, int Column, int Offset);
