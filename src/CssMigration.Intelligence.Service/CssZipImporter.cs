// File purpose: Safely turns an approved ZIP of tenant CSS files into an in-memory portfolio.
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace CssMigration.Intelligence.Service;

public sealed record CssZipImportResponse(
    CssPortfolioInput Portfolio,
    CssPortfolioAnalysisResponse Analysis,
    IReadOnlyList<CssZipTenantMapping> Mappings,
    IReadOnlyList<CssZipTenantFailure> Failures);

public sealed record CssZipTenantMapping(
    string TenantKey,
    string DisplayName,
    IReadOnlyList<string> Files,
    int CssCharacters,
    string InputFormat,
    IReadOnlyList<string> CompatibilityRepairs);

public sealed record CssZipTenantFailure(
    string TenantKey,
    string DisplayName,
    IReadOnlyList<string> Files,
    IReadOnlyList<PortfolioValidationError> Errors);

public sealed partial class CssZipImporter
{
    public const long MaximumArchiveBytes = 50_000_000;
    private const long MaximumExpandedBytes = 200_000_000;
    private const int MaximumEntries = 2_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CssPortfolioAnalyzer _analyzer = new();
    private readonly ILessCompiler _lessCompiler;

    public CssZipImporter(ILessCompiler? lessCompiler = null)
    {
        _lessCompiler = lessCompiler ?? new NodeLessCompiler();
    }

