// File purpose: Parses raw CSS into a deterministic inventory of rules, declarations, at-rules, source locations, diagnostics, and safety flags.
using System.Text.RegularExpressions;

namespace CssMigration.Intelligence.Core;

public sealed class CssInventoryEngine
{
    public CssInventoryReport Analyze(IEnumerable<CssSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var inventories = sources
            .OrderBy(source => source.SourceId, StringComparer.Ordinal)
            .Select(source => new CssSourceParser(source).Parse())
            .ToArray();

        var declarations = inventories.SelectMany(source => source.Rules).SelectMany(rule => rule.Declarations).ToArray();
        var diagnostics = inventories.SelectMany(source => source.Diagnostics).ToArray();

        return new CssInventoryReport(
            "1.0.0",
            new CssInventorySummary(
                inventories.Length,
                inventories.Sum(source => source.Rules.Count),
                inventories.Sum(source => source.AtRules.Count),
                declarations.Length,
                declarations.Count(declaration => declaration.Important),
                diagnostics.Length,
                diagnostics.Count(diagnostic => diagnostic.Severity == "error"),
                inventories.SelectMany(source => source.Rules).Sum(rule => rule.Flags.Count(flag => flag.StartsWith("unsafe:", StringComparison.Ordinal)))
                    + declarations.Sum(declaration => declaration.Flags.Count(flag => flag.StartsWith("unsafe:", StringComparison.Ordinal)))
                    + inventories.SelectMany(source => source.AtRules).Sum(rule => rule.Flags.Count(flag => flag.StartsWith("unsafe:", StringComparison.Ordinal)))),
            inventories);
    }
}

