// File purpose: Safely turns an approved ZIP of tenant CSS files into an in-memory portfolio.
using System.IO.Compression;
using System.Text;

namespace CssMigration.Intelligence.Service;

public sealed record CssZipImportResponse(
    CssPortfolioInput Portfolio,
    CssPortfolioAnalysisResponse Analysis,
    IReadOnlyList<CssZipTenantMapping> Mappings);

public sealed record CssZipTenantMapping(string TenantKey, string DisplayName, IReadOnlyList<string> Files, int CssCharacters);

public sealed class CssZipImporter
{
    public const long MaximumArchiveBytes = 12_000_000;
    private const long MaximumExpandedBytes = 50_000_000;
    private const int MaximumEntries = 500;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CssPortfolioAnalyzer _analyzer = new();

    public CssZipImportResponse Import(Stream stream, IReadOnlyDictionary<string, string>? displayNames = null)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var files = new Dictionary<string, List<(string Path, string Css)>>(StringComparer.OrdinalIgnoreCase);
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
                if (!path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    throw Error("zip.file.unsupported", $"Only CSS files are accepted: {path}");
                if (!seen.Add(path)) throw Error("zip.file.duplicate", $"Duplicate CSS file: {path}");
                if (seen.Count > MaximumEntries) throw Error("zip.too_many", "The archive contains too many CSS files.");
                expanded += entry.Length;
                if (expanded > MaximumExpandedBytes || entry.Length > CssPortfolioValidator.MaximumCssLength)
                    throw Error("zip.too_large", "The archive or one stylesheet exceeds the demo size limit.");
                var parts = path.Split('/');
                var tenant = parts.Length == 1 ? Path.GetFileNameWithoutExtension(parts[0]) : parts[0];
                if (string.IsNullOrWhiteSpace(tenant) || tenant.Length > 100)
                    throw Error("zip.tenant.invalid", $"Cannot identify a tenant for {path}");
                using var reader = new StreamReader(entry.Open(), StrictUtf8, detectEncodingFromByteOrderMarks: true);
                var css = reader.ReadToEnd();
                if (css.Length > CssPortfolioValidator.MaximumCssLength)
                    throw Error("zip.css.too_large", $"CSS file is too large: {path}");
                if (!files.TryGetValue(tenant, out var group)) files[tenant] = group = [];
                group.Add((path, css));
            }
            if (files.Count == 0) throw Error("zip.empty", "The archive contains no CSS files.");
            var now = DateTimeOffset.UtcNow;
            var sources = files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item =>
            {
                var css = string.Join("\n", item.Value.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(file => $"/* Imported from {file.Path} */\n{file.Css}"));
                if (css.Length > CssPortfolioValidator.MaximumCssLength)
                    throw Error("zip.tenant.too_large", $"Combined CSS for {item.Key} exceeds the demo limit.");
                var displayName = displayNames is not null && displayNames.TryGetValue(item.Key, out var supplied)
                    ? supplied : FriendlyName(item.Key);
                return new CssPortfolioSource(item.Key, displayName, $"{item.Key}-archive.css", "zip-import", now, css);
            }).ToArray();
            var portfolio = new CssPortfolioInput("1.0", now, sources);
            var mappings = files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new CssZipTenantMapping(item.Key, sources.Single(source => source.TenantKey == item.Key).DisplayName,
                    item.Value.Select(file => file.Path).ToArray(), item.Value.Sum(file => file.Css.Length)))
                .ToArray();
            return new CssZipImportResponse(portfolio, _analyzer.AnalyzePortfolio(portfolio), mappings);
        }
        catch (InvalidDataException)
        {
            throw Error("zip.invalid", "The selected file is not a valid ZIP archive.");
        }
        catch (DecoderFallbackException)
        {
            throw Error("zip.encoding", "CSS files must use UTF-8 encoding.");
        }
    }

    private static PortfolioValidationException Error(string code, string message)
        => new([new PortfolioValidationError(code, message)]);

    private static string FriendlyName(string tenantKey)
        => string.Join(" ", tenantKey.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