    public CssZipImportResponse Import(Stream stream, IReadOnlyDictionary<string, string>? displayNames = null)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var files = new Dictionary<string, List<StylesheetFile>>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                var path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("/.DS_Store", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith("/", StringComparison.Ordinal) || path.Split('/').Any(part => part is "" or "." or ".."))
                    throw Error("zip.path.invalid", $"Unsafe archive path: {path}");
                var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixMode == 0xA000) throw Error("zip.symlink", $"Symbolic links are not accepted: {path}");
                var isLess = path.EndsWith(".less", StringComparison.OrdinalIgnoreCase);
                if (!isLess && !path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    throw Error("zip.file.unsupported", $"Only CSS and LESS files are accepted: {path}");
                if (!seen.Add(path)) throw Error("zip.file.duplicate", $"Duplicate stylesheet: {path}");
                if (seen.Count > MaximumEntries) throw Error("zip.too_many", "The archive contains too many stylesheets.");
                expanded += entry.Length;
                if (expanded > MaximumExpandedBytes || entry.Length > CssPortfolioValidator.MaximumCssLength)
                    throw Error("zip.too_large", "The archive exceeds the 200 MB expanded limit or one stylesheet exceeds the 5 million character limit.");
                var parts = path.Split('/');
                var tenant = parts.Length == 1 ? Path.GetFileNameWithoutExtension(parts[0]) : parts[0];
                if (string.IsNullOrWhiteSpace(tenant) || tenant.Length > 100)
                    throw Error("zip.tenant.invalid", $"Cannot identify a tenant for {path}");
                using var reader = new StreamReader(entry.Open(), StrictUtf8, detectEncodingFromByteOrderMarks: true);
                var stylesheet = reader.ReadToEnd();
                if (stylesheet.Length > CssPortfolioValidator.MaximumCssLength)
                    throw Error("zip.stylesheet.too_large", $"Stylesheet is too large: {path}");
                if (!files.TryGetValue(tenant, out var group)) files[tenant] = group = [];
                group.Add(new(path, stylesheet, isLess));
            }
            if (files.Count == 0) throw Error("zip.empty", "The archive contains no CSS or LESS files.");
            var now = DateTimeOffset.UtcNow;
            var sources = new List<CssPortfolioSource>();
            var mappings = new List<CssZipTenantMapping>();
            var failures = new List<CssZipTenantFailure>();
            foreach (var item in files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = item.Value.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                var hasLess = ordered.Any(file => file.IsLess);
                var displayName = displayNames is not null && displayNames.TryGetValue(item.Key, out var supplied)
                    ? supplied : FriendlyName(item.Key);
                try
                {
                    var source = string.Join("\n", ordered.Select(file =>
                        $"/* Imported from {file.Path} */\n{RemoveInternalImports(file, ordered)}"));
                    var repairs = new List<string>();
                    if (hasLess) source = NormalizeLegacyLess(source, repairs);
                    var css = hasLess ? _lessCompiler.Compile(source, item.Key) : source;
                    if (css.Length > CssPortfolioValidator.MaximumCssLength)
                        throw Error("zip.tenant.too_large", $"Combined CSS for {item.Key} exceeds the demo limit.");
                    sources.Add(new CssPortfolioSource(item.Key, displayName, $"{item.Key}-archive.css",
                        hasLess ? "zip-less-compiled" : "zip-import", now, css));
                    mappings.Add(new CssZipTenantMapping(item.Key, displayName,
                        ordered.Select(file => file.Path).ToArray(), ordered.Sum(file => file.Content.Length),
                        hasLess ? "LESS compiled to CSS" : "CSS", repairs));
                }
                catch (PortfolioValidationException exception)
                {
                    failures.Add(new CssZipTenantFailure(item.Key, displayName,
                        ordered.Select(file => file.Path).ToArray(), exception.Errors));
                }
            }
            if (sources.Count == 0)
                throw new PortfolioValidationException(failures.SelectMany(failure => failure.Errors).ToArray());
            var portfolio = new CssPortfolioInput("1.0", now, sources);
            return new CssZipImportResponse(portfolio, _analyzer.AnalyzePortfolio(portfolio), mappings, failures);
        }
        catch (InvalidDataException)
        {
            throw Error("zip.invalid", "The selected file is not a valid ZIP archive.");
        }
        catch (DecoderFallbackException)
        {
            throw Error("zip.encoding", "CSS and LESS files must use UTF-8 encoding.");
        }
    }

    private static string RemoveInternalImports(StylesheetFile file, IReadOnlyList<StylesheetFile> tenantFiles)
        => ImportRegex().Replace(file.Content, match =>
        {
            var importPath = match.Groups[1].Value.Replace('\\', '/');
            if (importPath.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
                || importPath.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
                || importPath.StartsWith("//", StringComparison.Ordinal)
                || importPath.StartsWith("/", StringComparison.Ordinal)
                || importPath.Contains("url(", StringComparison.OrdinalIgnoreCase))
                throw Error("less.import.external", $"External or absolute LESS imports are not accepted: {file.Path} -> {importPath}");

            var directory = Path.GetDirectoryName(file.Path)?.Replace('\\', '/') ?? string.Empty;
            var combined = Path.GetFullPath(Path.Combine("/", directory, importPath)).Replace('\\', '/').TrimStart('/');
            var candidates = Path.HasExtension(combined) ? new[] { combined } : new[] { combined + ".less", combined + ".css" };
            if (!candidates.Any(candidate => tenantFiles.Any(item => string.Equals(item.Path, candidate, StringComparison.OrdinalIgnoreCase))))
                throw Error("less.import.missing", $"LESS import was not found inside the same tenant folder: {file.Path} -> {importPath}");
            return $"/* Internal import {importPath} included from the tenant ZIP. */";
        });

    private static PortfolioValidationException Error(string code, string message)
        => new([new PortfolioValidationError(code, message)]);

    private static string FriendlyName(string tenantKey)
        => string.Join(" ", tenantKey.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string NormalizeLegacyLess(string source, ICollection<string> repairs)
    {
        var repaired = DropShadowSpacingRegex().Replace(source, match =>
        {
            if (!repairs.Contains("Removed whitespace between drop-shadow and its opening parenthesis."))
                repairs.Add("Removed whitespace between drop-shadow and its opening parenthesis.");
            return "drop-shadow(";
        });
        return repaired;
    }

    [GeneratedRegex("@import\\s*(?:\\([^)]*\\)\\s*)?[\\\"']([^\\\"']+)[\\\"']\\s*;", RegexOptions.IgnoreCase)]
    private static partial Regex ImportRegex();

    [GeneratedRegex(@"\bdrop-shadow\s+\(", RegexOptions.IgnoreCase)]
    private static partial Regex DropShadowSpacingRegex();

    private sealed record StylesheetFile(string Path, string Content, bool IsLess);
}