internal sealed partial class CssSourceParser
{
    private static readonly HashSet<string> ContainerAtRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "media", "supports", "container", "layer", "scope", "keyframes", "-webkit-keyframes"
    };

    private static readonly HashSet<string> DeclarationAtRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "font-face", "page", "property", "counter-style"
    };

    private static readonly HashSet<string> LayoutProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "display", "grid", "grid-template-columns", "grid-template-rows", "grid-area", "gap",
        "flex", "flex-direction", "flex-wrap", "align-items", "align-content", "justify-content",
        "position", "top", "right", "bottom", "left", "inset", "float", "clear", "overflow",
        "width", "min-width", "max-width", "height", "min-height", "max-height",
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "transform", "z-index"
    };

    private readonly CssSource _source;
    private readonly string _css;
    private readonly List<CssRuleInventory> _rules = [];
    private readonly List<CssAtRuleInventory> _atRules = [];
    private readonly List<CssDiagnostic> _diagnostics = [];

    public CssSourceParser(CssSource source)
    {
        _source = source;
        _css = MaskComments(source.Content ?? string.Empty);
    }

    public CssSourceInventory Parse()
    {
        ParseScope(0, _css.Length, []);
        return new CssSourceInventory(_source.SourceId, _rules, _atRules, _diagnostics);
    }

    private void ParseScope(int start, int end, IReadOnlyList<string> context)
    {
        var cursor = start;
        while (cursor < end)
        {
            cursor = SkipWhitespace(cursor, end);
            if (cursor >= end) return;

            var statementStart = cursor;
            var delimiter = FindNextTopLevelDelimiter(cursor, end);
            if (delimiter < 0)
            {
                AddDiagnostic("CSS001", "error", "Expected '{' or ';' before the end of the source.", statementStart);
                return;
            }

            var prelude = _css[statementStart..delimiter].Trim();
            if (string.IsNullOrWhiteSpace(prelude))
            {
                AddDiagnostic("CSS002", "error", $"Unexpected '{_css[delimiter]}'.", delimiter);
                cursor = delimiter + 1;
                continue;
            }

            if (_css[delimiter] == ';')
            {
                if (prelude.StartsWith('@')) AddStatementAtRule(prelude, statementStart, context);
                else AddDiagnostic("CSS003", "error", $"Declaration-like text '{Shorten(prelude)}' appears outside a rule.", statementStart);
                cursor = delimiter + 1;
                continue;
            }

            var close = FindMatchingBrace(delimiter, end);
            if (close < 0)
            {
                AddDiagnostic("CSS004", "error", $"Rule '{Shorten(prelude)}' has no closing brace.", statementStart);
                return;
            }

            if (prelude.StartsWith('@'))
                ParseBlockAtRule(prelude, statementStart, delimiter + 1, close, context);
            else
                AddStyleRule(prelude, statementStart, delimiter + 1, close, context);

            cursor = close + 1;
        }
    }

    private void AddStatementAtRule(string prelude, int offset, IReadOnlyList<string> context)
    {
        var (name, value) = SplitAtRule(prelude);
        var flags = AtRuleFlags(name, value);
        _atRules.Add(new CssAtRuleInventory(name, value, false, context, flags, Location(offset)));
    }

    private void ParseBlockAtRule(
        string prelude,
        int offset,
        int contentStart,
        int contentEnd,
        IReadOnlyList<string> context)
    {
        var (name, value) = SplitAtRule(prelude);
        var flags = AtRuleFlags(name, value);
        _atRules.Add(new CssAtRuleInventory(name, value, true, context, flags, Location(offset)));

        var nextContext = context.Concat([$"@{name}{(string.IsNullOrWhiteSpace(value) ? string.Empty : $" {value}")}"]).ToArray();
        if (ContainerAtRules.Contains(name))
        {
            ParseScope(contentStart, contentEnd, nextContext);
            return;
        }

        if (DeclarationAtRules.Contains(name))
        {
            AddStyleRule(prelude, offset, contentStart, contentEnd, context);
            return;
        }

        AddDiagnostic("CSS005", "warning", $"Unsupported block at-rule '@{name}' was recorded but its body was not interpreted.", offset);
    }

    private void AddStyleRule(
        string selectorText,
        int offset,
        int contentStart,
        int contentEnd,
        IReadOnlyList<string> context)
    {
        var declarations = ParseDeclarations(contentStart, contentEnd);
        var selectors = SplitSelectorList(selectorText)
            .Select(selector => new CssSelectorInventory(selector, CalculateSpecificity(selector)))
            .ToArray();
        var affectedComponents = selectors
            .SelectMany(selector => ExtractAffectedComponents(selector.Text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var flags = RuleFlags(selectorText, declarations);
        var mediaQueries = context
            .Where(item => item.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
            .Select(item => item[6..].Trim())
            .ToArray();

        _rules.Add(new CssRuleInventory(
            $"{_source.SourceId}:R{_rules.Count + 1}",
            selectorText,
            selectors,
            declarations,
            context,
            mediaQueries,
            affectedComponents,
            flags,
            Location(offset)));
    }

    private IReadOnlyList<CssDeclarationInventory> ParseDeclarations(int start, int end)
    {
        var declarations = new List<CssDeclarationInventory>();
        var cursor = start;
        while (cursor < end)
        {
            cursor = SkipWhitespace(cursor, end);
            if (cursor >= end) break;

            var declarationStart = cursor;
            var colon = FindTopLevelCharacter(cursor, end, ':', ';', '{');
            if (colon < 0)
            {
                var trailing = _css[cursor..end].Trim();
                if (trailing.Length > 0)
                    AddDiagnostic("CSS006", "error", $"Declaration '{Shorten(trailing)}' has no property/value separator.", cursor);
                break;
            }

            if (_css[colon] == '{')
            {
                AddDiagnostic("CSS007", "error", "Nested CSS rules are not supported by inventory schema v1.", declarationStart);
                var close = FindMatchingBrace(colon, end);
                if (close < 0)
                {
                    AddDiagnostic("CSS004", "error", "Nested rule has no closing brace.", declarationStart);
                    break;
                }
                cursor = close + 1;
                continue;
            }

            if (_css[colon] == ';')
            {
                AddDiagnostic("CSS006", "error", $"Declaration '{Shorten(_css[declarationStart..colon].Trim())}' has no property/value separator.", declarationStart);
                cursor = colon + 1;
                continue;
            }

            var property = _css[declarationStart..colon].Trim().ToLowerInvariant();
            var terminator = FindDeclarationTerminator(colon + 1, end);
            var valueEnd = terminator < 0 ? end : terminator;
            var rawValue = _css[(colon + 1)..valueEnd].Trim();
            var importantMatch = ImportantRegex().Match(rawValue);
            var important = importantMatch.Success;
            var value = important ? rawValue[..importantMatch.Index].TrimEnd() : rawValue;

            if (property.Length == 0 || value.Length == 0)
            {
                AddDiagnostic("CSS008", "error", "A CSS declaration has an empty property or value.", declarationStart);
            }
            else
            {
                var flags = DeclarationFlags(property, value, important);
                declarations.Add(new CssDeclarationInventory(
                    property,
                    value,
                    important,
                    LayoutProperties.Contains(property) ? "layout-or-positioning" : "visual-or-behavioural",
                    flags,
                    Location(declarationStart)));
            }

            cursor = terminator < 0 ? end : terminator + 1;
        }

        return declarations;
    }

    private static IReadOnlyList<string> RuleFlags(string selectorText, IReadOnlyList<CssDeclarationInventory> declarations)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        if (Regex.IsMatch(selectorText, @"(^|,)\s*(html|body|:root|\*)\b", RegexOptions.IgnoreCase))
            flags.Add("review:broad-or-global-selector");
        if (declarations.Any(declaration => declaration.Important)) flags.Add("review:important-usage");
        if (declarations.Any(declaration => declaration.Category == "layout-or-positioning")) flags.Add("inventory:layout-or-positioning");
        return flags.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> DeclarationFlags(string property, string value, bool important)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        if (important) flags.Add("review:important");
        if (property is "behavior" or "-moz-binding") flags.Add("unsafe:legacy-executable-property");
        if (value.Contains("expression(", StringComparison.OrdinalIgnoreCase)) flags.Add("unsafe:css-expression");
        if (value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)) flags.Add("unsafe:javascript-url");
        if (value.Contains("data:text/html", StringComparison.OrdinalIgnoreCase)) flags.Add("unsafe:html-data-url");
        if (property == "position" && value.Equals("fixed", StringComparison.OrdinalIgnoreCase)) flags.Add("review:fixed-positioning");
        return flags.OrderBy(flag => flag, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> AtRuleFlags(string name, string prelude)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        if (name.Equals("import", StringComparison.OrdinalIgnoreCase)) flags.Add("unsafe:external-stylesheet-import");
        if (name.Equals("document", StringComparison.OrdinalIgnoreCase)) flags.Add("unsafe:document-scoped-rule");
        if (prelude.Contains("javascript:", StringComparison.OrdinalIgnoreCase)) flags.Add("unsafe:javascript-url");
        return flags.OrderBy(flag => flag, StringComparer.Ordinal).ToArray();
    }

    private static CssSpecificity CalculateSpecificity(string selector)
    {
        var normalized = WhereRegex().Replace(selector, string.Empty);
        var ids = IdRegex().Matches(normalized).Count;
        var classes = ClassRegex().Matches(normalized).Count
            + AttributeRegex().Matches(normalized).Count
            + PseudoClassRegex().Matches(normalized).Count;
        var pseudoElements = PseudoElementRegex().Matches(normalized).Count;
        var withoutDecorators = DecoratorRegex().Replace(normalized, " ");
        var elements = TypeSelectorRegex().Matches(withoutDecorators).Count + pseudoElements;
        return new CssSpecificity(ids, classes, elements);
    }

    private static IEnumerable<string> ExtractAffectedComponents(string selector)
    {
        foreach (Match match in ClassRegex().Matches(selector)) yield return match.Value[1..];
        foreach (Match match in IdRegex().Matches(selector)) yield return $"#{match.Value[1..]}";
        foreach (Match match in DataAttributeRegex().Matches(selector)) yield return $"[{match.Groups[1].Value}]";

        var withoutDecorators = DecoratorRegex().Replace(selector, " ");
        foreach (Match match in TypeSelectorRegex().Matches(withoutDecorators))
            if (!match.Value.Equals("from", StringComparison.OrdinalIgnoreCase)
                && !match.Value.Equals("to", StringComparison.OrdinalIgnoreCase))
                yield return match.Value.ToLowerInvariant();
    }

    private static IReadOnlyList<string> SplitSelectorList(string selectorText)
    {
        var selectors = new List<string>();
        var start = 0;
        var parentheses = 0;
        var brackets = 0;
        char quote = '\0';
        for (var index = 0; index < selectorText.Length; index++)
        {
            var character = selectorText[index];
            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote) quote = '\0';
                continue;
            }
            if (character is '\'' or '"') quote = character;
            else if (character == '(') parentheses++;
            else if (character == ')') parentheses--;
            else if (character == '[') brackets++;
            else if (character == ']') brackets--;
            else if (character == ',' && parentheses == 0 && brackets == 0)
            {
                selectors.Add(selectorText[start..index].Trim());
                start = index + 1;
            }
        }
        selectors.Add(selectorText[start..].Trim());
        return selectors.Where(selector => selector.Length > 0).ToArray();
    }

    private int FindNextTopLevelDelimiter(int start, int end)
    {
        var parentheses = 0;
        var brackets = 0;
        char quote = '\0';
        for (var index = start; index < end; index++)
        {
            var character = _css[index];
            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote) quote = '\0';
                continue;
            }
            if (character is '\'' or '"') quote = character;
            else if (character == '(') parentheses++;
            else if (character == ')') parentheses = Math.Max(0, parentheses - 1);
            else if (character == '[') brackets++;
            else if (character == ']') brackets = Math.Max(0, brackets - 1);
            else if (parentheses == 0 && brackets == 0 && character is '{' or ';') return index;
        }
        return -1;
    }

    private int FindTopLevelCharacter(int start, int end, params char[] targets)
    {
        var parentheses = 0;
        var brackets = 0;
        char quote = '\0';
        for (var index = start; index < end; index++)
        {
            var character = _css[index];
            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote) quote = '\0';
                continue;
            }
            if (character is '\'' or '"') quote = character;
            else if (character == '(') parentheses++;
            else if (character == ')') parentheses = Math.Max(0, parentheses - 1);
            else if (character == '[') brackets++;
            else if (character == ']') brackets = Math.Max(0, brackets - 1);
            else if (parentheses == 0 && brackets == 0 && targets.Contains(character)) return index;
        }
        return -1;
    }

    private int FindDeclarationTerminator(int start, int end)
        => FindTopLevelCharacter(start, end, ';', '{');

    private int FindMatchingBrace(int openingBrace, int end)
    {
        var depth = 0;
        char quote = '\0';
        for (var index = openingBrace; index < end; index++)
        {
            var character = _css[index];
            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote) quote = '\0';
                continue;
            }
            if (character is '\'' or '"') quote = character;
            else if (character == '{') depth++;
            else if (character == '}' && --depth == 0) return index;
        }
        return -1;
    }

    private int SkipWhitespace(int cursor, int end)
    {
        while (cursor < end && char.IsWhiteSpace(_css[cursor])) cursor++;
        return cursor;
    }

    private CssSourceLocation Location(int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < Math.Min(offset, _source.Content.Length); index++)
        {
            if (_source.Content[index] == '\n') { line++; column = 1; }
            else column++;
        }
        return new CssSourceLocation(line, column, offset);
    }

    private void AddDiagnostic(string code, string severity, string message, int offset)
        => _diagnostics.Add(new CssDiagnostic(code, severity, message, Location(offset)));

    private static (string Name, string Prelude) SplitAtRule(string text)
    {
        var trimmed = text.Trim()[1..];
        var separator = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return separator < 0
            ? (trimmed.ToLowerInvariant(), string.Empty)
            : (trimmed[..separator].ToLowerInvariant(), trimmed[(separator + 1)..].Trim());
    }

    private static string MaskComments(string css)
    {
        var characters = css.ToCharArray();
        for (var index = 0; index + 1 < characters.Length; index++)
        {
            if (characters[index] != '/' || characters[index + 1] != '*') continue;
            characters[index++] = ' ';
            characters[index] = ' ';
            while (index + 1 < characters.Length && !(characters[index] == '*' && characters[index + 1] == '/'))
            {
                if (characters[index] != '\n' && characters[index] != '\r') characters[index] = ' ';
                index++;
            }
            if (index + 1 < characters.Length) { characters[index] = ' '; characters[index + 1] = ' '; }
        }
        return new string(characters);
    }

    private static string Shorten(string value) => value.Length <= 60 ? value : $"{value[..57]}...";

    [GeneratedRegex(@"\s*!important\s*$", RegexOptions.IgnoreCase)] private static partial Regex ImportantRegex();
    [GeneratedRegex(@":where\([^)]*\)", RegexOptions.IgnoreCase)] private static partial Regex WhereRegex();
    [GeneratedRegex(@"#[a-zA-Z_][\w-]*")] private static partial Regex IdRegex();
    [GeneratedRegex(@"\.[a-zA-Z_][\w-]*")] private static partial Regex ClassRegex();
    [GeneratedRegex(@"\[[^\]]+\]")] private static partial Regex AttributeRegex();
    [GeneratedRegex(@"\[\s*(data-[\w-]+)", RegexOptions.IgnoreCase)] private static partial Regex DataAttributeRegex();
    [GeneratedRegex(@"(?<!:):(?!:)[a-zA-Z-]+(?:\([^)]*\))?")] private static partial Regex PseudoClassRegex();
    [GeneratedRegex(@"::[a-zA-Z-]+")] private static partial Regex PseudoElementRegex();
    [GeneratedRegex(@"#[\w-]+|\.[\w-]+|\[[^\]]+\]|::?[\w-]+(?:\([^)]*\))?")] private static partial Regex DecoratorRegex();
    [GeneratedRegex(@"(?<![-\w])(?:[a-zA-Z][\w-]*|[a-zA-Z][\w-]*\|[a-zA-Z][\w-]*)(?![-\w])")] private static partial Regex TypeSelectorRegex();
}
